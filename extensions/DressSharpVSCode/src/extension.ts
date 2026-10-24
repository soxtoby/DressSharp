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
        vscode.workspace.onDidChangeConfiguration(event => {
            if (event.affectsConfiguration("dresssharp")) formatter.restart();
        }),
    );
}
