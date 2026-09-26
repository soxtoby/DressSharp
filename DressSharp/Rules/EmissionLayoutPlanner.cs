using DressSharp.Architecture;
using Microsoft.CodeAnalysis;

namespace DressSharp.Rules;

/// <summary>
/// Prepares every file-specific decision needed for single-pass emission.
/// </summary>
sealed class EmissionLayoutPlanner
{
    readonly EmitterPlan _emitterPlan;
    readonly RuleSettings _settings;
    readonly SyntaxWrappingSettings _wrappingSettings;
    readonly TriviaLayoutPlan.PreparedSettings _triviaSettings;

    internal EmissionLayoutPlanner(
        RuleCatalog catalog,
        FormattingConfiguration configuration,
        RuleSettings settings,
        EmitterPlan emitterPlan)
    {
        _emitterPlan = emitterPlan;
        _settings = settings;
        _wrappingSettings = SyntaxWrappingSettings.From(
            catalog.SyntaxWrappingRules,
            configuration,
            settings.MaximumLineLength);
        _triviaSettings = TriviaLayoutPlan.Prepare(catalog, configuration);
    }

    internal EmissionLayoutPlan Plan(
        SyntaxNode root,
        string source,
        SyntaxRewritePlan rewrites,
        RuleContext context,
        LayoutFacts? facts = null)
    {
        var stream = _wrappingSettings.Enabled
            ? EffectiveTokenStream.ForSyntaxWrapping(
                root,
                source,
                rewrites,
                SyntaxWrappingTriggerMask.For(_wrappingSettings.ByKind))
            : EffectiveTokenStream.For(root, source, rewrites);

        var indentation = new IndentationModel(_emitterPlan, root, rewrites, facts);
        var claims = new ClaimedBreaks(_emitterPlan, context, root);
        SyntaxWrappingSolver? wrappingSolver = null;
        if (_wrappingSettings.Enabled)
        {
            var discovery = SyntaxWrappingDiscovery.Find(
                root,
                stream,
                _wrappingSettings.ByKind,
                _settings);
            wrappingSolver = new(
                source,
                stream,
                _wrappingSettings.ByKind,
                _settings,
                _emitterPlan,
                indentation,
                context,
                discovery,
                claims);
            indentation.Follows(wrappingSolver);
        }

        var trivia = TriviaLayoutPlan.For(root, stream, _triviaSettings, context);
        claims.Follows(stream, trivia);
        var wrapping = wrappingSolver?.Finish(trivia) ?? new([], 0);
        claims.Follows(
            index => wrapping.GapBefore(index) is { } gap && gap.AsSpan().IndexOfAny('\r', '\n') >= 0,
            settled: true);
        return new(stream, trivia, wrapping, indentation, claims);
    }
}
