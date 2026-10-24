import {basename, relative} from "node:path";
import * as vscode from "vscode";
import {locateDotnet} from "./dotnet-locator";
import {eligibility, type Eligibility} from "./eligibility";
import {FormattingWorker, workerInputs} from "./formatting-worker";
import {lineEdits, type LineEdit} from "./line-edits";
import {run, type ProcessOutput} from "./processes";
import type {ToolManifest} from "./tool-manifest";

/**
 * How a format was asked for. The DressSharp command reports every outcome, while VS Code's own
 * formatting, which also runs on save, keeps quiet about files DressSharp isn't set up for.
 */
type Trigger = "command" | "editor";

/** A file DressSharp is set up to format, and the dotnet executable that runs it. */
interface Target {
    file: string;
    manifest: ToolManifest;
    dotnet: string;
}

let documentationUrl = "https://soxtoby.github.io/DressSharp/vscode/";
let timeoutMs = 60_000;
let restoreTimeoutMs = 5 * 60_000;
let maxReportedLines = 12;

/** Formats C# documents with the DressSharp version each repository installs as a local .NET tool. */
export class DressSharpFormatter implements vscode.DocumentFormattingEditProvider, vscode.Disposable {
    #log: vscode.LogOutputChannel;
    #workers = new Map<string, FormattingWorker>();
    #reportedFailure: string | undefined;
    #reportedWarnings = "";

    constructor(log: vscode.LogOutputChannel) {
        this.#log = log;
    }

    async provideDocumentFormattingEdits(document: vscode.TextDocument, _options: vscode.FormattingOptions, token: vscode.CancellationToken): Promise<vscode.TextEdit[]> {
        let version = document.version;
        let text = document.getText();
        let formatted = await this.#format(document, text, "editor");
        return formatted == undefined || token.isCancellationRequested || document.version != version
            ? []
            : lineEdits(text, formatted).map(textEdit);
    }

    /** The DressSharp command, which formats a document whichever formatter is VS Code's default, and says how it went. */
    async formatDocument(document: vscode.TextDocument) {
        let name = basename(document.fileName);
        let version = document.version;
        let text = document.getText();
        let formatted = await vscode.window.withProgress(
            {location: vscode.ProgressLocation.Window, title: "Formatting with DressSharp"},
            () => this.#format(document, text, "command"));
        let edits = formatted == undefined ? undefined : lineEdits(text, formatted);

        if (edits?.length == 0) {
            vscode.window.setStatusBarMessage(`DressSharp: ${name} already matches your EditorConfig rules`, 5000);
        } else if (edits && document.version != version) {
            vscode.window.setStatusBarMessage(`DressSharp: ${name} changed while it was being formatted, so it was left alone`, 5000);
        } else if (edits) {
            let edit = new vscode.WorkspaceEdit();
            edit.set(document.uri, edits.map(textEdit));
            await vscode.workspace.applyEdit(edit);
            vscode.window.setStatusBarMessage(`DressSharp formatted ${name}`, 5000);
        }
    }

    /** Stops every formatter process once it has answered, so that the next format starts one with the current settings. */
    restart() {
        for (let worker of this.#workers.values())
            void worker.stop();
        this.#workers.clear();
    }

    dispose() {
        for (let worker of this.#workers.values())
            worker.dispose();
        this.#workers.clear();
    }

    async #format(document: vscode.TextDocument, text: string, trigger: Trigger): Promise<string | undefined> {
        let target = await this.#target(document, trigger);
        let output = target && await this.#run(target, text, trigger);
        return target && output && this.#formatted(target, output, trigger);
    }

    async #target(document: vscode.TextDocument, trigger: Trigger): Promise<Target | undefined> {
        let setup = document.uri.scheme == "file" ? await eligibility(document.uri.fsPath) : undefined;
        if (setup?.kind == "eligible") {
            let dotnet = await this.#dotnet(trigger);
            return dotnet ? {file: document.uri.fsPath, manifest: setup.manifest, dotnet} : undefined;
        } else {
            this.#reportSkipped(basename(document.fileName), setup, trigger);
            return undefined;
        }
    }

    #reportSkipped(name: string, setup: Exclude<Eligibility, {kind: "eligible"}> | undefined, trigger: Trigger) {
        let reason = setup?.kind == "noToolManifest" ? `No .config/dotnet-tools.json above ${name} installs DressSharp. Run "dotnet tool install DressSharp" in the repository.`
            : setup?.kind == "noEditorConfig" ? `No .editorconfig applies to ${name}. Run "dotnet dress init" to create one.`
            : `DressSharp formats only C# files saved on disk, and ${name} isn't one.`;
        this.#log.info(`Skipped ${name}: ${reason}`);
        if (trigger == "command")
            void this.#show("warning", reason, {"Learn more": () => vscode.env.openExternal(vscode.Uri.parse(documentationUrl))});
        else
            vscode.window.setStatusBarMessage(`DressSharp isn't set up for ${name}`, 5000);
    }

    async #dotnet(trigger: Trigger): Promise<string | undefined> {
        let location = await locateDotnet(vscode.workspace.getConfiguration("dresssharp").get<string>("dotnetPath"));
        if (location.kind == "found") {
            return location.path;
        } else {
            let message = location.kind == "configuredPathMissing"
                ? `The dotnet executable set in the DressSharp settings doesn't exist: ${location.path}`
                : "dotnet wasn't found on the PATH. Set its location in the DressSharp settings.";
            this.#reportFailure(message, trigger, {"Open settings": openSettings});
            return undefined;
        }
    }

    async #run(target: Target, text: string, trigger: Trigger): Promise<ProcessOutput | undefined> {
        let path = relative(target.manifest.root, target.file).replaceAll("\\", "/");
        let started = performance.now();
        try {
            let output = vscode.workspace.getConfiguration("dresssharp").get("backgroundProcess", true)
                ? await this.#worker(target).format({path, text})
                : await run({executable: target.dotnet, args: ["dress", "format", "--stdin", path], cwd: target.manifest.root}, text, timeoutMs);
            this.#log.info(`Ran DressSharp on ${target.file} in ${Math.round(performance.now() - started)} ms; exit code ${output.exitCode}`);
            return output;
        } catch (error) {
            this.#reportToolFailure(target, error instanceof Error ? error.message : String(error), trigger);
            return undefined;
        }
    }

    #worker(target: Target): FormattingWorker {
        let root = target.manifest.root;
        let worker = this.#workers.get(root)
            ?? new FormattingWorker({executable: target.dotnet, args: ["dress", "format", "--server"], cwd: root}, workerInputs(target.manifest, target.dotnet), timeoutMs);
        this.#workers.set(root, worker);
        return worker;
    }

    #formatted(target: Target, output: ProcessOutput, trigger: Trigger): string | undefined {
        if (output.exitCode == 0) {
            if (output.stderr.trim()) this.#log.info(output.stderr.trim());
            this.#reportedFailure = undefined;
            this.#reportWarnings(output.stderr, trigger);
            return output.stdout;
        } else {
            this.#reportToolFailure(target, output.stderr.trim() || output.stdout.trim() || `DressSharp exited with code ${output.exitCode}.`, trigger);
            return undefined;
        }
    }

    /** Warnings such as an EditorConfig with no DressSharp rules. Each new set is shown once, or every time the command runs. */
    #reportWarnings(stderr: string, trigger: Trigger) {
        let warnings = stderr.split(/\r?\n/)
            .map(line => line.trim())
            .filter(line => line.startsWith("warning: "))
            .map(line => line.slice("warning: ".length))
            .join("\n");
        let isNew = warnings != this.#reportedWarnings;
        if (warnings) {
            this.#reportedWarnings = warnings;
            if (isNew || trigger == "command") void this.#show("warning", warnings);
        }
    }

    #reportToolFailure(target: Target, details: string, trigger: Trigger) {
        this.#log.error(details);
        if (details.includes("dotnet tool restore"))
            this.#reportFailure("The repository's DressSharp tool isn't installed on this machine yet.", trigger, {"Run dotnet tool restore": () => this.#restore(target)});
        else if (details.includes("'--server'") || details.includes("'--stdin'"))
            this.#reportFailure(`The repository's DressSharp version can't format in VS Code. Run "dotnet tool update DressSharp" to update it.`, trigger);
        else
            this.#reportFailure(`DressSharp failed: ${details.split(/\r?\n/).slice(0, maxReportedLines).join("\n")}`, trigger, {"Show output": () => this.#log.show()});
    }

    /** The same failure on every save is reported once, until a format succeeds; the command reports it every time. */
    #reportFailure(message: string, trigger: Trigger, actions: Record<string, () => unknown> = {}) {
        let isNew = message != this.#reportedFailure;
        this.#reportedFailure = message;
        if (isNew || trigger == "command")
            void this.#show("error", message, actions);
    }

    async #restore(target: Target) {
        let output = await vscode.window.withProgress(
            {location: vscode.ProgressLocation.Notification, title: "Restoring .NET tools"},
            () => run({executable: target.dotnet, args: ["tool", "restore"], cwd: target.manifest.root}, "", restoreTimeoutMs)
                .catch((error: unknown): ProcessOutput => ({exitCode: 1, stdout: "", stderr: error instanceof Error ? error.message : String(error)})));
        this.#log.info(output.stdout.trim());
        if (output.exitCode == 0) {
            this.#reportedFailure = undefined;
            void vscode.window.showInformationMessage(`Restored the .NET tools in ${target.manifest.root}. Format the file again to use DressSharp.`);
        } else {
            this.#log.error(output.stderr.trim());
            void this.#show("error", `"dotnet tool restore" failed with exit code ${output.exitCode}.`, {"Show output": () => this.#log.show()});
        }
    }

    async #show(severity: "warning" | "error", message: string, actions: Record<string, () => unknown> = {}) {
        let choice = severity == "warning"
            ? await vscode.window.showWarningMessage(message, ...Object.keys(actions))
            : await vscode.window.showErrorMessage(message, ...Object.keys(actions));
        if (choice) await actions[choice]?.();
    }
}

function openSettings() {
    return vscode.commands.executeCommand("workbench.action.openSettings", "dresssharp.dotnetPath");
}

function textEdit(edit: LineEdit): vscode.TextEdit {
    let range = new vscode.Range(edit.start.line, edit.start.character, edit.end.line, edit.end.character);
    return vscode.TextEdit.replace(range, edit.text);
}
