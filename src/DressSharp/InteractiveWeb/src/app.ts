import {desiredAssignment, matchesRule, setEdit, validValue, type Assignment, type ConfigurationSnapshot, type PendingEdits, type PreferenceSnapshot} from "./state";

type Rule = {key: string; name: string; group: string; description: string; defaultValue: string; valueKind: string; values: Array<{value: string; label: string}>; minimum: number | null; specialValues: string[]};
type Bootstrap = {csrfToken: string; catalog: {version: number; rules: Rule[]}};

const root = document.querySelector<HTMLElement>("#app")!;
if (!root) throw new Error("Application root is missing.");
let bootstrap: Bootstrap;
let snapshot: ConfigurationSnapshot;
let edits: PendingEdits = new Map();
let query = "";
let saving = false;
let externalChange = false;
let unavailable = false;
let saved = false;
let saveError = "";
const touched = new Set<string>();
const expanded = new Set<string>();

start().catch(fatal);

async function start() {
    bootstrap = await getJson<Bootstrap>("/api/bootstrap");
    snapshot = await getJson<ConfigurationSnapshot>("/api/configuration");
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
    const preferences = new Map(snapshot.preferences.map(preference => [preference.key, preference]));
    const matching = bootstrap.catalog.rules.filter(rule => matchesRule(query, rule));
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
        details.open = query.length > 0 || expanded.has(groupName);
        details.addEventListener("toggle", () => {
            if (query) return;
            if (details.open) expanded.add(groupName); else expanded.delete(groupName);
        });
        details.append(el("summary", "group-heading", [el("span", "", [groupName]), el("span", "group-count", [String(rules.length)])]));
        for (const rule of rules) details.append(ruleRow(rule, preferences.get(rule.key)!));
        list.append(details);
    }
    if (!matching.length) list.append(el("p", "empty", ["No preferences match."]));

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

    root.replaceChildren(el("div", "shell", [
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
                list,
            ]),
            el("section", "canvas", [
                el("p", "kicker", ["Configuration workbench"]),
                el("h1", "", ["Shape the code. Keep the file yours."]),
                el("p", "lede", ["Edits stay in this browser until Save. DressSharp merges only changed preferences into the latest EditorConfig."]),
                el("div", "preview-frame", [
                    el("div", "preview-labels", [el("span", "", ["Source"]), el("span", "", ["Formatted output"])]),
                    el("div", "preview-placeholder", [el("span", "preview-mark", ["{ }"]), el("strong", "", ["Preview arrives in SOX-155"]), el("p", "", ["Preference editing and persistence are active now."])]),
                ]),
            ]),
        ]),
    ]));
}

function ruleRow(rule: Rule, preference: PreferenceSnapshot) {
    const desired = desiredAssignment(edits, preference);
    const changed = edits.has(rule.key);
    const origin = originText(preference);
    const control = createControl(rule, preference, desired, origin);
    const remove = el("button", "icon remove", ["×"]);
    remove.title = `Remove local assignment for ${rule.name}`;
    remove.setAttribute("aria-label", remove.title);
    setDisabled(remove, desired.kind === "absent" || saving);
    remove.addEventListener("click", () => change(preference, {kind: "absent", value: null}));
    const unset = el("button", "icon unset", ["∅"]);
    unset.title = `Set ${rule.name} to unset`;
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
        el("div", "rule-copy", [el("div", "rule-title", [el("strong", "", [rule.name]), copy]), el("p", "", [rule.description])]),
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
    if (["boolean", "choice"].includes(rule.valueKind)) {
        const select = document.createElement("select");
        select.append(option("absent", "Inherited / absent"), option("unset", "Unset"));
        for (const value of rule.values) select.append(option(`explicit:${value.value}`, value.label));
        select.value = desired.kind === "explicit" ? `explicit:${desired.value}` : desired.kind;
        select.title = origin;
        select.setAttribute("aria-label", `${rule.name}. ${origin}`);
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
    field.setAttribute("aria-label", `${rule.name}. ${origin}`);
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

function change(preference: PreferenceSnapshot, assignment: Assignment, rerender = true) {
    saved = false;
    edits = setEdit(edits, preference, assignment);
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
    const local = preference.local.kind === "absent" ? "No local assignment" : `Local: ${preference.local.value ?? "unset"}`;
    const effective = preference.effectiveValue === null ? "Effective: unset" : `Effective: ${preference.effectiveValue} from ${preference.effectiveSourcePath ?? "EditorConfig"}`;
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
