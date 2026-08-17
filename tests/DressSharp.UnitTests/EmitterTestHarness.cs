using DressSharp.Architecture;
using DressSharp.Rules;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.UnitTests;

static class EmitterTestHarness
{
    internal static string Format(string source, params (string Key, string Value)[] preferences)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var configuration = new FormattingConfiguration(
            preferences.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));
        var structural = FileScopedStructuralRules.Transform(
            root, RuleCatalog.BuiltIn.FileScopedStructural, configuration);
        if (!structural.Succeeded)
            throw structural.Failure!;

        var shaped = structural.Root;
        var text = ReferenceEquals(root, shaped) ? source : shaped.ToFullString();
        var settings = RuleSettings.From(configuration);
        var context = new RuleContext(shaped, settings);
        var rewrites = SyntaxRewritePlan.For(
            shaped,
            MemberRuleSet.From(RuleCatalog.BuiltIn.MemberScopedStructural, configuration),
            context);
        return SinglePassEmitter.Emit(
            shaped, EmitterPlan.From(RuleCatalog.BuiltIn, configuration), context, text, rewrites);
    }
}
