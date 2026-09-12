using System.Collections.Immutable;
using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class BlankLineRule(RuleKey key, string caption, string? subgroupName, BlankLineKind kind, string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = key,
        Caption = caption,
        ExpandedCaption = RuleMetadata.Humanize(key.ToName()),
        GroupName = "Blank lines",
        SubgroupName = subgroupName,
        Description = "Controls line breaks at structural boundaries. Only whitespace trivia changes.",
        Values = RuleValues.Integer(0),
        DefaultValue = defaultValue,
        Example = kind switch
        {
            BlankLineKind.AroundNamespaces => "namespace First { }\nnamespace Second { }",
            BlankLineKind.BetweenMembers => "class Example\n{\n    void First() { }\n    void Second() { }\n}",
            BlankLineKind.BetweenUsingGroups => "using System;\nusing static System.Math;\nusing Alias = System.String;\nclass Value { }",
            BlankLineKind.BetweenMemberCategories => "class Example\n{\n    int value;\n    void Run() { }\n}",
            BlankLineKind.Maximum => "class First { }\n\n\n\n\nclass Second { }",
            _ => """
            namespace Example
            {
                class First { }
                class Second { }
            }
            """
        },
        OwnedSyntax = "line breaks at structural boundaries",
        Invariant = "Only whitespace trivia changes"
    };

    internal BlankLineKind Kind { get; } = kind;
}

sealed class CommentRule(RuleKey key, string caption, string? subgroupName, CommentKind kind, ImmutableArray<string> values, string defaultValue) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        RuleKey = key,
        Caption = caption,
        ExpandedCaption = RuleMetadata.Humanize(key.ToName()),
        GroupName = "Comments",
        SubgroupName = subgroupName,
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
