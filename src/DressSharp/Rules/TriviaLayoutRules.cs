using System.Collections.Immutable;
using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class BlankLineRule(RuleKey key, BlankLineKind kind, string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = key,
        Name = RuleMetadata.Humanize(key.ToName()),
        GroupName = "Blank lines",
        Description = "Controls line breaks at structural boundaries. Only whitespace trivia changes.",
        Values = RuleValues.Integer(0),
        DefaultValue = defaultValue,
        Example = """
            namespace Example
            {
                class First { }
                class Second { }
            }
            """,
        OwnedSyntax = "line breaks at structural boundaries",
        Invariant = "Only whitespace trivia changes"
    };

    internal BlankLineKind Kind { get; } = kind;
}

sealed class CommentRule(RuleKey key, CommentKind kind, ImmutableArray<string> values, string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = key,
        Name = RuleMetadata.Humanize(key.ToName()),
        GroupName = "Comments",
        Description = "Controls comment-adjacent trivia. Comment text remains unchanged except for configured delimiter spacing.",
        Values = RuleValues.From(values),
        DefaultValue = defaultValue,
        Example = """
            /// <summary>Formats this example.</summary>
            class Example // attached comment
            {
                /*block comment*/
            }
            """,
        OwnedSyntax = "comment-adjacent trivia",
        Invariant = "Comment text remains unchanged except for configured delimiter spacing"
    };

    internal CommentKind Kind { get; } = kind;
}

enum BlankLineKind
{
    AroundNamespaces,
    AroundTypes,
    BetweenMembers,
    BetweenUsingGroups,
    BetweenMemberCategories,
    Maximum
}

enum CommentKind
{
    LineSpacing,
    BlockSpacing,
    AttachedPlacement,
    XmlPlacement,
    XmlElementLayout
}
