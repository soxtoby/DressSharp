using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DressSharp.Interactive;
using DressSharp.Rules;
using Markdig;

namespace DressSharp.Docs;

sealed class SiteWriter(string output, string? repositoryUrl, string version)
{
    const string HeroSource = """
        using System.Linq;
        using System;

        namespace Shop.Orders {
            public class OrderTotals {
                readonly IPricing pricing; readonly ILogger log;
                public OrderTotals( IPricing pricing,ILogger log ) { this.pricing=pricing; this.log = log; }

                public decimal Total(Order order, bool includeShipping) {
                    if(order.Lines.Count==0) return 0m;
                    var subtotal = order.Lines.Where(line => line.Quantity > 0).Sum(line => pricing.PriceFor(line.Sku, line.Quantity, order.Customer.Tier, order.Currency));
                    var shipping = includeShipping ? pricing.Shipping(order.Destination, order.Weight, order.Customer.Tier) : 0m;
                    log.Info($"Order {order.Id}: {subtotal} + {shipping}");
                    return subtotal+shipping;
                }
            }
        }
        """;

    const string Script = """
        <script>
        (function () {
          document.querySelectorAll(".example").forEach(function (example) {
            var tabs = example.querySelectorAll("[role=tab]");
            var panels = example.querySelectorAll("[role=tabpanel]");
            var label = example.querySelector(".selected-value");
            function select(value) {
              tabs.forEach(function (tab) { tab.setAttribute("aria-selected", tab.dataset.value === value ? "true" : "false"); });
              panels.forEach(function (panel) { panel.hidden = panel.dataset.value !== value; });
              if (label) label.textContent = value;
            }
            tabs.forEach(function (tab) { tab.addEventListener("click", function () { select(tab.dataset.value); }); });
            var initial = example.querySelector("[role=tab][aria-selected=true]") || tabs[0];
            if (initial) select(initial.dataset.value);
          });
          document.querySelectorAll(".copy").forEach(function (button) {
            button.addEventListener("click", function () {
              navigator.clipboard.writeText(button.dataset.copy).then(function () {
                button.textContent = "Copied";
                setTimeout(function () { button.textContent = "Copy"; }, 1200);
              });
            });
          });
          var filter = document.getElementById("filter");
          if (filter) {
            filter.addEventListener("input", function () {
              var query = filter.value.trim().toLowerCase();
              document.querySelectorAll(".rule-table tr[data-search]").forEach(function (row) {
                row.hidden = query !== "" && row.dataset.search.indexOf(query) === -1;
              });
              document.querySelectorAll(".index-group").forEach(function (group) {
                group.hidden = !group.querySelector("tr[data-search]:not([hidden])");
              });
            });
          }
        })();
        </script>
        """;

    static readonly MarkdownPipeline Markdown = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    internal async Task WriteAsync(ImmutableArray<RuleGroup> groups, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(output, "rules"));
        var hero = await CatalogReader.FormatWithDefaults(HeroSource, cancellationToken);
        if (hero.SkippedOccurrences > 0)
            throw new InvalidOperationException("The landing page example was not fully formatted.");

        await CopyAssets();
        await File.WriteAllTextAsync(Path.Combine(output, "index.html"), Landing(groups, hero), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, "rules", "index.html"), RulesIndex(groups), cancellationToken);
        foreach (var group in groups)
            await File.WriteAllTextAsync(Path.Combine(output, "rules", Slug(group.Name) + ".html"), GroupPage(groups, group), cancellationToken);
        await WriteMarkdownPage("cli-and-configuration.md", "cli.html", "CLI and configuration", cancellationToken);
        await WriteMarkdownPage("compatibility.md", "compatibility.html", "Compatibility", cancellationToken);
    }

    async Task CopyAssets()
    {
        using var css = typeof(SiteWriter).Assembly.GetManifestResourceStream("site.css")!;
        using var target = File.Create(Path.Combine(output, "site.css"));
        await css.CopyToAsync(target);
        var brand = Path.Combine(RepositoryRoot, "assets", "brand");
        foreach (var name in new[] { "dresssharp.svg", "favicon.svg", "favicon.ico" })
            File.Copy(Path.Combine(brand, name), Path.Combine(output, name), overwrite: true);
        // GitHub Pages runs Jekyll unless told otherwise, and Jekyll drops files it considers special.
        await File.WriteAllTextAsync(Path.Combine(output, ".nojekyll"), "");
    }

    static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DressSharp.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Run the docs generator from inside the repository.");
        }
    }

    // ---- Pages ------------------------------------------------------------------------------------

    string Landing(ImmutableArray<RuleGroup> groups, PreviewResult hero)
    {
        var ruleCount = groups.Sum(group => group.Rules.Count());
        var body = new StringBuilder();
        body.Append($"""
            <section class="hero">
              <div class="hero-copy">
                <p class="kicker">C# formatter · .NET tool · EditorConfig</p>
                <h1>Every line where you said it should be.</h1>
                <p class="lede">DressSharp formats C# according to explicit, independently selectable preferences. It reads syntax only, never types, and does nothing you did not ask for.</p>
                <div class="cta">
                  <a class="button primary" href="rules/index.html">Browse the {ruleCount} rules</a>
                  <a class="button" href="cli.html">CLI &amp; configuration</a>
                </div>
              </div>
              <div class="hero-install">
                <div class="pane-label">Install, write the Defaults, format</div>
                <pre class="install"><code><span class="prompt">$</span> dotnet tool install --global DressSharp
            <span class="prompt">$</span> dotnet dress init
            <span class="prompt">$</span> dotnet dress</code></pre>
                <p class="note">Or pin it per repository with <code>dotnet tool install DressSharp</code> and format only what Git touched: <code>dotnet dress format --staged</code>.</p>
              </div>
            </section>

            <section class="showcase">
              <div class="pane">
                <div class="pane-label">Before</div>
                {CodeBlock(HeroSource)}
              </div>
              <div class="pane">
                <div class="pane-label">After one run of <code>dotnet dress</code> with the Default preferences</div>
                {CodeBlock(hero.Text)}
              </div>
            </section>

            <section class="features">
              <article><h2>Explicit, never implicit</h2><p>A missing or <code>unset</code> preference makes no change. There is no hidden house style underneath your EditorConfig; <code>dotnet dress init</code> writes every supported key with its Default so the whole contract is in one file you can read.</p></article>
              <article><h2>Syntax only</h2><p>The formatter never asks for type or semantic information, so it needs no compilation, no restore, and no design-time build. It resolves the language version and preprocessor symbols from your project and leaves malformed or disabled regions untouched.</p></article>
              <article><h2>Speaks EditorConfig</h2><p>Standard <code>csharp_*</code> and <code>dotnet_*</code> keys work as they do in Visual Studio. Where the standard stops, <code>dress_*</code> keys pick up: list wrapping, closing-delimiter placement, initializer indentation, comment alignment, blank lines, and more.</p></article>
              <article><h2>Built for hooks</h2><p><code>--staged</code> and <code>--changed</code> select only the files Git reports as touched, so a pre-commit hook or an agent hook is one line. The package ships precompiled per platform so a one-file check is fast enough to run on every write.</p></article>
              <article><h2>Interactive configuration</h2><p><code>dotnet dress interactive</code> opens an offline workbench in your browser. Change a preference, watch real formatter output update, select lines to see which rules touched them, and save straight to your EditorConfig.</p></article>
              <article><h2>Safe on disk</h2><p>Unchanged files keep their bytes. Changed files are replaced atomically in the same directory, refused if they changed since they were read, and symlink targets are protected. Encoding and line endings only change when you set them.</p></article>
            </section>

            <section class="groups">
              <h2>Rule catalog <small>version {CatalogReader.Version}</small></h2>
              <p>Generated from the built-in catalog. Every example below is real formatter output for the example in the rule's own metadata.</p>
              <ul class="group-cards">
            """);
        foreach (var group in groups)
        {
            var count = group.Rules.Count();
            body.Append($"""
                    <li><a href="rules/{Slug(group.Name)}.html"><strong>{H(group.Name)}</strong><span>{count} {(count == 1 ? "preference" : "preferences")}</span></a></li>

                """);
        }
        body.Append("  </ul>\n</section>\n");
        return Page("DressSharp", "", body.ToString(), "landing");
    }

    string RulesIndex(ImmutableArray<RuleGroup> groups)
    {
        var body = new StringBuilder();
        body.Append($"""
            <header class="page-head">
              <h1>Rules</h1>
              <p>Every preference DressSharp understands, in the order <code>dotnet dress init</code> writes them. Each key controls exactly one formatting rule. Every preference also accepts <code>unset</code>, which selects no outcome so the rule makes no change.</p>
              <label class="filter"><span>Filter</span><input type="search" id="filter" placeholder="key, caption, or value" autocomplete="off"></label>
            </header>

            """);
        foreach (var group in groups)
        {
            body.Append(
                $"<section class=\"index-group\">\n<h2><a href=\"{Slug(group.Name)}.html\">{H(group.Name)}</a></h2>\n<table class=\"rule-table\">\n<thead><tr><th>Preference</th><th>Rule</th><th>Default</th></tr></thead>\n<tbody>\n");
            foreach (var subgroup in group.Subgroups)
            {
                foreach (var rule in subgroup.Rules)
                {
                    var caption = subgroup.Name is null ? rule.Metadata.Caption : $"{subgroup.Name} · {rule.Metadata.Caption}";
                    body.Append(
                        $"<tr data-search=\"{H(rule.Key + " " + rule.Metadata.ExpandedCaption + " " + string.Join(' ', rule.Metadata.AcceptedValueForms)).ToLowerInvariant()}\">")
                        .Append($"<td><a href=\"{Slug(group.Name)}.html#{rule.Key}\"><code>{H(rule.Key)}</code></a></td>")
                        .Append($"<td>{H(caption)}</td>")
                        .Append($"<td><code>{H(rule.Metadata.DefaultValue)}</code></td></tr>\n");
                }
            }
            body.Append("</tbody>\n</table>\n</section>\n");
        }
        return Page("Rules · DressSharp", "../", body.ToString(), "rules-index");
    }

    string GroupPage(ImmutableArray<RuleGroup> groups, RuleGroup group)
    {
        var nav = new StringBuilder(
            "<nav class=\"side\" aria-label=\"Rules\">\n<p class=\"side-title\"><a href=\"index.html\">All rules</a></p>\n<ul class=\"side-groups\">\n");
        foreach (var other in groups)
        {
            var current = other == group ? " class=\"current\"" : "";
            nav.Append($"<li{current}><a href=\"{Slug(other.Name)}.html\">{H(other.Name)}</a>");
            if (other == group)
            {
                nav.Append("\n<ul class=\"side-rules\">\n");
                foreach (var subgroup in group.Subgroups)
                {
                    if (subgroup.Name is not null)
                        nav.Append($"<li class=\"side-subgroup\">{H(subgroup.Name)}</li>\n");
                    foreach (var rule in subgroup.Rules)
                        nav.Append($"<li><a href=\"#{rule.Key}\">{H(rule.Metadata.Caption)}</a></li>\n");
                }
                nav.Append("</ul>\n");
            }
            nav.Append("</li>\n");
        }
        nav.Append("</ul>\n</nav>\n");

        var body = new StringBuilder();
        body.Append($"<header class=\"page-head\">\n<p class=\"kicker\"><a href=\"index.html\">Rules</a></p>\n<h1>{H(group.Name)}</h1>\n</header>\n");
        foreach (var subgroup in group.Subgroups)
        {
            if (subgroup.Name is not null)
                body.Append($"<h2 class=\"subgroup\">{H(subgroup.Name)}</h2>\n");
            foreach (var rule in subgroup.Rules)
                body.Append(RuleSection(rule));
        }
        return Page($"{group.Name} · DressSharp rules", "../", $"<div class=\"with-side\">\n{nav}<div class=\"main\">\n{body}</div>\n</div>\n", "group");
    }

    string RuleSection(RuleDocumentation rule)
    {
        var metadata = rule.Metadata;
        var section = new StringBuilder();
        section.Append($"<section class=\"rule\" id=\"{rule.Key}\">\n");
        section.Append($"<header class=\"rule-head\">\n<h3><a href=\"#{rule.Key}\">{H(metadata.ExpandedCaption)}</a></h3>\n");
        section.Append(
            $"<p class=\"key\"><code>{H(rule.Key)}</code><button type=\"button\" class=\"copy\" data-copy=\"{H(rule.Key)} = {H(metadata.DefaultValue)}\" title=\"Copy the Default assignment\">Copy</button></p>\n</header>\n");
        section.Append($"<p class=\"description\">{H(metadata.Description)}</p>\n");

        section.Append("<dl class=\"facts\">\n");
        section.Append($"<dt>Values</dt><dd>{ValueList(metadata)}</dd>\n");
        section.Append(
            $"<dt>Default</dt><dd><code>{H(metadata.DefaultValue)}</code>{(rule.IsDefaultUnset ? " <span class=\"note\">(the rule makes no change unless you set it)</span>" : "")}</dd>\n");
        section.Append($"<dt>Applies to</dt><dd>{H(Sentence(metadata.OwnedSyntax))}</dd>\n");
        section.Append($"<dt>Guarantee</dt><dd>{H(Sentence(metadata.Invariant))}</dd>\n");
        if (!rule.Companions.IsEmpty)
        {
            var companions = string.Join(", ", rule.Companions.Select(pair => $"<code>{H(pair.Key)} = {H(pair.Value)}</code>"));
            section.Append($"<dt>Shown with</dt><dd>{companions}</dd>\n");
        }
        section.Append("</dl>\n");

        section.Append(Example(rule));
        section.Append("</section>\n");
        return section.ToString();
    }

    string Example(RuleDocumentation rule)
    {
        var input = rule.Metadata.Example;
        var isFileRule = rule.Metadata.GroupName == "File";
        var example = new StringBuilder("<div class=\"example\">\n");
        example.Append($"<div class=\"pane\">\n<div class=\"pane-label\">Input</div>\n{CodeBlock(input)}\n</div>\n");
        example.Append(
            "<div class=\"pane outcomes\">\n<div class=\"pane-label\">Output for <span class=\"selected-value\"></span></div>\n<div class=\"values\" role=\"tablist\">\n");
        foreach (var outcome in rule.Outcomes)
        {
            var selected = outcome.IsDefault ? " aria-selected=\"true\"" : "";
            var title = outcome.IsDefault ? " title=\"Default\"" : "";
            example.Append(
                $"<button type=\"button\" role=\"tab\"{selected}{title} data-value=\"{H(outcome.Value)}\"><code>{H(outcome.Value)}</code>{(outcome.IsDefault ? "<small>Default</small>" : "")}</button>\n");
        }
        example.Append("</div>\n");
        foreach (var outcome in rule.Outcomes)
        {
            var hidden = outcome.IsDefault ? "" : " hidden";
            example.Append($"<div class=\"outcome\" role=\"tabpanel\" data-value=\"{H(outcome.Value)}\"{hidden}>\n");
            // Line endings are representation, reported in the metadata row, so a change to them alone is not a code change.
            var unchanged = Split(outcome.Result.Text).SequenceEqual(Split(input));
            example.Append(unchanged ? "<p class=\"unchanged\">Code unchanged</p>\n" : DiffBlock(input, outcome.Result.Text) + "\n");
            if (isFileRule)
            {
                example.Append(
                    $"<p class=\"meta\"><span>Encoding <code>{H(outcome.Result.Encoding)}</code></span><span>Line endings <code>{H(outcome.Result.LineEndings)}</code></span><span>Final newline <code>{(outcome.Result.FinalNewline ? "yes" : "no")}</code></span></p>\n");
            }
            example.Append("</div>\n");
        }
        example.Append("</div>\n</div>\n");
        return example.ToString();
    }

    static string ValueList(RuleMetadata metadata)
    {
        var forms = metadata.AcceptedValueForms.Select(form => $"<code>{H(form)}</code>");
        var kind = metadata.Values.Kind switch
            {
                RuleValueKind.MultipleChoice => " <span class=\"note\">Any comma-separated selection.</span>",
                RuleValueKind.Permutation => " <span class=\"note\">Every item, in any order, comma-separated.</span>",
                _ => ""
            };
        return string.Join(" ", forms) + kind;
    }

    static string Sentence(string text)
    {
        var trimmed = text.Trim().TrimEnd('.');
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..] + ".";
    }

    // ---- Markdown pages ---------------------------------------------------------------------------

    async Task WriteMarkdownPage(string sourceName, string targetName, string title, CancellationToken cancellationToken)
    {
        var markdown = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot, "docs", sourceName), cancellationToken);
        var html = Markdig.Markdown.ToHtml(markdown, Markdown);
        html = Regex.Replace(html, "href=\"([^\"]+)\\.md\"", match => $"href=\"{match.Groups[1].Value}.html\"");
        html = Regex.Replace(
            html,
            "<pre><code class=\"language-csharp\">(.*?)</code></pre>",
            match => CodeBlock(WebUtility.HtmlDecode(match.Groups[1].Value).TrimEnd('\n')),
            RegexOptions.Singleline);
        html = Regex.Replace(
            html,
            "<pre><code class=\"language-editorconfig\">(.*?)</code></pre>",
            match => $"<pre class=\"code\"><code>{match.Groups[1].Value.TrimEnd('\n')}</code></pre>",
            RegexOptions.Singleline);
        html = Regex.Replace(
            html,
            "<pre><code class=\"language-\\w+\">(.*?)</code></pre>",
            match => $"<pre class=\"code\"><code>{match.Groups[1].Value.TrimEnd('\n')}</code></pre>",
            RegexOptions.Singleline);
        await File.WriteAllTextAsync(
            Path.Combine(output, targetName),
            Page($"{title} · DressSharp", "", $"<article class=\"prose\">\n{html}</article>\n", "prose-page"),
            cancellationToken);
    }

    // ---- Code rendering ---------------------------------------------------------------------------

    static string CodeBlock(string code)
    {
        var lines = CSharpHighlighter.HighlightLines(code);
        var builder = new StringBuilder("<pre class=\"code\"><code>");
        // Lines are blocks, so no newline text sits between them; a newline would render as a blank line of its own.
        foreach (var line in lines)
            builder.Append("<span class=\"line\">").Append(line).Append("</span>");
        return builder.Append("</code></pre>").ToString();
    }

    static string DiffBlock(string before, string after)
    {
        var beforeLines = CSharpHighlighter.HighlightLines(before);
        var afterLines = CSharpHighlighter.HighlightLines(after);
        var beforeText = Split(before);
        var afterText = Split(after);
        var builder = new StringBuilder("<pre class=\"code diff\"><code>");
        foreach (var (kind, index) in LineDiff.Compute(beforeText, afterText))
        {
            var (cls, marker, html) = kind switch
                {
                    DiffKind.Delete => ("del", "-", beforeLines[index]),
                    DiffKind.Insert => ("ins", "+", afterLines[index]),
                    _ => ("ctx", " ", afterLines[index])
                };
            builder.Append($"<span class=\"line {cls}\"><span class=\"marker\">{marker}</span>{html}</span>");
        }
        return builder.Append("</code></pre>").ToString();
    }

    static string[] Split(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    // ---- Chrome -----------------------------------------------------------------------------------

    string Page(string title, string root, string body, string pageClass)
    {
        var repository = repositoryUrl is null ? "" : $"<a href=\"{H(repositoryUrl)}\">GitHub</a>";
        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{H(title)}</title>
            <meta name="description" content="DressSharp is a syntax-only, explicitly configured C# formatter distributed as one .NET tool package.">
            <link rel="icon" href="{root}favicon.svg" type="image/svg+xml">
            <link rel="alternate icon" href="{root}favicon.ico">
            <link rel="stylesheet" href="{root}site.css">
            </head>
            <body class="{pageClass}">
            <header class="top">
              <a class="brand" href="{root}index.html"><img src="{root}dresssharp.svg" alt="" width="30" height="30">DressSharp<small>{H(version)}</small></a>
              <nav class="top-nav">
                <a href="{root}rules/index.html">Rules</a>
                <a href="{root}cli.html">CLI</a>
                <a href="{root}compatibility.html">Compatibility</a>
                <a href="https://www.nuget.org/packages/DressSharp">NuGet</a>
                {repository}
              </nav>
            </header>
            <main>
            {body}</main>
            <footer class="bottom">
              <p>Generated from the DressSharp source at version {H(version)}, rule catalog version {CatalogReader.Version}. Every example is real formatter output.</p>
            </footer>
            {Script}
            </body>
            </html>

            """;
    }

    static string H(string text) => WebUtility.HtmlEncode(text);

    internal static string Slug(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
}
