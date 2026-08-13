using System.Collections.Immutable;
using DressSharp.Architecture;
using DressSharp.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DressSharp.UnitTests;

public class TransformationPipelineTests
{
    [Fact]
    public void Catalog_orders_rules_and_rejects_ambiguous_metadata()
    {
        var later = Rename("dress_parameters_layout", 20, "a", "b");
        var earlier = Rename("dress_arguments_layout", 10, "b", "c");

        var catalog = new RuleCatalog([later, earlier]);

        Assert.Equal([earlier, later], catalog.Rules);
        Assert.Throws<ArgumentException>(() => new RuleCatalog([later, Rename(later.Metadata.PreferenceKey, 30, "a", "b")]));
        Assert.Throws<ArgumentException>(() => new RuleCatalog([later, Rename("dress_arguments_layout", 20, "a", "b")]));
    }

    [Fact]
    public void Missing_and_unset_preferences_do_nothing()
    {
        var root = Parse("class C { int a; }");
        var pipeline = Pipeline(Rename("dress_arguments_layout", 0, "a", "b"));

        Assert.Same(root, pipeline.Transform(root, Configuration()).Root);
        Assert.Same(root, pipeline.Transform(root, Configuration(("dress_arguments_layout", "unset"))).Root);
    }

    [Fact]
    public void Rules_run_once_in_catalog_order_on_latest_tree()
    {
        var root = Parse("class C { int a; }");
        var pipeline = Pipeline(
            Rename("dress_arguments_layout", 20, "b", "c"),
            Rename("dress_parameters_layout", 10, "a", "b"));

        var result = pipeline.Transform(root, Configuration(
            ("dress_arguments_layout", "auto"),
            ("dress_parameters_layout", "auto")));

        Assert.Contains("int c;", result.Root.ToFullString());
    }

    [Fact]
    public void Rule_can_skip_unsafe_occurrence_and_continue()
    {
        var root = Parse("class C { int good; int broken = ; int later; }");
        var pipeline = Pipeline(new SafeIdentifierRule());

        var result = pipeline.Transform(root, Configuration(("dress_arguments_layout", "auto")));

        Assert.True(result.Succeeded);
        Assert.Contains("int changed; int broken = ; int changed;", result.Root.ToFullString());
    }

    [Fact]
    public async Task Unexpected_failure_returns_original_file_and_other_files_continue()
    {
        var first = Parse("class C { int a; }");
        var second = Parse("class C { int b; }");
        var pipeline = Pipeline(new SelectiveFailureRule());

        var results = await pipeline.TransformAsync([
            (first, Configuration(("dress_arguments_layout", "auto"))),
            (second, Configuration(("dress_arguments_layout", "auto")))
        ], TestContext.Current.CancellationToken);

        Assert.Same(first, results[0].Root);
        Assert.False(results[0].Succeeded);
        Assert.True(results[1].Succeeded);
        Assert.Contains("changed", results[1].Root.ToFullString());
    }

    [Fact]
    public async Task Familiar_preset_is_deterministic_and_idempotent()
    {
        var pipeline = Pipeline(Rename("dress_arguments_layout", 0, "a", "b"));
        var configuration = new FormattingConfiguration(DressSharp.Configuration.PreferenceCatalog.Familiar);
        var inputs = Enumerable.Range(0, 20).Select(_ =>
            (Root: (SyntaxNode)Parse("class C { int a; }"), Configuration: configuration)).ToArray();

        var first = await pipeline.TransformAsync(inputs, TestContext.Current.CancellationToken);
        var second = first.Select(result => pipeline.Transform(result.Root, configuration)).ToArray();

        Assert.All(first, result => Assert.Equal(first[0].Root.ToFullString(), result.Root.ToFullString()));
        Assert.All(second, result => Assert.Equal(first[0].Root.ToFullString(), result.Root.ToFullString()));
    }

    static CompilationUnitSyntax Parse(string source) => CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();

    static FormattingConfiguration Configuration(params (string Key, string Value)[] preferences) =>
        new(preferences.Select(item => new KeyValuePair<string, string>(item.Key, item.Value)));

    static TransformationPipeline Pipeline(params IFormattingRule[] rules) => new(new RuleCatalog(rules));

    static IFormattingRule Rename(string key, int order, string from, string to) => new RenameRule(key, order, from, to);

    sealed class RenameRule(string key, int order, string from, string to) : IFormattingRule
    {
        public RuleMetadata Metadata { get; } = MetadataFor(key, order);

        public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) =>
            root.ReplaceTokens(
                root.DescendantTokens().Where(token => token.ValueText == from && !context.IsUnsafe(token)),
                (token, _) => SyntaxFactory.Identifier(token.LeadingTrivia, to, token.TrailingTrivia));
    }

    sealed class SafeIdentifierRule : IFormattingRule
    {
        public RuleMetadata Metadata { get; } = MetadataFor("dress_arguments_layout", 0);

        public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context) =>
            root.ReplaceTokens(
                root.DescendantTokens().Where(token => token.ValueText is "good" or "later" && !context.IsUnsafe(token)),
                (token, _) => SyntaxFactory.Identifier(token.LeadingTrivia, "changed", token.TrailingTrivia));
    }

    sealed class SelectiveFailureRule : IFormattingRule
    {
        public RuleMetadata Metadata { get; } = MetadataFor("dress_arguments_layout", 0);

        public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
        {
            if (root.DescendantTokens().Any(token => token.ValueText == "a"))
                throw new InvalidOperationException("Synthetic failure");
            return root.ReplaceToken(
                root.DescendantTokens().Single(token => token.ValueText == "b"),
                SyntaxFactory.Identifier("changed"));
        }
    }

    static RuleMetadata MetadataFor(string key, int order) => new(
        key,
        ["auto"],
        "identifier tokens",
        RuleSafetyClass.Layout,
        "Synthetic test invariant",
        order);
}
