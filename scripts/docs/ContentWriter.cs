using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DressSharp.Interactive;
using DressSharp.Rules;

namespace DressSharp.Docs;

/// <summary>
/// Writes the generated part of the Starlight site in <c>site/</c>: a page per rule group, the
/// rules index, the landing page's data, and the brand assets.
/// Starlight renders, highlights, and navigates; this only decides what the pages say.
/// </summary>
sealed class ContentWriter(string root)
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
                    var subtotal = order.Lines.Where(line => line.Quantity > 0).Sum(line => pricing.PriceFor(line.Sku, line.Quantity, order.Customer.Tier, order.CurrencyCode));
                    var shipping = includeShipping ? pricing.Shipping(order.Destination, order.Weight, order.Customer.Tier) : 0m;
                    log.Info($"Order {order.Id}: {subtotal} + {shipping}");
                    return subtotal+shipping;
                }
            }
        }
        """;

    string Rules => Path.Combine(root, "site", "src", "content", "docs", "rules");
    string Generated => Path.Combine(root, "site", "src", "generated");
    string Public => Path.Combine(root, "site", "public");

    internal async Task WriteAsync(ImmutableArray<RuleGroup> groups, CancellationToken cancellationToken)
    {
        var hero = await CatalogReader.FormatWithDefaults(HeroSource, cancellationToken);
        if (hero.SkippedOccurrences > 0)
            throw new InvalidOperationException("The landing page example was not fully formatted.");

        // Every directory here is listed in site/.gitignore, so replacing it loses nothing.
        foreach (var directory in new[] { Rules, Generated, Public })
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            Directory.CreateDirectory(directory);
        }

        CopyBrand();
        await Write(Path.Combine(Generated, "landing.json"), Landing(hero, groups), cancellationToken);
        await Write(Path.Combine(Rules, "index.md"), RulesIndex(groups), cancellationToken);
        for (var i = 0; i < groups.Length; i++)
            await Write(Path.Combine(Rules, Slug(groups[i].Name) + ".mdx"), GroupPage(groups[i], i), cancellationToken);
    }

    void CopyBrand()
    {
        var brand = Path.Combine(root, "assets");
        foreach (var name in new[] { "favicon.svg", "favicon.ico" })
            File.Copy(Path.Combine(brand, name), Path.Combine(Public, name));
        // Starlight imports the logo as an asset, so it sits beside the sources rather than in public/.
        File.Copy(Path.Combine(brand, "dresssharp.svg"), Path.Combine(Generated, "dresssharp.svg"));
        // GitHub Pages runs Jekyll unless told otherwise, and Jekyll drops files it considers special.
        File.WriteAllText(Path.Combine(Public, ".nojekyll"), "");
    }

    static Task Write(string path, string content, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, content, cancellationToken);

    // ---- Landing data -----------------------------------------------------------------------------

    /// <summary>
    /// The landing page's data: the example before and after one run with the Default preferences, the Default line
    /// length it is cut to, and the rule groups the pattern book links to.
    /// </summary>
    static string Landing(PreviewResult hero, ImmutableArray<RuleGroup> groups)
    {
        var lineLength = groups.SelectMany(group => group.Rules).Single(rule => rule.Key == "max_line_length").Metadata.DefaultValue;
        var landing = new JsonObject
            {
                ["before"] = Lines(HeroSource),
                ["after"] = Lines(hero.Text),
                ["maxLineLength"] = int.Parse(lineLength, CultureInfo.InvariantCulture),
                ["groups"] = new JsonArray([
                        .. groups.Select(group => new JsonObject { ["name"] = group.Name, ["slug"] = Slug(group.Name), ["rules"] = group.Rules.Count() })
                    ])
            };
        return landing.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    static string Lines(string code) => string.Join('\n', Split(code)).TrimEnd('\n');

    // ---- Rule pages -------------------------------------------------------------------------------

    static string RulesIndex(ImmutableArray<RuleGroup> groups)
    {
        var page = new StringBuilder();
        page.Append("---\ntitle: Rules\ndescription: Every preference DressSharp understands.\nsidebar:\n  label: All rules\n  order: -1\n---\n\n");
        page.Append("Every preference DressSharp understands, in the order `dotnet dress init` writes them. ");
        page.Append(
            "Each key controls exactly one formatting rule. Every preference also accepts `unset`, which selects no outcome so the rule makes no change.\n");
        foreach (var group in groups)
        {
            page.Append($"\n## [{Text(group.Name)}]({Slug(group.Name)}/)\n\n| Preference | Rule | Default |\n| --- | --- | --- |\n");
            foreach (var subgroup in group.Subgroups)
            {
                foreach (var rule in subgroup.Rules)
                {
                    var caption = subgroup.Name is null ? rule.Metadata.Caption : $"{subgroup.Name} · {rule.Metadata.Caption}";
                    page.Append($"| [{Code(rule.Key)}]({Slug(group.Name)}/#{rule.Key}) | {Text(caption)} | {Code(rule.Metadata.DefaultValue)} |\n");
                }
            }
        }
        return page.ToString();
    }

    static string GroupPage(RuleGroup group, int order)
    {
        var page = new StringBuilder();
        page.Append($"---\ntitle: {Json(group.Name)}\nsidebar:\n  order: {order}\n---\n\n");
        page.Append("import { Tabs, TabItem } from \"@astrojs/starlight/components\";\n");
        foreach (var subgroup in group.Subgroups)
        {
            if (subgroup.Name is not null)
                page.Append($"\n## {Text(subgroup.Name)}\n");
            foreach (var rule in subgroup.Rules)
                page.Append(RuleSection(rule, subgroup.Name is null ? "##" : "###"));
        }
        return page.ToString();
    }

    static string RuleSection(RuleDocumentation rule, string heading)
    {
        var metadata = rule.Metadata;
        var section = new StringBuilder();
        // The anchor is the key, so links survive a caption being reworded.
        section.Append($"\n<span id={Attribute(rule.Key)}></span>\n\n{heading} {Text(metadata.ExpandedCaption)}\n\n");
        section.Append(Fence("editorconfig", "", $"{rule.Key} = {metadata.DefaultValue}")).Append("\n\n");
        section.Append(Text(metadata.Description)).Append("\n\n");

        section.Append($"- **Values:** {ValueList(metadata)}\n");
        section.Append($"- **Default:** {Code(metadata.DefaultValue)}{(rule.IsDefaultUnset ? " (the rule makes no change unless you set it)" : "")}\n");
        section.Append($"- **Applies to:** {Text(Sentence(metadata.OwnedSyntax))}\n");
        section.Append($"- **Guarantee:** {Text(Sentence(metadata.Invariant))}\n");
        if (!rule.Companions.IsEmpty)
            section.Append($"- **Shown with:** {string.Join(", ", rule.Companions.Select(pair => Code($"{pair.Key} = {pair.Value}")))}\n");
        section.Append('\n');

        section.Append(Example(rule));
        return section.ToString();
    }

    static string Example(RuleDocumentation rule)
    {
        var isFileRule = rule.Metadata.GroupName == "File";
        var example = new StringBuilder();
        // Options with their own examples each show their own input in the diff, so there is no shared input to show first.
        if (rule.Metadata.OptionExamples.IsEmpty)
            example.Append(Fence("cs", "title=\"Input\"", rule.Metadata.Example)).Append("\n\n");
        example.Append("<Tabs>\n");
        foreach (var outcome in rule.Outcomes.OrderBy(outcome => outcome.IsDefault ? 0 : 1))
        {
            var label = outcome.IsDefault ? $"{outcome.Value} (Default)" : outcome.Value;
            example.Append($"<TabItem label={Attribute(label)}>\n\n");
            // Line endings are representation, reported below the output, so a change to them alone is not a code change.
            var unchanged = Split(outcome.Result.Text).SequenceEqual(Split(outcome.Source));
            example.Append(unchanged ? "Code unchanged." : Fence("diff", "lang=\"cs\"", Diff(outcome.Source, outcome.Result.Text))).Append("\n\n");
            if (isFileRule)
            {
                example.Append(
                    $"Encoding {Code(outcome.Result.Encoding)} · Line endings {Code(outcome.Result.LineEndings)} · Final newline {Code(outcome.Result.FinalNewline ? "yes" : "no")}\n\n");
            }
            example.Append("</TabItem>\n");
        }
        return example.Append("</Tabs>\n").ToString();
    }

    static string ValueList(RuleMetadata metadata)
    {
        var forms = string.Join(" ", metadata.AcceptedValueForms.Select(Code));
        return metadata.Values.Kind switch
            {
                RuleValueKind.MultipleChoice => forms + " — any comma-separated selection.",
                RuleValueKind.Permutation => forms + " — every item, in any order, comma-separated.",
                _ => forms
            };
    }

    static string Sentence(string text)
    {
        var trimmed = text.Trim().TrimEnd('.');
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..] + ".";
    }

    /// <summary>A unified line diff whose every line carries a marker, so Expressive Code strips them all consistently.</summary>
    static string Diff(string before, string after)
    {
        var beforeLines = Split(before);
        var afterLines = Split(after);
        return string.Join(
            '\n',
            LineDiff.Compute(beforeLines, afterLines).Select(step => step.Kind switch
                {
                    DiffKind.Delete => "-" + beforeLines[step.Index],
                    DiffKind.Insert => "+" + afterLines[step.Index],
                    _ => " " + afterLines[step.Index]
                }));
    }

    static string[] Split(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    // ---- MDX text ---------------------------------------------------------------------------------

    /// <summary>A fenced code block, fenced longer than any backtick run inside so no content can close it.</summary>
    static string Fence(string language, string meta, string code)
    {
        var fence = new string('`', Math.Max(3, LongestRun(code, '`') + 1));
        return $"{fence}{language}{(meta.Length > 0 ? " " + meta : "")}\n{code.Replace("\r\n", "\n").TrimEnd('\n')}\n{fence}";
    }

    /// <summary>Inline code; a pipe is escaped because GitHub tables split cells on it even inside code.</summary>
    static string Code(string text)
    {
        var fence = new string('`', LongestRun(text, '`') + 1);
        var padding = text.StartsWith('`') || text.EndsWith('`') ? " " : "";
        return $"{fence}{padding}{text.Replace("|", "\\|")}{padding}{fence}";
    }

    /// <summary>Plain prose from the catalog: every character MDX or Markdown would read as syntax is escaped.</summary>
    static string Text(string text) => Regex.Replace(text, @"[\\`*_{}\[\]<>#|!~]", match => "\\" + match.Value);

    /// <summary>A JSX attribute whose value is a JavaScript string literal, which JSON already is.</summary>
    static string Attribute(string value) => "{" + Json(value) + "}";

    static string Json(string value) => JsonValue.Create(value).ToJsonString();

    static int LongestRun(string text, char character)
    {
        int longest = 0, current = 0;
        foreach (var c in text)
        {
            current = c == character ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return longest;
    }

    internal static string Slug(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
}
