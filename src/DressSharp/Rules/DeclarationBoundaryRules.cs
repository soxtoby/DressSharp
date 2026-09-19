using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class MultilineParameterListOpenBracePositionRule : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = RuleKey.DressMultilineParameterListOpenBracePosition,
            Caption = "Open brace position",
            ExpandedCaption = "Multiline parameter list open brace position",
            GroupName = "Braces and bodies",
            SubgroupName = "Newlines",
            Description = "Places a declaration body brace on the same line as, or the line after, an own-line multiline parameter-list closing parenthesis. Applies to primary constructors without base lists, methods, and constructors without initializers.",
            Values = RuleValues.Choice("same_line", "next_line"),
            DefaultValue = "next_line",
            Example = "class Example\n{\n    void Run(\n        int value\n    ) {\n    }\n}",
            OwnedSyntax = "the boundary between an own-line multiline parameter-list closing parenthesis and its directly following declaration body brace",
            Invariant = "Base lists, constructor initializers, constraints, and declarations without an own-line parameter-list close are unchanged"
        };
}
