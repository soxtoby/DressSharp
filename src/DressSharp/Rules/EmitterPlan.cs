using DressSharp.Architecture;
using Microsoft.CodeAnalysis;

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
    readonly bool?[] _initializerIndentations;

    EmitterPlan(
        (TokenSpacingRule Rule, string Preference)[] spacing,
        (NewLineRule Rule, NewLineRule.BraceCategories Categories)[] newLines,
        Dictionary<int, ulong> triggers,
        Dictionary<int, ulong> newLineTriggers,
        ulong newLineWildcards,
        ulong laterElementRules,
        RuleSettings settings,
        bool? indentBraces,
        bool? indentBlockContents,
        bool? indentSwitchLabels,
        bool? indentCaseContents,
        LabelIndentationStyle? labelIndentation,
        bool? indentCaseContentsWhenBlock,
        bool expandSingleLineBlocks,
        bool separateSingleLineStatements,
        bool?[] initializerIndentations,
        EmbeddedStatementSettings embeddedStatements)
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
        IndentSwitchLabels = indentSwitchLabels;
        IndentCaseContents = indentCaseContents;
        LabelIndentation = labelIndentation;
        IndentCaseContentsWhenBlock = indentCaseContentsWhenBlock;
        ExpandSingleLineBlocks = expandSingleLineBlocks;
        SeparateSingleLineStatements = separateSingleLineStatements;
        _initializerIndentations = initializerIndentations;
        EmbeddedStatements = embeddedStatements;
    }

    internal (TokenSpacingRule Rule, string Preference)[] Spacing { get; }
    internal (NewLineRule Rule, NewLineRule.BraceCategories Categories)[] NewLines { get; }
    internal int MaximumLineLength { get; }
    internal string IndentUnit { get; }
    internal bool? IndentBraces { get; }
    internal bool? IndentBlockContents { get; }
    internal bool? IndentSwitchLabels { get; }
    internal bool? IndentCaseContents { get; }
    internal LabelIndentationStyle? LabelIndentation { get; }
    internal bool? IndentCaseContentsWhenBlock { get; }
    internal bool ExpandSingleLineBlocks { get; }
    internal bool SeparateSingleLineStatements { get; }
    internal EmbeddedStatementSettings EmbeddedStatements { get; }
    internal ulong LaterElementRules { get; }
    /// <summary>
    /// The bit set of spacing rules that could claim a pair containing a token of this kind.
    /// </summary>
    internal ulong Trigger(int rawKind) => _triggers.GetValueOrDefault(rawKind);
    internal ulong NewLineTrigger(int rawKind) => _newLineWildcards | _newLineTriggers.GetValueOrDefault(rawKind);
    internal bool? InitializerIndentation(InitializerKind kind) => _initializerIndentations[(int)kind];

    internal bool? DesiredSpace(SyntaxToken left, SyntaxToken right)
    {
        var candidates = Trigger(left.RawKind) | Trigger(right.RawKind);
        return candidates == 0 
            ? null 
            : DesiredSpace(left, right, candidates);
    }

    internal bool? DesiredSpace(SyntaxToken left, SyntaxToken right, ulong candidates)
    {

        var desired = default(bool?);
        for (var index = 0; index < Spacing.Length; index++)
        {
            if ((candidates & (1UL << index)) == 0)
                continue;
            var candidate = Spacing[index].Rule.DesiredSpace(left, right, Spacing[index].Preference);
            if (candidate is not null)
                desired = candidate;
        }

        return desired;
    }

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

        var initializerIndentations = new bool?[Enum.GetValues<InitializerKind>().Length];
        foreach (var rule in catalog.InitializerIndentationRules)
        {
            initializerIndentations[(int)rule.Kind] = InitializerIndentation(configuration.Preferences.GetValueOrDefault(rule.Metadata.RuleKey));
        }

        return new(
            [.. spacing],
            [.. newLines],
            triggers,
            newLineTriggers,
            newLineWildcards,
            laterElementRules,
            RuleSettings.From(configuration),
            OptionalBoolean(configuration, RuleKey.CSharpIndentBraces),
            OptionalBoolean(configuration, RuleKey.CSharpIndentBlockContents),
            OptionalBoolean(configuration, RuleKey.CSharpIndentSwitchLabels),
            OptionalBoolean(configuration, RuleKey.CSharpIndentCaseContents),
            OptionalLabelIndentation(configuration),
            OptionalBoolean(configuration, RuleKey.CSharpIndentCaseContentsWhenBlock),
            OptionalBoolean(configuration, RuleKey.CSharpPreserveSingleLineBlocks) == false,
            OptionalBoolean(configuration, RuleKey.CSharpPreserveSingleLineStatements) == false,
            initializerIndentations,
            EmbeddedStatementSettings.From(configuration));
    }

    static bool? OptionalBoolean(FormattingConfiguration configuration, RuleKey key) =>
        configuration.Preferences.TryGetValue(key, out var value)
        && bool.TryParse(value, out var parsed)
            ? parsed
            : null;

    static bool? InitializerIndentation(string? value) =>
        value?.Equals("indented", StringComparison.OrdinalIgnoreCase) == true
            ? true
            : value?.Equals("not_indented", StringComparison.OrdinalIgnoreCase) == true
                ? false
                : null;

    static LabelIndentationStyle? OptionalLabelIndentation(FormattingConfiguration configuration) =>
        configuration.Preferences.GetValueOrDefault(RuleKey.CSharpIndentLabels) switch
        {
            "flush_left" => LabelIndentationStyle.FlushLeft,
            "no_change" => LabelIndentationStyle.NoChange,
            "one_less_than_current" => LabelIndentationStyle.OneLessThanCurrent,
            _ => null
        };

}

enum LabelIndentationStyle
{
    FlushLeft,
    NoChange,
    OneLessThanCurrent
}
