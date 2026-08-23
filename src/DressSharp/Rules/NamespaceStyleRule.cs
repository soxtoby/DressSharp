using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class NamespaceStyleRule : ISyntaxFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        RuleKey.DressNamespaceStyle,
            ["file_scoped", "block_scoped"],
        "compilation-unit namespace declaration",
        "The namespace name, externs, usings, attributes, and members are preserved while only the namespace delimiter form changes.");

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        if (root is not CompilationUnitSyntax unit)
            return root;
        return preference.Equals("file_scoped", StringComparison.OrdinalIgnoreCase) 
            ? ToFileScoped(unit, context)
            : ToBlockScoped(unit, context);
    }

    static CompilationUnitSyntax ToFileScoped(CompilationUnitSyntax unit, RuleContext context)
    {
        if (unit.Members is not [NamespaceDeclarationSyntax declaration] || !SyntaxRuleSafety.CanRewrite(declaration, context))
            return unit;
        var converted = SyntaxFactory.FileScopedNamespaceDeclaration(
                declaration.AttributeLists,
                declaration.Modifiers,
                declaration.NamespaceKeyword,
                declaration.Name,
                SyntaxRuleSafety.SemicolonFrom(declaration.OpenBraceToken),
                declaration.Externs,
                declaration.Usings,
                declaration.Members)
            .WithLeadingTrivia(declaration.GetLeadingTrivia())
            .WithTrailingTrivia(declaration.CloseBraceToken.TrailingTrivia.AddRange(declaration.GetTrailingTrivia()));
        return unit.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(converted));
    }

    static CompilationUnitSyntax ToBlockScoped(CompilationUnitSyntax unit, RuleContext context)
    {
        if (unit.Members is not [FileScopedNamespaceDeclarationSyntax declaration] || !SyntaxRuleSafety.CanRewrite(declaration, context))
            return unit;
        var converted = SyntaxFactory.NamespaceDeclaration(
                declaration.AttributeLists,
                declaration.Modifiers,
                declaration.NamespaceKeyword,
                declaration.Name,
                SyntaxFactory.Token(declaration.SemicolonToken.LeadingTrivia, SyntaxKind.OpenBraceToken, default),
                declaration.Externs,
                declaration.Usings,
                declaration.Members,
                SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithTrailingTrivia(declaration.SemicolonToken.TrailingTrivia),
                default)
            .WithLeadingTrivia(declaration.GetLeadingTrivia())
            .WithTrailingTrivia(declaration.GetTrailingTrivia());
        return unit.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(converted));
    }
}
