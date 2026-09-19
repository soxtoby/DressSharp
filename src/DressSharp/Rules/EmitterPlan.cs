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
    readonly bool[] _initializerLayouts;

    EmitterPlan(
        (TokenSpacingRule Rule, string Preference)[] spacing,
        (NewLineRule Rule, NewLineRule.BraceCategories Categories)[] newLines,
        Dictionary<int, ulong> triggers,
        Dictionary<int, ulong> newLineTriggers,
        ulong newLineWildcards,
        ulong initializerMemberBoundaryRules,
        RuleSettings settings,
        bool? indentBraces,
        bool? indentSwitchExpression,
        bool? indentLambdaBlock,
        bool? indentBlockContents,
        bool? indentSwitchLabels,
        bool? indentCaseContents,
        LabelIndentationStyle? labelIndentation,
        bool? indentCaseContentsWhenBlock,
        bool preserveSingleLineBlocks,
        bool expandSingleLineBlocks,
        bool separateSingleLineStatements,
        bool preserveTrivialSingleLineBlocks,
        bool?[] initializerIndentations,
        bool[] initializerLayouts,
        EmbeddedStatementSettings embeddedStatements,
        string? parametersClosingDelimiterPosition,
        string? multilineParameterListOpenBracePosition,
        bool alignComments)
    {
        Spacing = spacing;
        NewLines = newLines;
        _triggers = triggers;
        _newLineTriggers = newLineTriggers;
        _newLineWildcards = newLineWildcards;
        InitializerMemberBoundaryRules = initializerMemberBoundaryRules;
        MaximumLineLength = settings.MaximumLineLength;
        IndentUnit = settings.IndentUnit;
        IndentBraces = indentBraces;
        IndentSwitchExpression = indentSwitchExpression;
        IndentLambdaBlock = indentLambdaBlock;
        IndentBlockContents = indentBlockContents;
        IndentSwitchLabels = indentSwitchLabels;
        IndentCaseContents = indentCaseContents;
        LabelIndentation = labelIndentation;
        IndentCaseContentsWhenBlock = indentCaseContentsWhenBlock;
        PreserveSingleLineBlocks = preserveSingleLineBlocks;
        ExpandSingleLineBlocks = expandSingleLineBlocks;
        SeparateSingleLineStatements = separateSingleLineStatements;
        PreserveTrivialSingleLineBlocks = preserveTrivialSingleLineBlocks;
        _initializerIndentations = initializerIndentations;
        _initializerLayouts = initializerLayouts;
        EmbeddedStatements = embeddedStatements;
        ParametersClosingDelimiterPosition = parametersClosingDelimiterPosition;
        MultilineParameterListOpenBracePosition = multilineParameterListOpenBracePosition;
        AlignComments = alignComments;
    }

    internal (TokenSpacingRule Rule, string Preference)[] Spacing { get; }
    internal (NewLineRule Rule, NewLineRule.BraceCategories Categories)[] NewLines { get; }
    internal int MaximumLineLength { get; }
    internal string IndentUnit { get; }
    internal bool? IndentBraces { get; }
    internal bool? IndentSwitchExpression { get; }
    internal bool? IndentLambdaBlock { get; }
    internal bool? IndentBlockContents { get; }
    internal bool? IndentSwitchLabels { get; }
    internal bool? IndentCaseContents { get; }
    internal LabelIndentationStyle? LabelIndentation { get; }
    internal bool? IndentCaseContentsWhenBlock { get; }
    internal bool PreserveSingleLineBlocks { get; }
    internal bool ExpandSingleLineBlocks { get; }
    internal bool SeparateSingleLineStatements { get; }

    /// <summary>
    /// Whether expanding single-line blocks leaves alone the ones with nothing in them worth a
    /// line: an empty block and an accessor list of auto accessors.
    /// </summary>
    internal bool PreserveTrivialSingleLineBlocks { get; }
    internal EmbeddedStatementSettings EmbeddedStatements { get; }
    internal string? ParametersClosingDelimiterPosition { get; }
    internal string? MultilineParameterListOpenBracePosition { get; }
    internal bool AlignComments { get; }
    internal ulong InitializerMemberBoundaryRules { get; }

    /// <summary>
    /// The bit set of spacing rules that could claim a pair containing a token of this kind.
    /// </summary>
    internal ulong Trigger(int rawKind) => _triggers.GetValueOrDefault(rawKind);

    internal ulong NewLineTrigger(int rawKind) => _newLineWildcards | _newLineTriggers.GetValueOrDefault(rawKind);
    internal bool? InitializerIndentation(InitializerKind kind) => _initializerIndentations[(int)kind];
    internal bool HasInitializerLayout(InitializerKind kind) => _initializerLayouts[(int)kind];

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
        var initializerMemberBoundaryRules = 0UL;
        for (var index = 0; index < newLines.Count; index++)
        {
            var bit = 1UL << index;
            if (newLines[index].Item1.RequiresInitializerMemberBoundary)
            {
                newLineWildcards |= bit;
                initializerMemberBoundaryRules |= bit;
                continue;
            }

            foreach (var kind in newLines[index].Item1.EmitterTriggerKinds)
                newLineTriggers[(int)kind] = newLineTriggers.GetValueOrDefault((int)kind) | bit;
        }

        var initializerIndentations = new bool?[Enum.GetValues<InitializerKind>().Length];
        foreach (var rule in catalog.InitializerIndentationRules)
        {
            initializerIndentations[(int)rule.Kind] = OptionalIndentation(configuration.Preferences.GetValueOrDefault(rule.Metadata.RuleKey));
        }

        var initializerLayouts = new bool[Enum.GetValues<InitializerKind>().Length];
        foreach (var rule in catalog.SyntaxWrappingRules)
        {
            if (!rule.IsInitializerLayout
                || SyntaxWrappingRule.InitializerKindFor(rule.Kind) is not { } kind)
            {
                continue;
            }

            initializerLayouts[(int)kind] = configuration.Preferences.TryGetValue(rule.Metadata.RuleKey, out var preference)
                && !preference.Equals("unset", StringComparison.OrdinalIgnoreCase);
        }

        return new(
            [.. spacing],
            [.. newLines],
            triggers,
            newLineTriggers,
            newLineWildcards,
            initializerMemberBoundaryRules,
            RuleSettings.From(configuration),
            OptionalBoolean(configuration, RuleKey.CSharpIndentBraces),
            OptionalIndentation(configuration.Preferences.GetValueOrDefault(RuleKey.DressSwitchExpressionIndentation)),
            OptionalIndentation(configuration.Preferences.GetValueOrDefault(RuleKey.DressLambdaBlockIndentation)),
            OptionalBoolean(configuration, RuleKey.CSharpIndentBlockContents),
            OptionalBoolean(configuration, RuleKey.CSharpIndentSwitchLabels),
            OptionalBoolean(configuration, RuleKey.CSharpIndentCaseContents),
            OptionalLabelIndentation(configuration),
            OptionalBoolean(configuration, RuleKey.CSharpIndentCaseContentsWhenBlock),
            OptionalBoolean(configuration, RuleKey.CSharpPreserveSingleLineBlocks) == true,
            OptionalBoolean(configuration, RuleKey.CSharpPreserveSingleLineBlocks) == false,
            OptionalBoolean(configuration, RuleKey.CSharpPreserveSingleLineStatements) == false,
            OptionalBoolean(configuration, RuleKey.DressPreserveTrivialSingleLineBlocks) == true,
            initializerIndentations,
            initializerLayouts,
            EmbeddedStatementSettings.From(configuration),
            OptionalPreference(configuration, RuleKey.DressParametersClosingDelimiterPosition),
            OptionalPreference(configuration, RuleKey.DressMultilineParameterListOpenBracePosition),
            OptionalBoolean(configuration, RuleKey.DressCommentAlign) == true);
    }

    static bool? OptionalBoolean(FormattingConfiguration configuration, RuleKey key) =>
        configuration.Preferences.TryGetValue(key, out var value)
        && bool.TryParse(value, out var parsed)
            ? parsed
            : null;

    static string? OptionalPreference(FormattingConfiguration configuration, RuleKey key) =>
        configuration.Preferences.TryGetValue(key, out var value)
        && !value.Equals("unset", StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant()
            : null;

    static bool? OptionalIndentation(string? value) =>
        value?.Equals("indented", StringComparison.OrdinalIgnoreCase) == true
            ? true
            : value?.Equals("not_indented", StringComparison.OrdinalIgnoreCase) == true
                ? false
                : null;

    static LabelIndentationStyle? OptionalLabelIndentation(FormattingConfiguration configuration) =>
        configuration.Preferences.GetValueOrDefault(RuleKey.CSharpIndentLabels) switch {
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
