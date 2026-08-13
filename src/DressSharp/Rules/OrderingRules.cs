using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class UsingOrderRule(string key, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        key == "dress_global_using_order"
            ? ["first", "last", "mixed"]
            : Permutations(),
        "contiguous using directive runs",
        RuleSafetyClass.Layout,
        "Directives and comments remain boundaries",
        order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var unit = (CompilationUnitSyntax)root;
        unit = unit.WithUsings(Sort(unit.Usings, preference));
        return unit.ReplaceNodes(
            unit.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>(),
            (node, rewritten) => rewritten.WithUsings(Sort(rewritten.Usings, preference)));
    }

    SyntaxList<UsingDirectiveSyntax> Sort(SyntaxList<UsingDirectiveSyntax> source, string preference)
    {
        if (preference == "mixed" || source.Any(HasBoundary))
            return source;
        IEnumerable<UsingDirectiveSyntax> ordered = key == "dress_global_using_order"
            ? preference == "first"
                ? source.OrderByDescending(x => !x.GlobalKeyword.IsKind(SyntaxKind.None))
                : source.OrderBy(x => !x.GlobalKeyword.IsKind(SyntaxKind.None))
            : source.OrderBy(x => Rank(x, preference));
        return SyntaxFactory.List(ordered);
    }

    static bool HasBoundary(UsingDirectiveSyntax x) => x.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective || t.IsComment());

    static int Rank(UsingDirectiveSyntax x, string value)
    {
        var kind = x.Alias is not null
            ? "alias"
            : x.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                ? "static"
                : "ordinary";
        return Array.IndexOf(value.Split(',', StringSplitOptions.TrimEntries), kind);
    }

    static ImmutableArray<string> Permutations() =>
        new[] { "ordinary", "static", "alias" }
            .Permute()
            .Select(x => string.Join(',', x))
            .ToImmutableArray();
}

sealed class ModifierOrderRule(int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new("csharp_preferred_modifier_order", ["public,protected,internal,private,file,new,static,abstract,virtual,sealed,override,readonly,unsafe,required,volatile,async"], "member modifier lists", RuleSafetyClass.Layout, "Only modifier token order changes", order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var ranks = preference.Split(',', StringSplitOptions.TrimEntries).Select((x, i) => (x, i)).ToDictionary(x => x.x, x => x.i);
        return root.ReplaceNodes(root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(x => !context.IsUnsafe(x)), (node, rewritten) => rewritten.WithModifiers(SyntaxFactory.TokenList(rewritten.Modifiers.OrderBy(x => ranks.GetValueOrDefault(x.ValueText, int.MaxValue)))));
    }
}

static class RuleExtensions
{
    extension(SyntaxTrivia t)
    {
        internal bool IsWhitespaceOrEndOfLine() =>
            t.IsKind(SyntaxKind.WhitespaceTrivia)
            || t.IsKind(SyntaxKind.EndOfLineTrivia);

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