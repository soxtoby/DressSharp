import * as vscode from "vscode";
import {DressSharpFormatter} from "./formatter";

export function activate(context: vscode.ExtensionContext) {
    let log = vscode.window.createOutputChannel("DressSharp", {log: true});
    let formatter = new DressSharpFormatter(log);
    context.subscriptions.push(
        log,
        formatter,
        vscode.languages.registerDocumentFormattingEditProvider({language: "csharp", scheme: "file"}, formatter),
        // A text editor command would finish before the formatter answers, since VS Code ignores its result.
        vscode.commands.registerCommand("dresssharp.formatDocument", async () => {
            let document = vscode.window.activeTextEditor?.document;
            if (document) await formatter.formatDocument(document);
        }),
        vscode.commands.registerCommand("dresssharp.useAsDefaultFormatter", () => useAsDefaultFormatter(context.extension.id)),
        vscode.workspace.onDidChangeConfiguration(event => {
            if (event.affectsConfiguration("dresssharp")) formatter.restart();
        }),
    );
}

/** Makes DressSharp the formatter that Format Document and format on save use for C#, and offers to turn on format on save. */
async function useAsDefaultFormatter(extensionId: string) {
    let target = await chooseSettingsTarget();
    if (target == undefined) return;

    await vscode.workspace.getConfiguration("editor", {languageId: "csharp"}).update("defaultFormatter", extensionId, target, true);
    let formatOnSave = vscode.workspace.getConfiguration("editor", {languageId: "csharp"}).get<boolean>("formatOnSave");
    let scope = target == vscode.ConfigurationTarget.Global ? "all workspaces" : "this workspace";
    let choice = await vscode.window.showInformationMessage(
        `DressSharp is now the default C# formatter in ${scope}.`,
        ...(formatOnSave ? [] : ["Format on save"]));
    if (choice)
        await vscode.workspace.getConfiguration("editor", {languageId: "csharp"}).update("formatOnSave", true, target, true);
}

/** The settings to change: the user's, when no folder is open, or else whichever the user picks. */
async function chooseSettingsTarget(): Promise<vscode.ConfigurationTarget | undefined> {
    let choices = [
        {label: "This workspace", description: "Workspace settings", target: vscode.ConfigurationTarget.Workspace},
        {label: "All workspaces", description: "User settings", target: vscode.ConfigurationTarget.Global},
    ];
    return vscode.workspace.workspaceFolders?.length
        ? (await vscode.window.showQuickPick(choices, {placeHolder: "Where should DressSharp be the default C# formatter?"}))?.target
        : vscode.ConfigurationTarget.Global;
}
