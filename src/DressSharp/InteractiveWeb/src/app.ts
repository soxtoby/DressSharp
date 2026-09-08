import {desiredAssignment, matchesRule, setEdit, validValue, type Assignment, type ConfigurationSnapshot, type PendingEdits, type PreferenceSnapshot} from "./state";
import {Preview} from "./preview";

type Rule = {key: string; expandedCaption: string; group: string; subgroup: string | null; caption: string; description: string; defaultValue: string; valueKind: string; values: Array<{value: string; label: string}>; minimum: number | null; specialValues: string[]};
type Bootstrap = {csrfToken: string; catalog: {version: number; rules: Rule[]}};

const root = document.querySelector<HTMLElement>("#app")!;
if (!root) throw new Error("Application root is missing.");
let bootstrap: Bootstrap;
let snapshot: ConfigurationSnapshot;
let preview: Preview;
let edits: PendingEdits = new Map();
let query = "";
let saving = false;
let externalChange = false;
let unavailable = false;
let saved = false;
let saveError = "";
let selectionRules: Set<string> | null = null;
let selectionLabel = "";
const touched = new Set<string>();
const expanded = new Set<string>();

start().catch(fatal);

async function start() {
    bootstrap = await getJson<Bootstrap>("/api/bootstrap");
    snapshot = await getJson<ConfigurationSnapshot>("/api/configuration");
    preview = await Preview.create((keys, label) => {
        selectionRules = keys;
        selectionLabel = label;
        render();
    });
    render();
    window.setInterval(poll, 10_000);
    window.addEventListener("focus", poll);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) void poll(); });
}

async function poll() {
    if (document.hidden || saving) return;
    try {
        const latest = await getJson<ConfigurationSnapshot>("/api/configuration");
        unavailable = false;
        externalChange = latest.revision !== snapshot.revision;
    } catch { unavailable = true; }
    render();
}

function render() {
    const openChoices = root.querySelector<HTMLElement>(".multi-options:popover-open");
    const focusedChoice = openChoices?.contains(document.activeElement) ? document.activeElement?.id : null;
    const choicesScroll = openChoices?.scrollTop ?? 0;
    const preferences = new Map(snapshot.preferences.map(preference => [preference.key, preference]));
    const matching = bootstrap.catalog.rules.filter(rule =>
        (selectionRules === null || selectionRules.has(rule.key))
        && (matchesRule(query, rule) || matchesRule(query, {...rule, expandedCaption: `${rule.subgroup ?? ""} ${rule.caption}`})));
    const search = input("search", "Find a preference");
    search.value = query;
    search.addEventListener("input", () => {
        query = search.value;
        render();
        document.querySelector<HTMLInputElement>(".search input")?.focus();
    });
    const list = el("div", "group-list");
    for (const [groupName, rules] of groupRules(matching)) {
        const details = document.createElement("details");
        details.open = selectionRules !== null || query.length > 0 || expanded.has(groupName);
        details.addEventListener("toggle", () => {
            if (query || selectionRules !== null) return;
            if (details.open) expanded.add(groupName); else expanded.delete(groupName);
        });
        details.append(el("summary", "group-heading", [el("span", "", [groupName]), el("span", "group-count", [String(rules.length)])]));
        const subgroups = new Map<string, Rule[]>([["", []]]);
        for (const rule of rules) {
            const subgroup = rule.subgroup ?? "";
            if (!subgroups.has(subgroup)) subgroups.set(subgroup, []);
            subgroups.get(subgroup)!.push(rule);
        }
        for (const [subgroup, subgroupRules] of subgroups) {
            if (!subgroupRules.length) continue;
            const section = subgroup ? el("section", "rule-subgroup", [el("h2", "subgroup-heading", [subgroup])]) : details;
            for (const rule of subgroupRules) section.append(ruleRow(rule, preferences.get(rule.key)!));
            if (subgroup) details.append(section);
        }
        list.append(details);
    }
    if (!matching.length) list.append(el("p", "empty", [selectionRules?.size === 0
        ? "No individual setting changes these lines. Settings already satisfied or overridden by other settings may not appear."
        : "No preferences match."]));
    const relatedCount = el("span", "related-count", [selectionRules === null ? "" : String(selectionRules.size)]);
    relatedCount.title = `${selectionLabel} · ${selectionRules?.size ?? 0} related rules`;
    const clearSelection = el("button", "quiet clear-selection", ["×"]);
    clearSelection.title = "Show all rules and clear preview selection";
    clearSelection.setAttribute("aria-label", clearSelection.title);
    clearSelection.addEventListener("click", () => preview.clearSelectionRules());

    const invalid = hasInvalidEdits();
    const save = el("button", "save", [saveLabel()]);
    setDisabled(save, edits.size === 0 || invalid || unavailable || saving);
    save.addEventListener("click", saveChanges);
    const reload = el("button", "quiet", ["Reload"]);
    setDisabled(reload, unavailable || saving);
    reload.addEventListener("click", reloadConfiguration);
    const notices = el("div", "notices");
    if (externalChange) notices.append(notice("File changed externally.", reload));
    if (unavailable) notices.append(notice("DressSharp cannot read the configuration. It will keep trying."));
    if (saved) notices.append(el("div", "notice saved", ["Saved"]));
    const stop = el("button", "quiet", ["Stop server"]);
    stop.addEventListener("click", stopServer);

    const shell = el("div", "shell", [
        el("header", "app-header", [
            el("div", "brand", [el("span", "brand-mark", ["D#"]), el("span", "", ["DressSharp ", el("small", "", ["interactive"])])]),
            el("div", "target", [el("span", "status-dot"), el("span", "", [el("small", "", ["Target"]), snapshot.targetPath])]),
            el("div", "header-actions", [el("span", "catalog-version", [`Catalog v${bootstrap.catalog.version}`]), stop]),
        ]),
        notices,
        el("main", "workbench-grid", [
            el("aside", "settings-rail", [
                el("div", "rail-toolbar", [el("div", "", [el("h1", "", ["Preferences"]), el("span", "rule-count", [`${bootstrap.catalog.rules.length} rules`]), ...(saveError ? [el("span", "save-error", [saveError])] : [])]), save]),
                el("label", "search", [el("span", "", ["⌕"]), search]),
                el("div", "preview-selection-controls", [
                    preview.selectionSummary,
                    preview.selectionAction,
                    relatedCount,
                    clearSelection,
                ]),
                list,
            ]),
            el("section", "canvas"),
        ]),
    ]);
    if (!root.querySelector(".shell")) {
        shell.querySelector(".canvas")!.replaceWith(preview.element);
        root.replaceChildren(shell);
    } else {
        for (const selector of [".app-header", ".notices", ".settings-rail"]) {
            const current = root.querySelector<HTMLElement>(selector)!;
            const replacement = shell.querySelector<HTMLElement>(selector)!;
            const scrollTop = current.scrollTop;
            current.replaceWith(replacement);
            replacement.scrollTop = scrollTop;
        }
    }
    if (openChoices) {
        const replacement = document.getElementById(openChoices.id);
        replacement?.showPopover();
        if (replacement) replacement.scrollTop = choicesScroll;
        if (focusedChoice) document.getElementById(focusedChoice)?.focus({preventScroll: true});
    }
    preview.configure(snapshot, edits);
}

function ruleRow(rule: Rule, preference: PreferenceSnapshot) {
    const desired = desiredAssignment(edits, preference);
    const changed = edits.has(rule.key);
    const origin = originText(preference);
    const control = createControl(rule, preference, desired, origin);
    const description = rule.valueKind === "multiplechoice"
        ? `${rule.description} ${rule.specialValues.length ? `${rule.specialValues.map(value => `“${value}”`).join(" and ")} must each be used alone. ` : ""}Clearing every option sets this preference to unset.`
        : rule.description;
    control.setAttribute("aria-description", description);
    const name = el("span", "rule-name", [rule.caption]);
    name.title = description;
    const remove = el("button", "icon remove", ["×"]);
    remove.title = `Remove local assignment for ${rule.expandedCaption}`;
    remove.setAttribute("aria-label", remove.title);
    setDisabled(remove, desired.kind === "absent" || saving);
    remove.addEventListener("click", () => change(preference, {kind: "absent", value: null}));
    const unset = el("button", "icon unset", ["∅"]);
    unset.title = `Set ${rule.expandedCaption} to unset`;
    unset.setAttribute("aria-label", unset.title);
    setDisabled(unset, desired.kind === "unset" || saving);
    unset.addEventListener("click", () => change(preference, {kind: "unset", value: null}));
    const copy = el("button", "icon copy", ["⧉"]);
    copy.title = `Copy ${rule.key}`;
    copy.setAttribute("aria-label", copy.title);
    copy.addEventListener("click", async () => {
        try {
            await navigator.clipboard.writeText(rule.key);
            copy.textContent = "✓";
            copy.setAttribute("aria-label", `Copied ${rule.key}`);
            window.setTimeout(() => { copy.textContent = "⧉"; copy.setAttribute("aria-label", copy.title); }, 1200);
        } catch { copy.setAttribute("aria-label", `Could not copy ${rule.key}`); }
    });
    const invalid = desired.kind === "explicit" && touched.has(rule.key) && !validValue(desired.value ?? "", rule);
    return el("article", `rule-row${changed ? " changed" : ""}`, [
        el("div", "rule-copy", [el("div", "rule-title", [name, copy])]),
        el("div", `rule-control${["boolean", "choice"].includes(rule.valueKind) ? " enum-control" : ""}`, [
            control,
            ...(["boolean", "choice"].includes(rule.valueKind) ? [] : [unset]),
            remove,
            ...(invalid ? [el("span", "field-error", ["Invalid value"])] : []),
        ]),
        ...(changed ? [el("span", "sr-only", ["Changed"])] : []),
    ]);
}

function createControl(rule: Rule, preference: PreferenceSnapshot, desired: Assignment, origin: string) {
    if (rule.valueKind === "multiplechoice") return multipleChoiceControl(rule, preference, desired, origin);
    if (["boolean", "choice"].includes(rule.valueKind)) {
        const select = document.createElement("select");
        select.append(option("absent", "Inherited / absent"), option("unset", "Unset"));
        for (const value of rule.values) select.append(option(`explicit:${value.value}`, value.label));
        select.value = desired.kind === "explicit" ? `explicit:${desired.value}` : desired.kind;
        select.title = origin;
        select.setAttribute("aria-label", `${rule.expandedCaption}. ${origin}`);
        setDisabled(select, saving);
        select.addEventListener("change", () => change(preference, parseSelection(select.value)));
        return select;
    }
    const field = document.createElement("input");
    field.type = rule.valueKind === "integer" ? "number" : "text";
    if (rule.minimum !== null) field.min = String(rule.minimum);
    field.value = desired.kind === "explicit" ? desired.value ?? "" : "";
    field.placeholder = preference.inherited.kind === "explicit" ? preference.inherited.value ?? "" : "unset";
    field.title = origin;
    field.setAttribute("aria-label", `${rule.expandedCaption}. ${origin}`);
    field.setAttribute("aria-invalid", String(desired.kind === "explicit" && touched.has(rule.key) && !validValue(desired.value ?? "", rule)));
    setDisabled(field, saving);
    field.addEventListener("input", () => {
        change(preference, {kind: "explicit", value: field.value}, false);
        const row = field.closest(".rule-row");
        row?.classList.toggle("changed", edits.has(rule.key));
        const remove = row?.querySelector<HTMLButtonElement>(".remove");
        if (remove) remove.disabled = false;
        const unset = row?.querySelector<HTMLButtonElement>(".unset");
        if (unset) unset.disabled = false;
    });
    field.addEventListener("blur", () => { touched.add(rule.key); render(); });
    return field;
}

function multipleChoiceControl(rule: Rule, preference: PreferenceSnapshot, desired: Assignment, origin: string) {
    const selected = new Set((desired.kind === "explicit" ? desired.value ?? "" : "").split(",").map(value => value.trim().toLowerCase()).filter(Boolean));
    const choices = [...rule.specialValues.map(value => ({value, label: value.charAt(0).toUpperCase() + value.slice(1)})), ...rule.values];
    const labels = choices.filter(choice => selected.has(choice.value)).map(choice => choice.label);
    const caption = desired.kind === "explicit" ? labels.join(", ") || "Select options" : desired.kind === "unset" ? "Unset" : "Inherited / absent";
    const trigger = el("button", "multi-select", [el("span", "", [caption]), el("span", "", ["▾"])]);
    trigger.title = `${caption}. ${origin}`;
    trigger.setAttribute("aria-label", `${rule.expandedCaption}. ${caption}. ${origin}`);
    setDisabled(trigger, saving);
    const panel = el("div", "multi-options");
    panel.id = `choices-${rule.key}`;
    panel.popover = "auto";
    panel.setAttribute("role", "group");
    panel.setAttribute("aria-label", rule.expandedCaption);
    trigger.popoverTargetElement = panel;
    for (const choice of choices) {
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.id = `${panel.id}-${choice.value}`;
        checkbox.checked = selected.has(choice.value);
        checkbox.disabled = saving;
        checkbox.addEventListener("change", () => {
            if (checkbox.checked) selected.add(choice.value); else selected.delete(choice.value);
            touched.add(rule.key);
            change(preference, selected.size ? {kind: "explicit", value: [...selected].join(",")} : {kind: "unset", value: null});
        });
        panel.append(el("label", "multi-option", [checkbox, choice.label]));
    }
    panel.addEventListener("beforetoggle", event => {
        if ((event as ToggleEvent).newState !== "open") return;
        const bounds = trigger.getBoundingClientRect();
        const width = Math.min(320, window.innerWidth - 16);
        panel.style.width = `${width}px`;
        panel.style.left = `${Math.max(8, Math.min(bounds.right - width, window.innerWidth - width - 8))}px`;
        const below = window.innerHeight - bounds.bottom - 12;
        const above = bounds.top - 12;
        panel.style.maxHeight = `${Math.min(420, Math.max(below, above))}px`;
        panel.style.top = below >= above ? `${bounds.bottom + 4}px` : "auto";
        panel.style.bottom = below >= above ? "auto" : `${window.innerHeight - bounds.top + 4}px`;
    });
    return el("div", "multi-control", [trigger, panel]);
}

function change(preference: PreferenceSnapshot, assignment: Assignment, rerender = true) {
    saved = false;
    edits = setEdit(edits, preference, assignment);
    preview.configure(snapshot, edits);
    if (rerender) render(); else updateSaveState();
}

function hasInvalidEdits() {
    return [...edits].some(([key, assignment]) => assignment.kind === "explicit"
        && !validValue(assignment.value ?? "", bootstrap.catalog.rules.find(rule => rule.key === key)!));
}

function saveLabel() { return `Save ${edits.size} ${edits.size === 1 ? "change" : "changes"}`; }

function updateSaveState() {
    const save = document.querySelector<HTMLButtonElement>(".save");
    if (!save) return;
    save.textContent = saveLabel();
    save.disabled = edits.size === 0 || hasInvalidEdits() || unavailable || saving;
}

async function reloadConfiguration() {
    try { snapshot = await getJson("/api/configuration"); unavailable = false; externalChange = false; }
    catch { unavailable = true; }
    render();
}

async function saveChanges() {
    saving = true; saved = false; saveError = ""; render();
    try {
        snapshot = await getJson("/api/configuration", {method: "POST", headers: {"Content-Type": "application/json", "X-DressSharp-CSRF": bootstrap.csrfToken}, body: JSON.stringify({edits: [...edits].map(([key, assignment]) => ({key, ...assignment}))})});
        edits = new Map(); touched.clear(); externalChange = false; unavailable = false; saved = true; saveError = "";
        window.setTimeout(() => { saved = false; render(); }, 1800);
    } catch (error) { saveError = error instanceof Error ? error.message : "Save failed"; }
    finally { saving = false; render(); }
}

async function stopServer() {
    await getJson("/api/shutdown", {method: "POST", headers: {"X-DressSharp-CSRF": bootstrap.csrfToken}}, true);
    unavailable = true; render();
}

function originText(preference: PreferenceSnapshot) {
    const local = preference.local.kind === "absent" ? "No saved local assignment" : `Saved local: ${preference.local.value ?? "unset"}`;
    const effective = preference.effectiveValue === null ? "Saved effective: unset" : `Saved effective: ${preference.effectiveValue} from ${preference.effectiveSourcePath ?? "EditorConfig"}`;
    return `${local}. ${effective}.`;
}

function parseSelection(value: string): Assignment {
    if (value === "absent") return {kind: "absent", value: null};
    if (value === "unset") return {kind: "unset", value: null};
    return {kind: "explicit", value: value.slice("explicit:".length)};
}

async function getJson<T = ConfigurationSnapshot>(url: string, init?: RequestInit, allowEmpty = false): Promise<T> {
    const response = await fetch(url, {...init, cache: "no-store"});
    if (!response.ok) {
        const error = await response.json().catch(() => ({message: `HTTP ${response.status}`})) as {message: string};
        throw new Error(error.message);
    }
    return allowEmpty ? undefined as T : await response.json() as T;
}

function groupRules(rules: Rule[]) {
    const groups = new Map<string, Rule[]>();
    for (const rule of rules) groups.set(rule.group, [...(groups.get(rule.group) ?? []), rule]);
    return groups;
}

function option(value: string, label: string) { const node = document.createElement("option"); node.value = value; node.textContent = label; return node; }
function input(type: string, placeholder: string) { const node = document.createElement("input"); node.type = type; node.placeholder = placeholder; node.setAttribute("aria-label", placeholder); return node; }
function notice(text: string, action?: HTMLElement) { return el("div", "notice", [el("span", "", [text]), ...(action ? [action] : [])]); }
function setDisabled(node: HTMLButtonElement | HTMLInputElement | HTMLSelectElement, disabled: boolean) { node.disabled = disabled; }
function fatal(error: unknown) { const message = error instanceof Error ? error.message : String(error); root.replaceChildren(el("main", "fatal", [el("span", "brand-mark", ["D#"]), el("h1", "", ["DressSharp could not start."]), el("pre", "", [message])])); }
function el<K extends keyof HTMLElementTagNameMap>(tag: K, className: string, children: Array<Node | string> = []) { const node = document.createElement(tag); if (className) node.className = className; node.append(...children); return node; }
