import {validValue} from "./state";
import {ExampleDiff} from "./example-diff";

export type ExampleRule = {
    key: string; expandedCaption: string; description: string; example: string; defaultValue: string;
    valueKind: string; values: Array<{value: string; label: string}>; minimum: number | null; specialValues: string[];
    examplePreferences: Record<string, string>;
};

// Independent of the editable preview and pending preferences; only catalog data enters here.
export class RuleExamples {
    readonly element = document.createElement("section");
    private controller: AbortController | undefined;
    private triggerId = "";
    private diff: ExampleDiff | undefined;

    constructor() {
        this.element.id = "rule-examples";
        this.element.className = "rule-examples";
        this.element.popover = "auto";
        this.element.setAttribute("role", "dialog");
        this.element.setAttribute("aria-labelledby", "example-title");
        this.element.addEventListener("beforetoggle", event => {
            if ((event as ToggleEvent).newState !== "closed") return;
            this.controller?.abort();
            this.diff?.cleanUp();
            if (this.element.contains(document.activeElement))
                document.getElementById(this.triggerId)?.focus({preventScroll: true});
        });
        document.body.append(this.element);
        window.addEventListener("resize", () => this.position());
    }

    private position() {
        const panel = document.querySelector<HTMLElement>(".preferences");
        const left = window.innerWidth > 900 && panel && !panel.hidden ? panel.getBoundingClientRect().right + 12 : 20;
        this.element.style.setProperty("--example-left", `${left}px`);
    }

    show(rule: ExampleRule, triggerId: string) {
        this.controller?.abort();
        this.diff?.cleanUp();
        const diff = new ExampleDiff();
        this.diff = diff;
        this.triggerId = triggerId;
        const title = node("h2", rule.expandedCaption);
        title.id = "example-title";
        const close = node("button", "Close");
        close.className = "quiet";
        close.addEventListener("click", () => this.element.hidePopover());
        const header = node("header");
        header.append(title, close);
        const key = node("code", rule.key);
        key.className = "example-key";
        const outcome = document.createElement(["boolean", "choice"].includes(rule.valueKind) ? "select" : "input");
        outcome.id = "example-outcome";
        if (outcome instanceof HTMLSelectElement) {
            for (const value of rule.values) {
                const option = node("option", value.label);
                option.value = value.value;
                outcome.append(option);
            }
        }
        outcome.value = rule.defaultValue;
        const label = node("label", "Value");
        label.htmlFor = outcome.id;
        const controls = node("div");
        controls.className = "example-controls";
        controls.append(label, outcome);
        const help = node("p", rule.valueKind === "permutation"
            ? `Order all values, separated by commas: ${rule.values.map(value => value.value).join(", ")}.`
            : rule.valueKind === "multiplechoice"
                ? `Comma-separated values: ${rule.values.map(value => value.value).join(", ")}.${rule.specialValues.length ? ` Or use ${rule.specialValues.join(" or ")} alone.` : ""}`
                : rule.valueKind === "integer"
                    ? `Integer ≥ ${rule.minimum ?? 0}.${rule.specialValues.length ? ` Or use ${rule.specialValues.join(" or ")}.` : ""}`
                    : "");
        help.id = "example-help";
        help.hidden = !help.textContent;
        outcome.setAttribute("aria-describedby", help.id);
        const resultLabel = node("h3", "Output");
        const columns = node("div");
        columns.className = "example-diff";
        const labels = node("div");
        labels.className = "example-labels";
        labels.append(node("h3", "Input code"), resultLabel);
        const scroll = node("div");
        scroll.className = "example-diff-scroll";
        scroll.append(diff.host);
        columns.append(labels, scroll);
        const status = node("p");
        status.className = "example-status";
        status.setAttribute("role", "status");
        const whitespace = document.createElement("input");
        whitespace.type = "checkbox";
        whitespace.checked = ["indent_style", "tab_width", "end_of_line", "trim_trailing_whitespace", "insert_final_newline"].includes(rule.key);
        const whitespaceLabel = node("label");
        whitespaceLabel.className = "example-whitespace";
        whitespaceLabel.append(whitespace, " Whitespace");
        const details = node("div");
        details.className = "example-details";
        details.append(node("span", `Default value: ${rule.defaultValue}.`), whitespaceLabel);
        whitespace.addEventListener("change", () => diff.showWhitespace(whitespace.checked));
        const format = async () => {
            this.controller?.abort();
            const controller = new AbortController();
            this.controller = controller;
            const value = outcome.value.trim();
            resultLabel.textContent = `Output · ${value || "—"}`;
            diff.update(rule.example, rule.example);
            const valid = value.length > 0 && validValue(value, rule);
            outcome.setAttribute("aria-invalid", String(!valid));
            if (!valid) { status.textContent = "Enter a valid value."; return; }
            status.textContent = "Formatting example…";
            try {
                const response = await fetch("/api/preview", {
                    method: "POST", headers: {"Content-Type": "application/json"}, signal: controller.signal,
                    body: JSON.stringify({source: rule.example, preferences: [
                        ...Object.entries(rule.examplePreferences).map(([key, value]) => ({key,
                            local: {kind: "explicit", value}, inherited: {kind: "absent", value: null}})),
                        {key: rule.key, local: {kind: "explicit", value}, inherited: {kind: "absent", value: null}},
                    ]}),
                });
                if (!response.ok) {
                    const error = await response.json() as {message: string};
                    throw new Error(error.message);
                }
                const result = await response.json() as {text: string; encoding: string; lineEndings: string; finalNewline: boolean; skippedOccurrences: number};
                if (controller.signal.aborted) return;
                diff.update(rule.example, result.text);
                status.textContent = (result.text === rule.example ? "Code unchanged · " : "")
                    + `${result.encoding.toUpperCase()} · ${result.lineEndings} · final newline ${result.finalNewline ? "present" : "absent"}`
                    + (result.skippedOccurrences ? ` · ${result.skippedOccurrences} transformations skipped` : "");
            } catch (error) {
                if (!controller.signal.aborted) status.textContent = `Example failed: ${error instanceof Error ? error.message : "Try another value."}`;
            }
        };
        outcome.addEventListener("input", () => void format());
        const supporting = Object.entries(rule.examplePreferences).map(([key, value]) => `${key} = ${value}`).join("; ");
        this.element.replaceChildren(header, key, node("p", rule.description), controls, help,
            details,
            ...(supporting ? [node("p", `Example also uses: ${supporting}.`)] : []), columns, status);
        this.position();
        this.element.showPopover();
        diff.showWhitespace(whitespace.checked);
        outcome.focus({preventScroll: true});
        void format();
    }
}

function node<K extends keyof HTMLElementTagNameMap>(tag: K, text = "") {
    const element = document.createElement(tag);
    element.textContent = text;
    return element;
}
