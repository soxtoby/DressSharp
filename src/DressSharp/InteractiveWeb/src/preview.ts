import {diffWithContext} from "./diff-context";
import {FileDiff, preloadHighlighter, type FileDiffOptions, type FileContents} from "@pierre/diffs";
import {Editor} from "@pierre/diffs/edit";
import {DiffsContainerLoaded} from "../node_modules/@pierre/diffs/dist/components/web-components.js";
import {cursorPosition, PreviewRevision, previewPreferences, whitespaceMarkers} from "./preview-state";
import type {ConfigurationSnapshot, PendingEdits} from "./state";
import {findSelectionRules, outputSelection, type LineSelection} from "./selection-rules";

const sample = `using System;

namespace Example
{
    public class Receipt
    {
        public decimal Total(decimal subtotal,decimal tax)
        {
            if(subtotal>0) { return subtotal+tax; }
            else { return 0; }
        }
    }
}
`;

type PreviewResult = {text: string; encoding: string; lineEndings: string; finalNewline: boolean; skippedOccurrences: number; languageVersion: string};

export function overviewMarkerRanges(rows: readonly {top: number; height: number}[], scale: number) {
    const ranges = rows.map(row => ({top: row.top * scale, bottom: (row.top + row.height) * scale, target: row.top}))
        .sort((left, right) => left.top - right.top);
    const merged: typeof ranges = [];
    for (const range of ranges) {
        const previous = merged.at(-1);
        if (previous && range.top <= previous.bottom + 1) previous.bottom = Math.max(previous.bottom, range.bottom);
        else merged.push({...range});
    }
    return merged;
}

class PreviewDiff extends FileDiff {
    override setEditorActiveLine(lineNumber: number | null, options?: Parameters<FileDiff["setEditorActiveLine"]>[1]) {
        super.setEditorActiveLine(lineNumber, {...options, lineNumberOnly: true});
    }

    refresh(source: string, output: string, host: HTMLElement) {
        // Pierre edits additions. Reverse the comparison and visually place source left.
        const oldFile: FileContents = {name: "Preview.cs", lang: "csharp", contents: output};
        const newFile: FileContents = {name: "Preview.cs", lang: "csharp", contents: source};
        const fileDiff = diffWithContext(oldFile, newFile);
        this.hunksRenderer.clearRenderCache();
        if (source.length === 0) {
            this.hunksRenderer.hydrate(fileDiff);
            this.hunksRenderer.beginEditSession(fileDiff);
        }
        this.render({fileDiff, oldFile, newFile, fileContainer: host, forceRender: true});
    }
}

const editorCss = `
:host { --diffs-light-addition-color: #c93742 !important; --diffs-light-deletion-color: #23864d !important;
    --diffs-bg: #faf9f3 !important; background-color: #faf9f3 !important; }
pre[data-diff] { min-height: 100%; font-size: 12px; tab-size: 4; }
pre[data-diff-type="split"] > [data-additions] { grid-column: 1; grid-row: 1; }
pre[data-diff-type="split"] > [data-deletions] { grid-column: 2; grid-row: 1; }
[data-line] { position: relative; }
[data-deletions], [data-deletions] [data-line] { cursor: text; user-select: text; }
[data-deletions] ::selection { color: inherit; background: var(--diffs-editor-selection-bg) !important; }
:host([data-whitespace]) [data-line]::after { content: var(--preview-whitespace) !important; position: absolute;
    top: 0; left: 0; padding-inline: inherit; color: #718078; pointer-events: none; white-space: pre; font: inherit; }
@media (max-width: 700px) {
    pre[data-diff-type="split"] { display: flex; flex-direction: column; }
    pre[data-diff-type="split"] > [data-additions] { order: 1; width: 100%; }
    pre[data-diff-type="split"] > [data-deletions] { order: 2; width: 100%; }
    [data-additions], [data-deletions] { position: relative; padding-top: 30px; }
    [data-additions]::before, [data-deletions]::before { position: absolute; top: 0; left: 0; padding: 8px; }
    [data-additions]::before { content: "Source · editable"; color: #a92f38; }
    [data-deletions]::before { content: "Formatted output · read only"; color: #1f6548; }
}`;

export class Preview {
    readonly element = document.createElement("section");
    private readonly host = document.createElement("diffs-container");
    private readonly scroll: HTMLElement;
    private readonly overview: HTMLElement;
    private readonly horizontalScroll = document.createElement("div");
    private readonly horizontalTrack = document.createElement("div");
    private readonly status = document.createElement("span");
    private readonly cursor = document.createElement("span");
    private readonly facts = document.createElement("span");
    private readonly revision = new PreviewRevision();
    private readonly diff: PreviewDiff;
    private editor: Editor<"file-diff">;
    private source = sample;
    private output = this.source;
    private preferences = "";
    private timer = 0;
    private controller: AbortController | undefined;
    private stale = true;
    private markerFrame = 0;
    readonly selectionAction = document.createElement("button");
    readonly selectionSummary = document.createElement("span");
    private selection: LineSelection | undefined;
    private selectionRulesPinned = false;
    private analysisController: AbortController | undefined;
    private readonly onSelectionRules: (keys: Set<string> | null, label: string) => void;

    static async create(onSelectionRules: (keys: Set<string> | null, label: string) => void) {
        if (!DiffsContainerLoaded) throw new Error("The preview editor could not load.");
        await preloadHighlighter({themes: ["github-light"], langs: ["csharp"], preferredHighlighter: "shiki-js"});
        return new Preview(onSelectionRules);
    }

    private constructor(onSelectionRules: (keys: Set<string> | null, label: string) => void) {
        this.onSelectionRules = onSelectionRules;
        this.element.className = "canvas preview-canvas";
        this.element.innerHTML = `<div class="preview-toolbar"><div><h1>Preview</h1><small class="parse-context">Latest stable C# · no predefined symbols</small></div><label><input type="checkbox" class="whitespace-toggle"> Whitespace</label></div><div class="preview-frame"><div class="preview-labels"><span>Source · editable · UTF-8 · LF</span><span>Formatted output · read only · select to copy</span></div><div class="diff-viewport"><div class="diff-scroll"></div><div class="diff-overview" role="navigation" aria-label="Differences in preview" hidden></div></div><div class="preview-note"></div></div>`;
        this.scroll = this.element.querySelector(".diff-scroll")!;
        this.overview = this.element.querySelector(".diff-overview")!;
        this.scroll.append(this.host);
        this.selectionAction.className = "quiet find-selection-rules";
        this.selectionSummary.className = "preview-selection-summary";
        this.selectionSummary.textContent = "No preview selection";
        this.selectionAction.textContent = "Show related rules";
        this.selectionAction.title = "Select source or output lines to find settings affecting their formatting. Already-satisfied or overlapping settings may not appear.";
        this.selectionAction.disabled = true;
        this.selectionAction.addEventListener("click", () => void this.analyzeSelection());
        this.selectionAction.setAttribute("aria-label", "Show related rules for the preview selection");
        this.horizontalScroll.className = "diff-horizontal-scroll";
        this.horizontalScroll.tabIndex = 0;
        this.horizontalScroll.setAttribute("role", "region");
        this.horizontalScroll.setAttribute("aria-label", "Scroll source and formatted output horizontally");
        this.horizontalScroll.append(this.horizontalTrack);
        this.element.querySelector(".preview-note")!.before(this.horizontalScroll);
        this.horizontalScroll.addEventListener("scroll", () => {
            if (this.horizontalScroll.scrollLeft !== this.diff.getCodeScrollLeft()) {
                this.diff.setCodeScrollLeft(this.horizontalScroll.scrollLeft);
            }
        });
        this.status.setAttribute("role", "status");
        this.cursor.className = "cursor-position";
        this.cursor.textContent = "Ln 1, Col 1";
        const selectionTools = document.createElement("span");
        selectionTools.className = "preview-selection-tools";
        selectionTools.append(this.selectionSummary, this.selectionAction);
        const details = document.createElement("span");
        details.className = "preview-details";
        details.append(selectionTools, this.cursor, this.facts);
        this.element.querySelector(".preview-note")!.append(this.status, details);
        this.element.querySelector<HTMLInputElement>(".whitespace-toggle")!.addEventListener("change", event => {
            this.host.toggleAttribute("data-whitespace", (event.target as HTMLInputElement).checked);
            this.updateMarkers();
        });
        this.diff = new PreviewDiff(this.options());
        this.editor = this.createEditor();
        this.host.shadowRoot?.addEventListener("scroll", event => {
            const pane = event.target;
            if (pane instanceof HTMLElement && pane.matches("code[data-additions], code[data-deletions]")) {
                this.horizontalScroll.scrollLeft = this.diff.getCodeScrollLeft();
            }
        }, true);
        new ResizeObserver(() => this.queueMarkers()).observe(this.host);
        document.addEventListener("selectionchange", () => queueMicrotask(() => this.updateCursor()));
        const selectionChanged = () => queueMicrotask(() => { this.updateCursor(); this.captureSelection(); });
        this.host.addEventListener("pointerup", selectionChanged);
        this.host.addEventListener("keyup", selectionChanged);
        if (this.host.shadowRoot) new MutationObserver(() => this.queueMarkers()).observe(this.host.shadowRoot, {childList: true, characterData: true, subtree: true});
    }

    mount() {
        // Pierre measures fonts and padding when editing begins; the DOM must be connected.
        this.diff.refresh(this.source, this.output, this.host);
        this.editor.edit(this.diff);
        this.updateCursor();
    }

    private createEditor(initialState?: NonNullable<ConstructorParameters<typeof Editor<"file-diff">>[1]>["initialState"]) {
        return new Editor("file-diff", {...initialState ? {initialState} : {}, onChange: event => {
            this.source = event.file.contents;
            this.clearSelectionRules();
            queueMicrotask(() => this.updateCursor());
            this.schedule();
        }, onFocus: () => queueMicrotask(() => this.updateCursor())});
    }

    private refreshOutput() {
        const state = this.editor.getEditState();
        const view = this.editor.getViewState();
        const focused = this.host.shadowRoot?.activeElement?.matches('[role="textbox"]') ?? false;
        // The formatted baseline changes, but the source document and its undo history do not.
        this.editor.cleanUp();
        this.diff.setOptions(this.options());
        this.diff.refresh(this.source, this.output, this.host);
        this.editor = this.createEditor(state ? {type: "file-diff", document: state.document, fileInfo: state.fileInfo, editor: view} : undefined);
        this.editor.edit(this.diff);
        if (focused) this.editor.focus({preventScroll: true});
        this.editor.setViewState(view);
        this.updateCursor();
    }

    configure(snapshot: ConfigurationSnapshot, edits: PendingEdits) {
        const preferences = JSON.stringify(previewPreferences(snapshot, edits));
        if (preferences === this.preferences) return;
        this.preferences = preferences;
        this.schedule();
    }

    private options(): FileDiffOptions<undefined, undefined> {
        return {diffStyle: "split", expandUnchanged: true, disableFileHeader: true, theme: "github-light", themeType: "light",
            preferredHighlighter: "shiki-js", overflow: "scroll", unsafeCSS: editorCss,
            disableBackground: this.stale, diffIndicators: "none", lineDiffType: this.stale ? "none" : "char",
            onPostRender: () => this.queueMarkers()};
    }

    private schedule() {
        this.analysisController?.abort();
        this.selectionAction.textContent = "Show related rules";
        this.selectionAction.disabled = true;
        const revision = this.revision.next();
        window.clearTimeout(this.timer);
        this.controller?.abort();
        this.stale = true;
        this.status.textContent = "Updating · output outdated";
        // Editor onChange runs before its own render completes.
        queueMicrotask(() => {
            this.diff.setOptions(this.options());
        });
        this.timer = window.setTimeout(() => void this.format(revision), 250);
    }

    private async format(revision: number) {
        const controller = new AbortController();
        this.controller = controller;
        try {
            const response = await fetch("/api/preview", {method: "POST", headers: {"Content-Type": "application/json"},
                body: `{"source":${JSON.stringify(this.source)},"preferences":${this.preferences}}`, signal: controller.signal});
            if (!response.ok) {
                const error = await response.json() as {message: string};
                throw new Error(error.message);
            }
            const result = await response.json() as PreviewResult;
            if (!this.revision.isCurrent(revision)) return;
            if (this.selection?.side === "output")
                this.selection = outputSelection(this.output, result.text, {...this.selection, side: "source"});
            this.output = result.text;
            this.stale = false;
            this.selectionAction.disabled = !this.selection;
            this.status.textContent = result.skippedOccurrences > 0 ? `${result.skippedOccurrences} transformations skipped` : "Up to date";
            this.facts.textContent = `${result.encoding.toUpperCase()} · ${result.lineEndings} · final newline ${result.finalNewline ? "present" : "absent"}`;
            this.element.querySelector(".parse-context")!.textContent = `C# ${result.languageVersion} · no predefined symbols`;
            this.refreshOutput();
        } catch (error) {
            if (!this.revision.isCurrent(revision) || controller.signal.aborted) return;
            this.status.textContent = `Output outdated · ${error instanceof Error ? error.message : "Preview failed"}`;
        }
    }

    private queueMarkers() {
        this.selectionSummary.textContent = this.selection
            ? `${this.selection.side === "source" ? "Source" : "Output"} lines ${this.selection.start + 1}–${Math.max(this.selection.start + 1, this.selection.end)}`
            : "No preview selection";
        this.selectionSummary.title = `Preview selection: ${this.selectionSummary.textContent}`;
        if (this.markerFrame) return;
        this.markerFrame = requestAnimationFrame(() => {
            this.markerFrame = 0;
            this.updateHorizontalScroll();
            this.updateOverview();
            this.updateMarkers();
        });
    }

    private updateHorizontalScroll() {
        const panes = this.host.shadowRoot?.querySelectorAll<HTMLElement>("code[data-additions], code[data-deletions]");
        if (!panes?.length) return;
        const overflow = Math.max(...Array.from(panes, pane => pane.scrollWidth - pane.clientWidth));
        this.horizontalTrack.style.width = `${this.horizontalScroll.clientWidth + overflow}px`;
        this.horizontalScroll.scrollLeft = this.diff.getCodeScrollLeft();
    }

    private updateOverview() {
        this.overview.replaceChildren();
        const hasOverflow = this.scroll.scrollHeight > this.scroll.clientHeight;
        this.overview.hidden = !hasOverflow;
        if (this.stale || !hasOverflow) return;
        const shadow = this.host.shadowRoot;
        if (!shadow) return;
        const scrollBox = this.scroll.getBoundingClientRect();
        const scale = this.overview.clientHeight / this.scroll.scrollHeight;
        const rows = Array.from(shadow.querySelectorAll<HTMLElement>('[data-line-type^="change-"]'), line => {
            const box = line.getBoundingClientRect();
            const top = box.top - scrollBox.top + this.scroll.scrollTop;
            return {top, height: box.height};
        });
        const merged = overviewMarkerRanges(rows, scale);
        merged.forEach((range, index) => {
            const marker = document.createElement("button");
            marker.className = "diff-overview-marker";
            marker.type = "button";
            marker.style.top = `${range.top}px`;
            marker.style.height = `${Math.max(3, range.bottom - range.top)}px`;
            marker.title = `Difference ${index + 1} of ${merged.length}`;
            marker.setAttribute("aria-label", marker.title);
            marker.addEventListener("click", () => this.scroll.scrollTo({top: Math.max(0, range.target - this.scroll.clientHeight * .35), behavior: "smooth"}));
            this.overview.append(marker);
        });
    }

    private updateCursor() {
        const position = cursorPosition(this.editor.getViewState().selections);
        if (position) this.cursor.textContent = position;
    }

    clearSelectionRules() {
        this.analysisController?.abort();
        this.analysisController = undefined;
        this.selectionAction.textContent = "Show related rules";
        this.selection = undefined;
        this.selectionAction.disabled = true;
        this.queueMarkers();
        if (this.selectionRulesPinned) {
            this.selectionRulesPinned = false;
            this.onSelectionRules(null, "");
        }
    }

    private captureSelection() {
        if (this.stale) return;
        const shadow = this.host.shadowRoot;
        if (!shadow) return;
        const native = document.getSelection();
        const range = native?.getComposedRanges?.({shadowRoots: [shadow]})[0]
            ?? (native?.rangeCount ? native.getRangeAt(0) : undefined);
        const lineOf = (node: Node | undefined) => (node instanceof Element ? node : node?.parentElement)?.closest<HTMLElement>("[data-line]");
        const first = lineOf(range?.startContainer);
        const last = lineOf(range?.endContainer);
        let selection: LineSelection | undefined;
        if (first?.closest("[data-deletions]") && last?.closest("[data-deletions]")) {
            if (range && (range.startContainer !== range.endContainer || range.startOffset !== range.endOffset))
                selection = {start: Number(first.dataset["line"]) - 1, end: Number(last.dataset["line"]), side: "output"};
        } else if (first?.closest("[data-deletions]") || last?.closest("[data-deletions]")) {
            return;
        } else {
            const current = this.editor.getViewState().selections?.at(-1);
            if (current && (current.start.line !== current.end.line || current.start.character !== current.end.character)) selection = {start: current.start.line,
                end: current.end.line + (current.end.character === 0 && current.end.line > current.start.line ? 0 : 1), side: "source"};
        }
        if (JSON.stringify(selection) === JSON.stringify(this.selection)) return;
        this.clearSelectionRules();
        this.selection = selection;
        this.selectionAction.disabled = !selection;
        this.queueMarkers();
    }

    private async analyzeSelection() {
        if (this.stale || !this.selection) return;
        this.analysisController?.abort();
        const controller = new AbortController();
        this.analysisController = controller;
        this.selectionAction.disabled = true;
        this.selectionAction.textContent = "Finding…";
        const selection = this.selection;
        try {
            const keys = await findSelectionRules(this.source, this.output, JSON.parse(this.preferences), selection, async preferences => {
                const response = await fetch("/api/preview", {method: "POST", headers: {"Content-Type": "application/json"},
                    body: JSON.stringify({source: this.source, preferences}), signal: controller.signal});
                if (!response.ok) throw new Error((await response.json() as {message: string}).message);
                return (await response.json() as PreviewResult).text;
            }, controller.signal);
            if (controller.signal.aborted) return;
            this.selectionRulesPinned = true;
            this.onSelectionRules(keys, `${selection.side === "source" ? "Source" : "Output"} lines ${selection.start + 1}–${Math.max(selection.start + 1, selection.end)} · pinned`);
        } catch (error) {
            if (!controller.signal.aborted) {
                this.status.textContent = `Selection check failed · ${error instanceof Error ? error.message : "Try again"}`;
                controller.abort();
            }
        } finally {
            if (this.analysisController === controller) {
                this.analysisController = undefined;
                this.selectionAction.textContent = "Show related rules";
                this.selectionAction.disabled = this.stale || !this.selection;
            }
        }
    }

    private updateMarkers() {
        const shadow = this.host.shadowRoot;
        if (!shadow || !this.host.hasAttribute("data-whitespace")) return;
        const sourceEndings = [...this.source.matchAll(/[^\r\n]*(\r\n|\r|\n|$)/g)];
        const outputEndings = [...this.output.matchAll(/[^\r\n]*(\r\n|\r|\n|$)/g)];
        for (const line of shadow.querySelectorAll<HTMLElement>("[data-line]")) {
            const text = line.textContent ?? "";
            const endings = line.closest("[data-additions]") ? sourceEndings : outputEndings;
            const ending = endings[Number(line.dataset["line"]) - 1]?.[1];
            const markers = whitespaceMarkers(text) + (ending === "\r\n" ? "↵" : ending === "\n" ? "↓" : ending === "\r" ? "←" : "");
            line.style.setProperty("--preview-whitespace", JSON.stringify(markers));
        }
    }
}
