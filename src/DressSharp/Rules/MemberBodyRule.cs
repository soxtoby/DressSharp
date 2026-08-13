using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class MemberBodyRule(string key, MemberBodyKind kind, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
            ["block", "expression"],
        $"{kind.ToString().ToLowerInvariant()} bodies",
        RuleSafetyClass.SyntaxTransformation,
        "The selected body form preserves the represented statement or returned expression.",
        order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var rewriter = new Rewriter(kind, preference.Equals("expression", StringComparison.OrdinalIgnoreCase), context);
        return rewriter.Visit(root);
    }

    sealed class Rewriter(MemberBodyKind kind, bool expression, RuleContext context) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) =>
            kind == MemberBodyKind.Method
                ? RewriteCallable(node, node.Body, node.ExpressionBody, node.SemicolonToken, node.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword })
                : base.VisitMethodDeclaration(node);

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node) =>
            kind == MemberBodyKind.Constructor
                ? RewriteCallable(node, node.Body, node.ExpressionBody, node.SemicolonToken, true)
                : base.VisitConstructorDeclaration(node);

        public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node) =>
            kind == MemberBodyKind.Operator
                ? RewriteCallable(node, node.Body, node.ExpressionBody, node.SemicolonToken, false)
                : base.VisitOperatorDeclaration(node);

        public override SyntaxNode? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node) =>
            kind == MemberBodyKind.Operator
                ? RewriteCallable(node, node.Body, node.ExpressionBody, node.SemicolonToken, false)
                : base.VisitConversionOperatorDeclaration(node);

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node) =>
            kind == MemberBodyKind.Property
                ? RewriteProperty(node)
                : base.VisitPropertyDeclaration(node);

        public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax node) =>
            kind == MemberBodyKind.Indexer
                ? RewriteIndexer(node)
                : base.VisitIndexerDeclaration(node);

        public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node) =>
            kind == MemberBodyKind.Accessor
                ? RewriteAccessor(node)
                : base.VisitAccessorDeclaration(node);

        T RewriteCallable<T>(T node, BlockSyntax? body, ArrowExpressionClauseSyntax? arrow, SyntaxToken semicolon, bool statementBody) where T : SyntaxNode
        {
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;

            switch (expression)
            {
                case true when body is not null && TryExpression(body, statementBody, out var value):
                {
                    var clause = SyntaxFactory.ArrowExpressionClause(value).WithArrowToken(SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken).WithLeadingTrivia(body.OpenBraceToken.LeadingTrivia));
                    return (T)((dynamic)node).WithBody(null).WithExpressionBody(clause).WithSemicolonToken(SyntaxRuleSafety.SemicolonFrom(body.CloseBraceToken));
                }
                case false when arrow is not null:
                {
                    var statement = statementBody ? (StatementSyntax)SyntaxFactory.ExpressionStatement(arrow.Expression) : Return(arrow.Expression);
                    var block = SyntaxFactory.Block(statement).WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia)).WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithTrailingTrivia(semicolon.TrailingTrivia));
                    return (T)((dynamic)node).WithBody(block).WithExpressionBody(null).WithSemicolonToken(default(SyntaxToken));
                }
                default:
                {
                    return node;
                }
            }
        }

        PropertyDeclarationSyntax RewriteProperty(PropertyDeclarationSyntax node)
        {
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;

            switch (expression)
            {
                case true when (node.AccessorList?.Accessors is [{ Keyword.RawKind: (int)SyntaxKind.GetKeyword, Body: not null } accessor] && TryExpression(accessor.Body, false, out var value)):
                    return node
                        .WithAccessorList(null)
                        .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(value)
                            .WithArrowToken(SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken).WithLeadingTrivia(node.AccessorList.OpenBraceToken.LeadingTrivia)))
                        .WithSemicolonToken(SyntaxRuleSafety.SemicolonFrom(node.AccessorList.CloseBraceToken));
                case false when node.ExpressionBody is { } arrow:
                    return node
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithAccessorList(Getter(arrow, node.SemicolonToken));
                default:
                    return node;
            }
        }

        IndexerDeclarationSyntax RewriteIndexer(IndexerDeclarationSyntax node)
        {
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;

            switch (expression)
            {
                case true when (node.AccessorList?.Accessors is [{ Keyword.RawKind: (int)SyntaxKind.GetKeyword, Body: not null } accessor] && TryExpression(accessor.Body, false, out var value)):
                    return node
                        .WithAccessorList(null)
                        .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(value)
                            .WithArrowToken(SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken).WithLeadingTrivia(node.AccessorList.OpenBraceToken.LeadingTrivia)))
                        .WithSemicolonToken(SyntaxRuleSafety.SemicolonFrom(node.AccessorList.CloseBraceToken));
                case false when node.ExpressionBody is { } arrow:
                    return node
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithAccessorList(Getter(arrow, node.SemicolonToken));
                default:
                    return node;
            }
        }

        AccessorDeclarationSyntax RewriteAccessor(AccessorDeclarationSyntax node)
        {
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;

            var statementBody = node.IsKind(SyntaxKind.SetAccessorDeclaration) || node.IsKind(SyntaxKind.InitAccessorDeclaration) || node.IsKind(SyntaxKind.AddAccessorDeclaration) || node.IsKind(SyntaxKind.RemoveAccessorDeclaration);

            switch (expression)
            {
                case true when node.Body is not null && TryExpression(node.Body, statementBody, out var value):
                {
                    return node
                        .WithBody(null)
                        .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(value)
                            .WithArrowToken(SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken)
                                .WithLeadingTrivia(node.Body.OpenBraceToken.LeadingTrivia)))
                        .WithSemicolonToken(SyntaxRuleSafety.SemicolonFrom(node.Body.CloseBraceToken));
                }
                case false when node.ExpressionBody is { } arrow:
                {
                    StatementSyntax statement = statementBody 
                        ? SyntaxFactory.ExpressionStatement(arrow.Expression) 
                        : Return(arrow.Expression);
                    return node
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(SyntaxFactory.Block(statement)
                            .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                                .WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia))
                            .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                                .WithTrailingTrivia(node.SemicolonToken.TrailingTrivia)));
                }
                default:
                {
                    return node;
                }
            }
        }

        static AccessorListSyntax Getter(ArrowExpressionClauseSyntax arrow, SyntaxToken semicolon) =>
            SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, SyntaxFactory.Block(Return(arrow.Expression)))))
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                    .WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                    .WithTrailingTrivia(semicolon.TrailingTrivia));

        static ReturnStatementSyntax Return(ExpressionSyntax expression) =>
            SyntaxFactory.ReturnStatement(expression.WithoutLeadingTrivia())
                .WithReturnKeyword(SyntaxFactory.Token(SyntaxKind.ReturnKeyword).WithTrailingTrivia(SyntaxFactory.Space));

        static bool TryExpression(BlockSyntax body, bool statementBody, [NotNullWhen(true)] out ExpressionSyntax? expression)
        {
            expression = null;
            if (body.Statements.Count != 1)
                return false;
            
            switch (statementBody)
            {
                case false when body.Statements[0] is ReturnStatementSyntax { Expression: { } returned }:
                    expression = returned;
                    break;
                
                case true when body.Statements[0] is ExpressionStatementSyntax statement:
                    expression = statement.Expression;
                    break;
            }
            
            return expression is not null;
        }
    }
}

enum MemberBodyKind
{
    Method,
    Constructor,
    Operator,
    Property,
    Indexer,
    Accessor
}