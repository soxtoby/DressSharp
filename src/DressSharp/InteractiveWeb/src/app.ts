type Rule = {
    key: string;
    name: string;
    group: string;
    description: string;
    defaultValue: string;
};

type Bootstrap = {
    csrfToken: string;
    requestedConfig: string | null;
    catalog: {
        version: number;
        rules: Rule[];
    };
};

const root = document.querySelector<HTMLElement>("#app")!;

if (!root) 
    throw new Error("Application root is missing.")

start().catch((error: unknown) => {
    const message = error instanceof Error ? error.message : String(error);
    root.replaceChildren(el("main", "fatal", [
        el("span", "brand-mark", ["D#"]),
        el("p", "kicker", ["Startup failed"]),
        el("h1", "", ["DressSharp could not load its local application."]),
        el("pre", "", [message]),
    ]));
});

async function start() {
    const response = await fetch("/api/bootstrap", {cache: "no-store"});
    if (!response.ok) throw new Error(`Bootstrap returned HTTP ${response.status}.`);
    const bootstrap = await response.json() as Bootstrap;
    render(bootstrap);
}

function render(bootstrap: Bootstrap) {
    const rules = bootstrap.catalog.rules;
    const groups = groupRules(rules);
    const list = el("div", "group-list");
    const count = el("span", "rule-count", [`${rules.length} preferences`]);
    const search = document.createElement("input");
    search.type = "search";
    search.placeholder = "Find a preference";
    search.setAttribute("aria-label", "Find a preference");

    const drawRules = (query = "") => {
        list.replaceChildren();
        const normalized = query.trim().toLocaleLowerCase();
        let visible = 0;
        for (const [name, group] of groups) {
            const matches = group.filter(rule => `${rule.name} ${rule.key}`.toLocaleLowerCase().includes(normalized));
            if (matches.length == 0) continue;
            visible += matches.length;
            const details = document.createElement("details");
            details.open = true;
            details.append(el("summary", "", [name, el("span", "", [String(matches.length)])]));
            for (const rule of matches) {
                details.append(el("article", "rule-row", [
                    el("div", "rule-copy", [
                        el("strong", "", [rule.name]),
                        el("code", "", [rule.key]),
                    ]),
                    el("span", "default-value", [rule.defaultValue]),
                ]));
            }
            list.append(details);
        }
        count.textContent = `${visible} ${visible === 1 ? "preference" : "preferences"}`;
    };

    search.addEventListener("input", () => drawRules(search.value));
    drawRules();

    const stop = el("button", "quiet stop", ["Stop server"]);
    stop.addEventListener("click", async () => {
        stop.setAttribute("disabled", "");
        stop.textContent = "Stopping...";
        const response = await fetch("/api/shutdown", {
            method: "POST",
            headers: {"X-DressSharp-CSRF": bootstrap.csrfToken},
        });
        if (!response.ok) {
            stop.removeAttribute("disabled");
            stop.textContent = "Stop server";
            throw new Error(`Shutdown returned HTTP ${response.status}.`);
        }
        document.body.classList.add("stopped");
        stop.textContent = "Server stopped";
        stop.closest("header")?.after(el("div", "stopped-banner", ["DressSharp stopped. You can close this tab."]));
    });

    const target = bootstrap.requestedConfig ?? "Nearest .editorconfig";
    root.replaceChildren(el("div", "shell", [
        el("header", "app-header", [
            el("div", "brand", [el("span", "brand-mark", ["D#"]), el("span", "", ["DressSharp ", el("small", "", ["interactive"])])]),
            el("div", "target", [el("span", "status-dot"), el("span", "", [el("small", "", ["Requested target"]), target])]),
            el("div", "header-actions", [el("span", "catalog-version", [`Catalog v${bootstrap.catalog.version}`]), stop]),
        ]),
        el("main", "workbench-grid", [
            el("aside", "settings-rail", [
                el("div", "rail-heading", [el("div", "", [el("h1", "", ["Rules"]), count])]),
                el("label", "search", [el("span", "", ["⌕"]), search]),
                list,
            ]),
            el("section", "canvas", [
                el("div", "canvas-heading", [
                    el("p", "kicker", ["Local application ready"]),
                    el("h1", "", ["Configuration preview"]),
                    el("p", "lede", ["The server and catalog are running offline. EditorConfig assignments and formatting preview arrive in the dependent workflow tickets."]),
                ]),
                el("div", "preview-frame", [
                    el("div", "preview-labels", [el("span", "", ["Source"]), el("span", "", ["Formatted output"])]),
                    el("div", "preview-columns", [
                        codePane("using System;\n\nclass Receipt\n{\n    decimal Total(decimal subtotal, decimal tax)\n    {\n        return subtotal + tax;\n    }\n}"),
                        codePane("using System;\n\nclass Receipt\n{\n    decimal Total(decimal subtotal, decimal tax) => subtotal + tax;\n}"),
                    ]),
                    el("div", "preview-note", [el("span", "", ["Preview wiring pending"]), "This shell contains no mock formatter or write behavior."]),
                ]),
            ]),
        ]),
    ]));
}

function groupRules(rules: Rule[]) {
    const groups = new Map<string, Rule[]>();
    for (const rule of rules) {
        const group = groups.get(rule.group) ?? [];
        group.push(rule);
        groups.set(rule.group, group);
    }
    return groups;
}

function codePane(source: string) {
    return el("pre", "code-pane", [el("code", "", [source])]);
}

function el<K extends keyof HTMLElementTagNameMap>(
    tag: K,
    className: string,
    children: Array<Node | string> = [],
) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    node.append(...children);
    return node;
}
