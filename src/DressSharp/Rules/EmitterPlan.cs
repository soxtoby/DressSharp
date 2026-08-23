using DressSharp.Architecture;

namespace DressSharp.Rules;

/// <summary>
/// Everything the emitter needs that depends on configuration rather than on the file, resolved once
/// so that per-file work is only walking and deciding.
/// </summary>
sealed class EmitterPlan
{
    readonly Dictionary<int, ulong> _triggers;
    readonly Dictionary<int, ulong> _newLineTriggers;
    readonly ulong _newLineWildcards;

    EmitterPlan(
        (TokenSpacingRule Rule, string Preference)[] spacing,
        (NewLineRule Rule, NewLineRule.BraceCategories Categories)[] newLines,
        Dictionary<int, ulong> triggers,
        Dictionary<int, ulong> newLineTriggers,
        ulong newLineWildcards,
        ulong laterElementRules,
        RuleSettings settings,
        bool indentBraces,
        bool indentBlockContents)
    {
        Spacing = spacing;
        NewLines = newLines;
        _triggers = triggers;
        _newLineTriggers = newLineTriggers;
        _newLineWildcards = newLineWildcards;
        LaterElementRules = laterElementRules;
        MaximumLineLength = settings.MaximumLineLength;
        IndentUnit = settings.IndentUnit;
        IndentBraces = indentBraces;
        IndentBlockContents = indentBlockContents;
    }

    internal (TokenSpacingRule Rule, string Preference)[] Spacing { get; }
    internal (NewLineRule Rule, NewLineRule.BraceCategories Categories)[] NewLines { get; }
    internal int MaximumLineLength { get; }
    internal string IndentUnit { get; }
    internal bool IndentBraces { get; }
    internal bool IndentBlockContents { get; }
    internal ulong LaterElementRules { get; }
    internal string LineEnding { get; init; } = "\n";

    /// <summary>
    /// The bit set of spacing rules that could claim a pair containing a token of this kind.
    /// </summary>
    internal ulong Trigger(int rawKind) => _triggers.GetValueOrDefault(rawKind);
    internal ulong NewLineTrigger(int rawKind) => _newLineWildcards | _newLineTriggers.GetValueOrDefault(rawKind);

    internal static EmitterPlan From(RuleCatalog catalog, FormattingConfiguration configuration)
    {
        var spacing = new List<(TokenSpacingRule, string)>();
        var newLines = new List<(NewLineRule, NewLineRule.BraceCategories)>();
        foreach (var rule in catalog.SpacingRules)
        {
            if (!configuration.Preferences.TryGetValue(rule.Metadata.RuleKey, out var preference)
                || preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
                continue;
            spacing.Add((rule, preference));
        }

        foreach (var rule in catalog.NewLineRules)
        {
            if (configuration.Preferences.TryGetValue(rule.Metadata.RuleKey, out var preference)
                && !preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
            {
                newLines.Add((rule, NewLineRule.BraceCategories.From(rule.Kind, preference)));
            }
        }

        var triggers = new Dictionary<int, ulong>();
        for (var index = 0; index < spacing.Count; index++)
        {
            foreach (var kind in spacing[index].Item1.TriggerKinds)
                triggers[(int)kind] = triggers.GetValueOrDefault((int)kind) | (1UL << index);
        }

        var newLineTriggers = new Dictionary<int, ulong>();
        var newLineWildcards = 0UL;
        var laterElementRules = 0UL;
        for (var index = 0; index < newLines.Count; index++)
        {
            var bit = 1UL << index;
            if (newLines[index].Item1.RequiresLaterElement)
            {
                newLineWildcards |= bit;
                laterElementRules |= bit;
                continue;
            }

            foreach (var kind in newLines[index].Item1.EmitterTriggerKinds)
                newLineTriggers[(int)kind] = newLineTriggers.GetValueOrDefault((int)kind) | bit;
        }

        return new(
            [.. spacing],
            [.. newLines],
            triggers,
            newLineTriggers,
            newLineWildcards,
            laterElementRules,
            RuleSettings.From(configuration),
            IsTrue(configuration, RuleKey.CSharpIndentBraces),
            IsTrue(configuration, RuleKey.CSharpIndentBlockContents));
    }

    static bool IsTrue(FormattingConfiguration configuration, RuleKey key) =>
        configuration.Preferences.GetValueOrDefault(key, "false").Equals("true", StringComparison.OrdinalIgnoreCase);
}
