using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class LambdaBlockIndentationRule : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = RuleKey.DressLambdaBlockIndentation,
            Caption = "Lambda block",
            ExpandedCaption = "Lambda block indentation",
            GroupName = "Indentation",
            Description = "Indents multiline lambda block braces one level from the lambda's starting line, or aligns them with that line. Overrides csharp_indent_braces for lambda blocks; contents follow csharp_indent_block_contents.",
            Values = RuleValues.Choice("indented", "not_indented"),
            DefaultValue = "not_indented",
            Example = """
                var callback = () =>
                {
                    Work();
                    return true;
                };
                """,
            OwnedSyntax = "multiline lambda block braces",
            Invariant = "Only indentation whitespace changes"
        };
}
