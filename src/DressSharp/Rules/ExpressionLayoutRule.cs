using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class BinaryExpressionIndentationRule : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = RuleKey.DressBinaryExpressionIndentation,
            Caption = "Binary expression indentation",
            GroupName = "Wrapping",
            SubgroupName = "Expressions",
            Description = "Controls whether wrapped higher-precedence binary groups align with the outer expression or indent one additional level per precedence group. Does not introduce wrapping.",
            Values = RuleValues.Choice("flat", "precedence"),
            DefaultValue = "flat",
            Example = "var result = first\n    || second\n        && third;",
            OwnedSyntax = "wrapped binary expression indentation",
            Invariant = "Only whitespace changes"
        };
}

sealed class NestedConditionalStyleRule : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = RuleKey.DressNestedConditionalStyle,
            Caption = "Nested ternary style",
            GroupName = "Wrapping",
            SubgroupName = "Expressions",
            Description = "Shapes wrapped nested ternaries. Decision ladders flatten false-branch chains; multiline conditions or branches fall back to staircase. Does not force single-line expressions to wrap.",
            Values = RuleValues.Choice("flat", "staircase", "decision_ladder"),
            DefaultValue = "flat",
            Example = "var result = first\n    ? one\n    : second\n        ? two\n        : three;",
            OwnedSyntax = "nested conditional expression whitespace",
            Invariant = "Only whitespace changes"
        };
}

sealed class OperatorPlacementRule : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = RuleKey.DotnetStyleOperatorPlacementWhenWrapping,
            Caption = "Operator position",
            GroupName = "Wrapping",
            SubgroupName = "Expressions",
            Description = "Places wrapped binary and ternary operators at the beginning or end of the line. Does not introduce wrapping in single-line expressions. Commented operator boundaries are preserved.",
            Values = RuleValues.Choice("beginning_of_line", "end_of_line"),
            DefaultValue = "beginning_of_line",
            Example = "var result = first\n    + second\n    + third;",
            OwnedSyntax = "binary and conditional operator whitespace",
            Invariant = "Only whitespace changes"
        };
}
