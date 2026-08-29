// PROTOTYPE: Three variants of DressSharp Interactive, switchable via ?variant=.
import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { EditProvider, FileDiff } from "@pierre/diffs/react";
import { Editor } from "@pierre/diffs/edit";
import { parseDiffFromFile } from "@pierre/diffs";
import "./styles.css";

const initialSource = `using System;

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

const groups = [
  {
    name: "Braces & bodies",
    short: "Bodies",
    rules: [
      rule("dress_method_body", "Method body", "expression", ["block", "expression"], "local", "Choose block or expression-bodied methods."),
      rule("dress_namespace_style", "Namespace style", "file_scoped", ["file_scoped", "block_scoped"], "inherited", "Convert namespace declarations between file and block scope."),
      rule("dress_embedded_statement_braces", "Embedded statement braces", "balanced", ["compact", "balanced", "always"], "local", "Keep each if/else chain structurally consistent."),
      rule("dress_embedded_statement_placement", "Embedded statement placement", "next_line", ["same_line", "next_line"], "default", "Place an embedded body beside or below its statement header."),
    ],
  },
  {
    name: "Spacing",
    short: "Spacing",
    rules: [
      rule("csharp_space_after_comma", "After commas", "true", ["true", "false"], "local", "Control the space following commas."),
      rule("csharp_space_around_binary_operators", "Binary operators", "before_and_after", ["before_and_after", "none", "ignore"], "inherited", "Control spacing around binary operators."),
      rule("csharp_space_after_keywords_in_control_flow_statements", "Control-flow keywords", "true", ["true", "false"], "default", "Control the space before a control statement's opening parenthesis."),
    ],
  },
  {
    name: "Wrapping",
    short: "Wrapping",
    rules: [
      rule("max_line_length", "Maximum line length", "120", ["80", "100", "120", "180", "off"], "local", "Set the width used by auto layout rules."),
      rule("dress_arguments_layout", "Argument layout", "auto", ["always_single", "auto", "always_multi"], "inherited", "Lay out method arguments according to an explicit mode."),
      rule("dress_binary_expressions_layout", "Binary expression layout", "auto", ["always_single", "auto", "always_multi"], "default", "Wrap long binary expressions as one planned construct."),
    ],
  },
  {
    name: "File",
    short: "File",
    rules: [
      rule("end_of_line", "Line endings", "lf", ["lf", "crlf", "cr"], "local", "Choose the emitted line-ending sequence."),
      rule("insert_final_newline", "Final newline", "true", ["true", "false"], "inherited", "Control whether changed files end with a newline."),
      rule("trim_trailing_whitespace", "Trailing whitespace", "true", ["true", "false"], "default", "Remove trailing spaces and tabs from changed lines."),
    ],
  },
];

function rule(key, label, value, values, origin, description) {
  return { key, label, value, loaded: value, values, origin, description };
}

const flatRules = groups.flatMap(group => group.rules.map(item => ({ ...item, group: group.name })));
const variantNames = { A: "Workbench", B: "Rule atlas", C: "Code first" };

function App() {
  const params = new URLSearchParams(location.search);
  const variant = ["A", "B", "C"].includes(params.get("variant")) ? params.get("variant") : "A";
  const [rules, setRules] = useState(flatRules);
  const [source, setSource] = useState(initialSource);
  const [selectedKey, setSelectedKey] = useState("dress_embedded_statement_braces");
  const [whitespace, setWhitespace] = useState(false);
  const changed = rules.filter(item => item.value !== item.loaded);
  const formatted = useMemo(() => mockFormat(source, rules), [source, rules]);
  const shared = { rules, setRules, source, setSource, formatted, selectedKey, setSelectedKey, whitespace, setWhitespace, changed };

  useEffect(() => {
    const beforeUnload = event => {
      if (!changed.length && source === initialSource) return;
      event.preventDefault();
    };
    addEventListener("beforeunload", beforeUnload);
    return () => removeEventListener("beforeunload", beforeUnload);
  }, [changed.length, source]);

  return (
    <>
      {variant === "A" && <VariantA {...shared} />}
      {variant === "B" && <VariantB {...shared} />}
      {variant === "C" && <VariantC {...shared} />}
      <PrototypeSwitcher current={variant} />
    </>
  );
}

function VariantA(props) {
  const [exampleKey, setExampleKey] = useState(null);
  const example = props.rules.find(item => item.key === exampleKey);
  return (
    <div className="shell variant-a">
      <Header changed={props.changed} hideSave />
      <main className="workbench-grid">
        <aside className="settings-rail">
          <div className="rail-heading"><div><h1>Rules</h1><p>Effective values for <code>[*.cs]</code></p></div><button className="primary">Save{props.changed.length ? ` · ${props.changed.length}` : ""}</button></div>
          <RuleSearchList {...props} compact onExample={setExampleKey} />
        </aside>
        <section className="canvas">
          <CanvasHeader title="Preview" {...props} />
          <DiffWorkspace {...props} />
          <OutputFacts />
          {example && <div className="example-popover"><button aria-label="Close example" onClick={() => setExampleKey(null)}>×</button><RuleExample item={example} /></div>}
        </section>
      </main>
    </div>
  );
}

function VariantB(props) {
  const selected = props.rules.find(item => item.key === props.selectedKey) ?? props.rules[0];
  const group = groups.find(item => item.name === selected.group) ?? groups[0];
  return (
    <div className="shell variant-b">
      <Header changed={props.changed} compact />
      <main className="atlas">
        <section className="atlas-hero">
          <p className="kicker">Rule atlas · catalog v1</p>
          <h1>Make every formatting decision visible.</h1>
          <p>Browse the catalog, compare outcomes, then test the complete configuration against your own code.</p>
        </section>
        <nav className="group-tabs" aria-label="Preference groups">
          {groups.map(item => <button key={item.name} className={item.name === selected.group ? "active" : ""} onClick={() => props.setSelectedKey(item.rules[0].key)}>{item.name}<span>{item.rules.length}</span></button>)}
        </nav>
        <section className="atlas-rule-grid">
          <div className="rule-index">
            <p className="section-label">{group.name}</p>
            {props.rules.filter(item => item.group === group.name).map(item => <RuleTile key={item.key} item={item} {...props} />)}
          </div>
          <RuleStory item={selected} {...props} />
        </section>
        <section className="atlas-preview">
          <CanvasHeader title="Configuration preview" {...props} />
          <DiffWorkspace {...props} />
        </section>
      </main>
      <SaveDock {...props} />
    </div>
  );
}

function VariantC(props) {
  const selected = props.rules.find(item => item.key === props.selectedKey) ?? props.rules[0];
  return (
    <div className="shell variant-c">
      <Header changed={props.changed} compact />
      <main className="code-first">
        <section className="code-stage">
          <CanvasHeader title="Live formatter" {...props} />
          <DiffWorkspace {...props} />
          <OutputFacts />
        </section>
        <aside className="inspector">
          <div className="inspector-head">
            <p className="kicker">Inspector</p>
            <strong>{selected.label}</strong>
            <code>{selected.key}</code>
          </div>
          <RuleControl item={selected} {...props} expanded />
          <RuleExample item={selected} />
          <div className="rule-jump">
            <p className="section-label">Jump to a rule</p>
            {groups.map(group => (
              <details key={group.name} open={group.name === selected.group}>
                <summary>{group.name}<span>{group.rules.length}</span></summary>
                {props.rules.filter(item => item.group === group.name).map(item => <button key={item.key} className={item.key === selected.key ? "active" : ""} onClick={() => props.setSelectedKey(item.key)}>{item.label}<OriginDot origin={item.origin} />{item.value !== item.loaded && <i>changed</i>}</button>)}
              </details>
            ))}
          </div>
        </aside>
      </main>
      <SaveDock {...props} />
    </div>
  );
}

function Header({ changed, compact, hideSave }) {
  return (
    <header className={compact ? "app-header compact" : "app-header"}>
      <a className="brand" href="?variant=A" aria-label="DressSharp interactive home"><span className="brand-mark">D#</span><span>DressSharp <small>interactive</small></span></a>
      <div className="target"><span className="status-dot" /> <span><small>Editing</small>D:\Workbench\.editorconfig</span></div>
      <div className="header-actions"><button className="quiet">Reload</button>{!hideSave && <button className="primary">Save{changed.length ? ` · ${changed.length}` : ""}</button>}</div>
    </header>
  );
}

function RailIntro({ eyebrow, title }) {
  return <div className="rail-intro"><p className="kicker">{eyebrow}</p><h1>{title}</h1><p>Effective values for <code>[*.cs]</code>. Inherited rules are marked.</p></div>;
}

function RuleSearchList(props) {
  const [query, setQuery] = useState("");
  const filtered = props.rules.filter(item => `${item.label} ${item.key}`.toLowerCase().includes(query.toLowerCase()));
  return (
    <>
      <label className="search"><span>⌕</span><input value={query} onChange={event => setQuery(event.target.value)} placeholder="Find a preference" /></label>
      <div className="group-list">
        {groups.map(group => {
          const matches = filtered.filter(item => item.group === group.name);
          if (!matches.length) return null;
          return <details key={group.name} open><summary>{group.name}<span>{matches.length}</span></summary>{matches.map(item => <RuleRow key={item.key} item={item} {...props} />)}</details>;
        })}
      </div>
    </>
  );
}

function RuleRow({ item, selectedKey, setSelectedKey, compact, onExample, ...props }) {
  if (compact) return (
    <div className={`rule-row compact ${item.key === selectedKey ? "selected" : ""} ${item.value !== item.loaded ? "dirty" : ""}`}>
      <button className="rule-title" onClick={() => setSelectedKey(item.key)}><span>{item.label}</span><small>{item.origin === "inherited" ? "Inherited" : item.origin === "local" ? "Local" : "Default"}</small></button>
      <CompactRuleControl item={item} {...props} />
      <button className="example-trigger" title={`Show ${item.label} example`} onClick={() => { setSelectedKey(item.key); onExample(item.key); }}>?</button>
    </div>
  );
  return (
    <div className={`rule-row ${item.key === selectedKey ? "selected" : ""} ${item.value !== item.loaded ? "dirty" : ""}`}>
      <button className="rule-title" onClick={() => setSelectedKey(item.key)}><span>{item.label}</span><small>{item.key}</small></button>
      <RuleControl item={item} {...props} />
    </div>
  );
}

function CompactRuleControl({ item, setRules }) {
  const update = value => setRules(current => current.map(candidate => candidate.key === item.key ? { ...candidate, value } : candidate));
  return (
    <select aria-label={item.label} value={item.value} onChange={event => update(event.target.value)}>
      <option value="__none">{item.origin === "inherited" ? "Use inherited" : "Not configured"}</option>
      <option value="unset">unset</option>
      <optgroup label="Explicit value">{item.values.map(value => <option key={value}>{value}</option>)}</optgroup>
    </select>
  );
}

function RuleTile({ item, selectedKey, setSelectedKey, ...props }) {
  return (
    <article className={`rule-tile ${item.key === selectedKey ? "selected" : ""} ${item.value !== item.loaded ? "dirty" : ""}`} onClick={() => setSelectedKey(item.key)}>
      <div><OriginDot origin={item.origin} /><span>{item.origin === "inherited" ? "Inherited" : item.origin === "local" ? "Local" : "Not configured"}</span>{item.value !== item.loaded && <i>Changed</i>}</div>
      <h3>{item.label}</h3><code>{item.key}</code>
      <RuleControl item={item} {...props} />
    </article>
  );
}

function RuleControl({ item, rules, setRules, expanded }) {
  const update = value => setRules(current => current.map(candidate => candidate.key === item.key ? { ...candidate, value } : candidate));
  const assignment = item.value === "__none" ? "none" : item.value === "unset" ? "unset" : "value";
  return (
    <div className={`rule-control ${expanded ? "expanded" : ""}`} onClick={event => event.stopPropagation()}>
      {expanded && <label>Local assignment</label>}
      <div className="assignment-tabs" aria-label={`Assignment for ${item.label}`}>
        <button className={assignment === "none" ? "active" : ""} onClick={() => update("__none")}>None</button>
        <button className={assignment === "unset" ? "active" : ""} onClick={() => update("unset")}>unset</button>
        <button className={assignment === "value" ? "active" : ""} onClick={() => update(item.loaded === "unset" || item.loaded === "__none" ? item.values[0] : item.loaded)}>Value</button>
      </div>
      {assignment === "value" && <select value={item.value} onChange={event => update(event.target.value)}>{item.values.map(value => <option key={value}>{value}</option>)}</select>}
      {item.value !== item.loaded && <button className="restore" onClick={() => update(item.loaded)}>Restore</button>}
      {expanded && <p className="effective">Effective value <strong>{item.value === "__none" ? "inherited: " + item.loaded : item.value}</strong></p>}
    </div>
  );
}

function RuleStory({ item, ...props }) {
  return (
    <article className="rule-story">
      <p className="kicker">Selected preference</p><h2>{item.label}</h2><code>{item.key}</code><p>{item.description}</p>
      <RuleControl item={item} {...props} expanded />
      <RuleExample item={item} />
      <a className="related" href="#">Related: csharp_new_line_before_open_brace <span>↗</span></a>
    </article>
  );
}

function RuleExample({ item }) {
  return (
    <section className="example">
      <div><span>Rule example</span><small>read-only</small></div>
      <pre><code>{exampleFor(item.key)}</code></pre>
      <p>Change the value above to compare documented outcomes. Your preview source is unaffected.</p>
    </section>
  );
}

function CanvasHeader({ title, whitespace, setWhitespace }) {
  return <div className="canvas-header"><div><p className="kicker">All pending rules applied</p><h2>{title}</h2></div><div className="canvas-tools"><button className={whitespace ? "active" : ""} onClick={() => setWhitespace(!whitespace)}>¶ Whitespace</button><span>Latest C# · no symbols</span></div></div>;
}

function DiffWorkspace({ source, setSource, formatted, whitespace }) {
  const createEditor = useMemo(() => options => new Editor({ ...options, onChange: (_file, _annotations, event) => {
    const text = _file?.contents ?? event?.file?.contents;
    if (typeof text === "string") setSource(text);
    options.onChange?.(_file, _annotations, event);
  }}), [setSource]);
  const fileDiff = useMemo(() => parseDiffFromFile(
    { name: "Preview.cs", contents: formatted, cacheKey: `formatted-${formatted}` },
    { name: "Preview.cs", contents: source, cacheKey: `source-${source}` }), [formatted, source]);
  return (
    <div className={`diff-workspace ${whitespace ? "show-whitespace" : ""}`}>
      <div className="diff-labels"><span>Source · editable</span><span>Formatted output · read-only</span></div>
      <EditProvider createEditor={createEditor}>
        <FileDiff
          fileDiff={fileDiff}
          edit
          disableWorkerPool
          options={{
            diffStyle: "split",
            lineDiffType: "char",
            expandUnchanged: true,
            disableFileHeader: true,
            overflow: "scroll",
            theme: { dark: "github-dark", light: "github-light" },
            disableErrorHandling: true,
            unsafeCSS: `
              :host {
                --diffs-light-addition-color: #c93742 !important;
                --diffs-light-deletion-color: #23864d !important;
                --diffs-dark-addition-color: #f05d65 !important;
                --diffs-dark-deletion-color: #4ac979 !important;
                height: 100%;
              }
              pre[data-diff] { height: 100%; min-height: 100%; max-height: none; }
              pre[data-diff-type="split"] > [data-additions] { grid-column: 1; grid-row: 1; }
              pre[data-diff-type="split"] > [data-deletions] { grid-column: 2; grid-row: 1; }
              @media (max-width: 900px) {
                pre[data-diff-type="split"] { display: flex; flex-direction: column; }
                pre[data-diff-type="split"] > [data-additions] { order: 1; width: 100%; }
                pre[data-diff-type="split"] > [data-deletions] { order: 2; width: 100%; }
              }
            `,
          }}
        />
      </EditProvider>
      <div className="pierre-note"><span>Pierre prototype</span> Editable addition pane is visually moved left; this is the adoption risk under test.</div>
    </div>
  );
}

function OutputFacts() {
  return <div className="output-facts"><span><small>Encoding</small>UTF-8 · no BOM</span><span><small>Line endings</small>LF</span><span><small>Final newline</small>Present</span><span><small>Trailing whitespace</small>2 removed</span></div>;
}

function SaveDock({ changed, setRules }) {
  return (
    <div className={`save-dock ${changed.length ? "visible" : ""}`}>
      <div><strong>{changed.length} {changed.length === 1 ? "preference" : "preferences"} changed</strong><span>{changed.map(item => item.key).join(" · ")}</span></div>
      <button className="quiet" onClick={() => setRules(current => current.map(item => ({ ...item, value: item.loaded })))}>Discard</button>
      <button className="primary">Save configuration</button>
    </div>
  );
}

function OriginDot({ origin }) { return <span className={`origin-dot ${origin}`} title={origin} />; }

function PrototypeSwitcher({ current }) {
  const variants = ["A", "B", "C"];
  const navigate = direction => {
    const next = variants[(variants.indexOf(current) + direction + variants.length) % variants.length];
    const params = new URLSearchParams(location.search); params.set("variant", next); location.search = params;
  };
  useEffect(() => {
    const listener = event => {
      if (["INPUT", "TEXTAREA", "SELECT"].includes(document.activeElement?.tagName) || document.activeElement?.isContentEditable) return;
      if (event.key === "ArrowLeft") navigate(-1);
      if (event.key === "ArrowRight") navigate(1);
    };
    addEventListener("keydown", listener); return () => removeEventListener("keydown", listener);
  });
  return <nav className="prototype-switcher" aria-label="Prototype variants"><button onClick={() => navigate(-1)} aria-label="Previous variant">←</button><span><small>Prototype</small>{current} · {variantNames[current]}</span><button onClick={() => navigate(1)} aria-label="Next variant">→</button></nav>;
}

function mockFormat(text, rules) {
  const value = key => rules.find(item => item.key === key)?.value;
  let result = text;
  if (value("csharp_space_after_comma") !== "false") result = result.replace(/,(?!\s)/g, ", ");
  if (value("csharp_space_after_keywords_in_control_flow_statements") !== "false") result = result.replace(/\b(if|for|while|switch)\(/g, "$1 (");
  if (value("csharp_space_around_binary_operators") !== "none") result = result.replace(/\s*([+>])\s*/g, " $1 ");
  if (value("dress_namespace_style") === "file_scoped") result = result.replace(/namespace Example\s*\{\s*/, "namespace Example;\n\n").replace(/\n\}\s*$/, "\n");
  if (value("dress_method_body") === "expression") result = result.replace(/\n\s*\{\n\s*if \(subtotal > 0\) \{ return subtotal \+ tax; \}\n\s*else \{ return 0; \}\n\s*\}/, " => subtotal > 0 ? subtotal + tax : 0;");
  if (value("dress_embedded_statement_braces") === "always") result = result.replace(/if \(([^)]+)\)\s*return ([^;]+);/g, "if ($1)\n        {\n            return $2;\n        }");
  return result;
}

function exampleFor(key) {
  if (key.includes("braces")) return "if (ready) Run();\n\nif (ready)\n{\n    Run();\n}";
  if (key.includes("namespace")) return "namespace Example;\n\nclass Receipt { }";
  if (key.includes("layout") || key === "max_line_length") return "Send(customer, address, items, discount);";
  if (key.includes("comma")) return "Call(first, second, third);";
  if (key.includes("binary")) return "var total = subtotal + tax + shipping;";
  return "class Receipt\n{\n    void Print() { }\n}";
}

createRoot(document.getElementById("root")).render(<App />);
