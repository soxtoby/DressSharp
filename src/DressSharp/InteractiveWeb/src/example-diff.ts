import {FileDiff, type FileContents} from "@pierre/diffs";
import {diffWithContext} from "./diff-context";
import {whitespaceMarkers} from "./preview-state";

const styles = `
:host { --diffs-bg: #faf9f3 !important; --diffs-light-deletion-color: #c93742 !important;
    --diffs-light-addition-color: #23864d !important; }
pre[data-diff] { font-size: 12px; tab-size: 4; }
[data-line] { position: relative; }
[data-deletions], [data-additions] { user-select: text; cursor: text; }
[data-deletions]:focus-visible, [data-additions]:focus-visible { outline: 2px solid #ef6a3a; outline-offset: -2px; }
:host([data-whitespace]) [data-line]::after { content: var(--example-whitespace); position: absolute;
    top: 0; left: 0; padding-inline: inherit; color: #718078; pointer-events: none; white-space: pre; font: inherit; }
@media (max-width: 700px) {
    pre[data-diff-type="split"] { display: flex; flex-direction: column; }
    [data-deletions], [data-additions] { width: 100%; position: relative; padding-top: 30px; }
    [data-deletions]::before, [data-additions]::before { position: absolute; top: 0; left: 0; padding: 8px; }
    [data-deletions]::before { content: "Input code"; color: #a92f38; }
    [data-additions]::before { content: "Output"; color: #1f6548; }
}`;

export class ExampleDiff extends FileDiff {
    readonly host = document.createElement("diffs-container");
    private source = "";
    private output = "";

    constructor() {
        super({diffStyle: "split", expandUnchanged: true, disableFileHeader: true,
            theme: "github-light", themeType: "light", preferredHighlighter: "shiki-js",
            overflow: "scroll", lineDiffType: "char", diffIndicators: "none", unsafeCSS: styles,
            onPostRender: () => this.decorate()});
    }

    update(source: string, output: string) {
        this.source = source;
        this.output = output;
        const oldFile: FileContents = {name: "Example.cs", lang: "csharp", contents: source};
        const newFile: FileContents = {name: "Example.cs", lang: "csharp", contents: output};
        this.hunksRenderer.clearRenderCache();
        this.render({oldFile, newFile, fileDiff: diffWithContext(oldFile, newFile), fileContainer: this.host, forceRender: true});
    }

    showWhitespace(show: boolean) {
        this.host.toggleAttribute("data-whitespace", show);
        this.decorate();
    }

    private decorate() {
        const shadow = this.host.shadowRoot;
        if (!shadow) return;
        for (const [selector, label, text] of [["[data-deletions]", "Input code", this.source], ["[data-additions]", "Example output", this.output]]) {
            const pane = shadow.querySelector<HTMLElement>(selector!);
            if (!pane) continue;
            pane.setAttribute("role", "region");
            pane.setAttribute("aria-label", label!);
            pane.tabIndex = 0;
            const endings = [...text!.matchAll(/[^\r\n]*(\r\n|\r|\n|$)/g)];
            for (const line of pane.querySelectorAll<HTMLElement>("[data-line]")) {
                const ending = endings[Number(line.dataset["line"]) - 1]?.[1];
                const markers = whitespaceMarkers(line.textContent ?? "")
                    + (ending === "\r\n" ? "↵" : ending === "\n" ? "↓" : ending === "\r" ? "←" : "");
                line.style.setProperty("--example-whitespace", JSON.stringify(markers));
            }
        }
    }
}
