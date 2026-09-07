using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class SwitchExpressionIndentationRule : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = RuleKey.DressSwitchExpressionIndentation,
        Caption = "Switch expression",
        ExpandedCaption = "Switch expression indentation",
        GroupName = "Indentation",
        Description = "Indents multiline switch expression braces one level from the line containing switch, or aligns them with that line. Overrides csharp_indent_braces for switch expressions; arms follow csharp_indent_block_contents.",
        Values = RuleValues.Choice("indented", "not_indented"),
        DefaultValue = "indented",
        Example = """
            var value = input switch
                {
                    true => 1,
                    false => 0
                };
            """,
        OwnedSyntax = "multiline switch expression braces",
        Invariant = "Only indentation whitespace changes"
    };
}
