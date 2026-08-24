using System.Collections.Concurrent;
using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class UsingOrderRule(RuleKey ruleKey) : IUsingFormattingRule
{
    static readonly ConcurrentDictionary<string, Dictionary<string, int>> RankCache = new();

    public RuleMetadata Metadata { get; } = new(
        ruleKey,
        ruleKey == RuleKey.DressGlobalUsingOrder
            ? ["first", "last", "mixed"]
            : Permutations(),
        "contiguous using directive runs",
        "Directives and comments remain boundaries");

    public ImmutableArray<SyntaxKind> TargetKinds { get; } = [SyntaxKind.UsingDirective];

    public SyntaxList<UsingDirectiveSyntax> Rewrite(SyntaxList<UsingDirectiveSyntax> source, string preference, RuleContext context)
    {
        if (preference == "mixed" || source.Any(HasBoundary))
            return source;
        var ranks = ruleKey == RuleKey.DressGlobalUsingOrder
            ? null
            : Ranks(preference);
        return IsOrdered(source, RankOf)
            ? source
            : SyntaxFactory.List(source.OrderBy(RankOf));

        int RankOf(UsingDirectiveSyntax directive) => ruleKey == RuleKey.DressGlobalUsingOrder
            ? preference == "first" == !directive.GlobalKeyword.IsKind(SyntaxKind.None) ? 0 : 1
            : Rank(directive, ranks!);
    }

    static bool HasBoundary(UsingDirectiveSyntax x) => x.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective || t.IsComment());

    static int Rank(UsingDirectiveSyntax directive, Dictionary<string, int> ranks)
    {
        var kind = directive.Alias is not null
            ? "alias"
            : directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                ? "static"
                : "ordinary";
        return ranks[kind];
    }

    static Dictionary<string, int> Ranks(string preference) => RankCache.GetOrAdd(
        preference,
        static value => value.Split(',', StringSplitOptions.TrimEntries)
            .Select((kind, rank) => (kind, rank))
            .ToDictionary(entry => entry.kind, entry => entry.rank));

    static bool IsOrdered(SyntaxList<UsingDirectiveSyntax> source, Func<UsingDirectiveSyntax, int> rank)
    {
        var previous = -1;
        foreach (var directive in source)
        {
            var current = rank(directive);
            if (current < previous)
                return false;
            previous = current;
        }

        return true;
    }

    static ImmutableArray<string> Permutations() =>
        new[] { "ordinary", "static", "alias" }
            .Permute()
            .Select(x => string.Join(',', x))
            .ToImmutableArray();
}

sealed class ModifierOrderRule : ISyntaxFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        RuleKey.CSharpPreferredModifierOrder,
        ["public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async"],
        "member modifier lists",
        "Only modifier token order changes");

    static readonly ConcurrentDictionary<string, Dictionary<string, int>> RankCache = new();

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        if (root is not MemberDeclarationSyntax member)
            return root;
        var ranks = RankCache.GetOrAdd(preference, static value => value
            .Split(',', StringSplitOptions.TrimEntries)
            .Select((name, rank) => (name, rank))
            .ToDictionary(entry => entry.name, entry => entry.rank));
        return IsOutOfOrder(member.Modifiers, ranks) && !context.IsUnsafe(member)
            ? member.WithModifiers(SyntaxFactory.TokenList(member.Modifiers.OrderBy(modifier => Rank(modifier, ranks))))
            : member;
    }

    /// <summary>
    /// Whether reordering would actually move anything.
    /// </summary>
    /// <remarks>
    /// The sort is stable, so a member whose modifiers already rank in order sorts to itself. Asking
    /// first matters because rebuilding the member regardless makes every member look changed, which
    /// costs a rewrite here and denies callers any way to tell the untouched members apart.
    /// </remarks>
    static bool IsOutOfOrder(SyntaxTokenList modifiers, Dictionary<string, int> ranks)
    {
        if (modifiers.Count < 2)
            return false;

        var previous = -1;
        foreach (var modifier in modifiers)
        {
            var rank = Rank(modifier, ranks);
            if (rank < previous)
                return true;
            previous = rank;
        }

        return false;
    }

    static int Rank(SyntaxToken modifier, Dictionary<string, int> ranks) =>
        ranks.GetValueOrDefault(modifier.ValueText, int.MaxValue);
}

static class RuleExtensions
{
    extension(SyntaxTrivia t)
    {
        internal bool IsComment() =>
            t.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }

    internal static IEnumerable<IEnumerable<T>> Permute<T>(this IEnumerable<T> values)
    {
        var a = values.ToArray();
        return a.Length == 0
            ? [[]]
            : a.SelectMany((x, i) => a
                .Where((_, j) => j != i)
                .Permute()
                .Select(rest => new[] { x }.Concat(rest)));
    }
}
