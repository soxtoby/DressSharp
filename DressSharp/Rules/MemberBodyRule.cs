using System.Diagnostics.CodeAnalysis;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class MemberBodyRule(RuleKey ruleKey, string caption, string? subgroupName, MemberBodyKind kind, string defaultValue) : ISyntaxFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = ruleKey,
            Caption = caption,
            ExpandedCaption = RuleMetadata.Humanize(ruleKey.ToName()),
            GroupName = "Braces and bodies",
            SubgroupName = subgroupName,
            Description = $"Controls {kind.ToString().ToLowerInvariant()} bodies. The selected body form preserves the represented statement or returned expression.",
            Values = RuleValues.From(["block", "expression"]),
            DefaultValue = defaultValue,
            Example = kind switch
                {
                    MemberBodyKind.Constructor => "class Example { int value; public Example(int value) { this.value = value; } }",
                    MemberBodyKind.Operator => "class Example { public static Example operator +(Example a, Example b) { return a; } }",
                    MemberBodyKind.Property => "class Example { int Value { get { return 1; } } }",
                    MemberBodyKind.Indexer => "class Example { int this[int index] { get { return index; } } }",
                    MemberBodyKind.Accessor => "class Example { int Value { get { return 1; } set { Store(value); } } }",
                    _ => "class Example { int Value() { return 1; } }"
                },
            OwnedSyntax = $"{kind.ToString().ToLowerInvariant()} bodies",
            Invariant = "The selected body form preserves the represented statement or returned expression."
        };

    /// <summary>
    /// The declaration kinds this rule's rewriter visits, which follow directly from its member kind.
    /// </summary>
    public System.Collections.Immutable.ImmutableArray<SyntaxKind> TargetKinds { get; } = kind switch
        {
            MemberBodyKind.Method => [SyntaxKind.MethodDeclaration],
            MemberBodyKind.Constructor => [SyntaxKind.ConstructorDeclaration],
            MemberBodyKind.Operator => [SyntaxKind.OperatorDeclaration, SyntaxKind.ConversionOperatorDeclaration],
            MemberBodyKind.Property => [SyntaxKind.PropertyDeclaration],
            MemberBodyKind.Indexer => [SyntaxKind.IndexerDeclaration],
            _ =>
                [
                    SyntaxKind.GetAccessorDeclaration,
                    SyntaxKind.SetAccessorDeclaration,
                    SyntaxKind.InitAccessorDeclaration,
                    SyntaxKind.AddAccessorDeclaration,
                    SyntaxKind.RemoveAccessorDeclaration,
                    SyntaxKind.UnknownAccessorDeclaration
                ]
        };

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var rewriter = new Rewriter(kind, preference.Equals("expression", StringComparison.OrdinalIgnoreCase), context);
        return rewriter.Visit(root);
    }

    sealed class Rewriter(MemberBodyKind kind, bool expression, RuleContext context) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) =>
            kind == MemberBodyKind.Method
                ? RewriteCallable(
                    node,
                    node.Body,
                    node.ExpressionBody,
                    node.SemicolonToken,
                    node.ReturnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword })
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
            if (!CanConvert(body, arrow, statementBody))
                return node;
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;

            switch (expression)
            {
                case true when body is not null && TryExpression(body, statementBody, out var value):
                {
                    var clause = SyntaxFactory.ArrowExpressionClause(value).WithArrowToken(Arrow());
                    var rewritten = (T)((dynamic)node).WithBody(null).WithExpressionBody(clause).WithSemicolonToken(
                        SyntaxRuleSafety.SemicolonFrom(body.CloseBraceToken));
                    return RemoveTriviaBeforeArrow(rewritten, ((dynamic)rewritten).ExpressionBody.ArrowToken);
                }
                case false when arrow is not null:
                {
                    var statement = statementBody ? (StatementSyntax)SyntaxFactory.ExpressionStatement(arrow.Expression) : Return(arrow.Expression);
                    var block = GeneratedSyntax
                        .Mark(SyntaxFactory.Block(statement))
                        .WithOpenBraceToken(
                            SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia))
                        .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithTrailingTrivia(semicolon.TrailingTrivia));
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
            if (!CanConvert(node))
                return node;
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;

            switch (expression)
            {
                case true when (node.AccessorList?.Accessors is [{ Keyword.RawKind: (int)SyntaxKind.GetKeyword, Body: not null } accessor]
                        && TryExpression(accessor.Body, false, out var value)):
                    var rewritten = node
                        .WithAccessorList(null)
                        .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(value)
                            .WithArrowToken(Arrow()))
                        .WithSemicolonToken(SyntaxRuleSafety.SemicolonFrom(node.AccessorList.CloseBraceToken));
                    return RemoveTriviaBeforeArrow(rewritten, rewritten.ExpressionBody!.ArrowToken);
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
            if (!CanConvert(node))
                return node;
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;

            switch (expression)
            {
                case true when (node.AccessorList?.Accessors is [{ Keyword.RawKind: (int)SyntaxKind.GetKeyword, Body: not null } accessor]
                        && TryExpression(accessor.Body, false, out var value)):
                    var rewritten = node
                        .WithAccessorList(null)
                        .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(value)
                            .WithArrowToken(Arrow()))
                        .WithSemicolonToken(SyntaxRuleSafety.SemicolonFrom(node.AccessorList.CloseBraceToken));
                    return RemoveTriviaBeforeArrow(rewritten, rewritten.ExpressionBody!.ArrowToken);
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
            var statementBody = node.IsKind(SyntaxKind.SetAccessorDeclaration)
                || node.IsKind(SyntaxKind.InitAccessorDeclaration)
                || node.IsKind(SyntaxKind.AddAccessorDeclaration)
                || node.IsKind(SyntaxKind.RemoveAccessorDeclaration);
            if (!CanConvert(node.Body, node.ExpressionBody, statementBody))
                return node;
            if (!SyntaxRuleSafety.CanRewrite(node, context))
                return node;

            switch (expression)
            {
                case true when node.Body is not null && TryExpression(node.Body, statementBody, out var value):
                {
                    var rewritten = node
                        .WithBody(null)
                        .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(value)
                            .WithArrowToken(Arrow()))
                        .WithSemicolonToken(SyntaxRuleSafety.SemicolonFrom(node.Body.CloseBraceToken));
                    return RemoveTriviaBeforeArrow(rewritten, rewritten.ExpressionBody!.ArrowToken);
                }
                case false when node.ExpressionBody is { } arrow:
                {
                    StatementSyntax statement = statementBody
                        ? SyntaxFactory.ExpressionStatement(arrow.Expression)
                        : Return(arrow.Expression);
                    return node
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(GeneratedSyntax.Mark(SyntaxFactory.Block(statement))
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
            SyntaxFactory.AccessorList(
                SyntaxFactory.SingletonList(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, GeneratedSyntax.Mark(SyntaxFactory.Block(Return(arrow.Expression))))))
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                    .WithLeadingTrivia(arrow.ArrowToken.LeadingTrivia))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                    .WithTrailingTrivia(semicolon.TrailingTrivia));

        static ReturnStatementSyntax Return(ExpressionSyntax expression) =>
            SyntaxFactory.ReturnStatement(expression.WithoutLeadingTrivia())
                .WithReturnKeyword(SyntaxFactory.Token(SyntaxKind.ReturnKeyword).WithTrailingTrivia(SyntaxFactory.Space));

        static SyntaxToken Arrow() =>
            SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken)
                .WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(SyntaxFactory.Space);

        static T RemoveTriviaBeforeArrow<T>(T node, SyntaxToken arrow) where T : SyntaxNode
        {
            var previous = arrow.GetPreviousToken();
            return (T)node.ReplaceToken(previous, previous.WithTrailingTrivia(default(SyntaxTriviaList)));
        }

        bool CanConvert(BlockSyntax? body, ArrowExpressionClauseSyntax? arrow, bool statementBody) =>
            expression
                ? body is { Statements: [ReturnStatementSyntax { Expression: not null }] } && !statementBody
                    || body is { Statements: [ExpressionStatementSyntax] } && statementBody
                : arrow is not null;

        bool CanConvert(PropertyDeclarationSyntax node) => expression
            ? node.AccessorList?.Accessors is [{ Keyword.RawKind: (int)SyntaxKind.GetKeyword, Body.Statements: [ReturnStatementSyntax { Expression: not null }] }]
            : node.ExpressionBody is not null;

        bool CanConvert(IndexerDeclarationSyntax node) => expression
            ? node.AccessorList?.Accessors is [{ Keyword.RawKind: (int)SyntaxKind.GetKeyword, Body.Statements: [ReturnStatementSyntax { Expression: not null }] }]
            : node.ExpressionBody is not null;

        static bool TryExpression(BlockSyntax body, bool statementBody, [NotNullWhen(true)] out ExpressionSyntax? expression)
        {
            expression = null;
            if (body.Statements.Count != 1)
                return false;

            switch (statementBody)
            {
                case false when body.Statements[0] is ReturnStatementSyntax { Expression: { } returned }:
                    expression = returned.WithoutLeadingTrivia();
                    break;

                case true when body.Statements[0] is ExpressionStatementSyntax statement:
                    expression = statement.Expression.WithoutLeadingTrivia();
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
