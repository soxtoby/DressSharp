using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class CommentAlignmentRule : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = RuleKey.DressCommentAlign,
        Caption = "Alignment",
        ExpandedCaption = "Comment alignment",
        GroupName = "Comments",
        Description = "Aligns a comment with the code it leads, giving it the indentation that code is given. When false a comment keeps the offset its author gave it and moves with that code instead. Comment text remains unchanged.",
        Values = RuleValues.Boolean(),
        DefaultValue = "true",
        Example = """
            class Example
            {
                void Work()
                {
                        // Explains the call below.
                    Call();
                }
            }
            """,
        OwnedSyntax = "the indentation of a comment on its own line",
        Invariant = "Only indentation whitespace changes"
    };
}
