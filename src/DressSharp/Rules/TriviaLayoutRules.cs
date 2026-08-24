using System.Collections.Immutable;
using DressSharp.Architecture;

namespace DressSharp.Rules;

sealed class BlankLineRule(RuleKey key, BlankLineKind kind) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
            ["non-negative integer"],
        "line breaks at structural boundaries",
        "Only whitespace trivia changes")
        {
            ValueValidator = value => int.TryParse(value, out var count) && count >= 0
        };

    internal BlankLineKind Kind { get; } = kind;
}

sealed class CommentRule(RuleKey key, CommentKind kind, ImmutableArray<string> values) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        values,
        "comment-adjacent trivia",
        "Comment text remains unchanged except for configured delimiter spacing");

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