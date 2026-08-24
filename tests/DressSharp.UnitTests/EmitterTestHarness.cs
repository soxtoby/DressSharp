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
            preferences.Select(pair => new KeyValuePair<RuleKey, string>(RuleKeys.Parse(pair.Key), pair.Value)));
        var structural = FileScopedStructuralRules.Transform(root, RuleCatalog.BuiltIn, configuration);
        if (!structural.Succeeded)
            throw structural.Failure!;

        var settings = RuleSettings.From(configuration);
        var emitterPlan = EmitterPlan.From(RuleCatalog.BuiltIn, configuration);
        var shaped = structural.Root;
        var text = ReferenceEquals(root, shaped) ? source : shaped.ToFullString();
        var context = new RuleContext(shaped, settings);
        var rewrites = SyntaxRewritePlan.For(
            shaped,
            MemberRuleSet.From(RuleCatalog.BuiltIn, configuration),
            context);
        var constructPreparation = ConstructLayoutPlan.Prepare(
            shaped,
            text,
            rewrites,
            RuleCatalog.BuiltIn.ConstructLayoutRules,
            configuration,
            settings,
            emitterPlan,
            context);
        var stream = constructPreparation.Stream;
        var trivia = TriviaLayoutPlan.For(
            shaped,
            stream,
            TriviaLayoutPlan.Prepare(RuleCatalog.BuiltIn, configuration),
            context);
        var constructs = constructPreparation.Finish(trivia);
        return SinglePassEmitter.Emit(shaped, emitterPlan, context, text, stream, trivia, constructs);
    }
}
