using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class NewLineRule(string key, NewLineKind kind, ImmutableArray<string> values, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        values,
        "owned token boundaries",
        RuleSafetyClass.Layout,
        "Only boundary whitespace changes",
        order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var boundaries = kind == NewLineKind.OpenBrace
            ? root.DescendantTokens()
                .Where(token => token.IsKind(SyntaxKind.OpenBraceToken) && BraceCategory(token) is not null)
                .Select(token => (
                    Token: token,
                    NewLine: preference == "all" || preference.Split(',', StringSplitOptions.TrimEntries).Contains(BraceCategory(token)!)))
            : Targets(root).Select(token => (Token: token, NewLine: preference == "true"));

        var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
        foreach (var (token, newLine) in boundaries.Where(item => SafeBoundary(item.Token, context)))
        {
            var previous = token.GetPreviousToken();
            var combined = previous.TrailingTrivia.Concat(token.LeadingTrivia).ToList();
            var lastLine = combined.FindLastIndex(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            var indent = lastLine >= 0 && lastLine + 1 < combined.Count && combined[lastLine + 1].IsKind(SyntaxKind.WhitespaceTrivia)
                ? combined[lastLine + 1].ToString()
                : "";
            var separator = newLine
                ? SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(context.LineEnding), SyntaxFactory.Whitespace(indent))
                : SyntaxFactory.TriviaList(SyntaxFactory.Space);
            var rewrittenPrevious = replacements.GetValueOrDefault(previous, previous);
            var rewrittenToken = replacements.GetValueOrDefault(token, token);
            replacements[previous] = rewrittenPrevious.WithTrailingTrivia(separator);
            replacements[token] = rewrittenToken.WithLeadingTrivia(default(SyntaxTriviaList));
        }

        return TokenRewriting.ReplaceTokens(root, replacements);
    }

    IEnumerable<SyntaxToken> Targets(SyntaxNode root) => kind switch
        {
            NewLineKind.Else => root.DescendantTokens().Where(token => token.IsKind(SyntaxKind.ElseKeyword)),
            NewLineKind.Catch => root.DescendantTokens().Where(token => token.IsKind(SyntaxKind.CatchKeyword)),
            NewLineKind.Finally => root.DescendantTokens().Where(token => token.IsKind(SyntaxKind.FinallyKeyword)),
            NewLineKind.ObjectInitializerMembers => root.DescendantNodes()
                .OfType<InitializerExpressionSyntax>()
                .Where(node => node.IsKind(SyntaxKind.ObjectInitializerExpression))
                .SelectMany(node => node.Expressions.Skip(1).Select(expression => expression.GetFirstToken())),
            NewLineKind.AnonymousTypeMembers => root.DescendantNodes()
                .OfType<AnonymousObjectCreationExpressionSyntax>()
                .SelectMany(node => node.Initializers.Skip(1).Select(member => member.GetFirstToken())),
            NewLineKind.QueryClauses => root.DescendantNodes()
                .OfType<QueryExpressionSyntax>()
                .SelectMany(query => query.Body.Clauses.Select(clause => clause.GetFirstToken()).Append(query.Body.SelectOrGroup.GetFirstToken())),
            _ => []
        };

    static bool SafeBoundary(SyntaxToken token, RuleContext context)
    {
        var previous = token.GetPreviousToken();
        return !context.IsUnsafe(token) && !context.IsUnsafe(previous)
            && previous.TrailingTrivia.Concat(token.LeadingTrivia)
                .None(trivia => trivia.IsDirective
                    || trivia.IsComment()
                    || trivia.IsKind(SyntaxKind.DisabledTextTrivia));
    }

    static string? BraceCategory(SyntaxToken token) => token.Parent switch
        {
            AccessorListSyntax { Parent: EventDeclarationSyntax } => "events",
            AccessorListSyntax { Parent: IndexerDeclarationSyntax } => "indexers",
            AccessorListSyntax { Parent: PropertyDeclarationSyntax } => "properties",
            AnonymousObjectCreationExpressionSyntax => "anonymous_types",
            BlockSyntax { Parent: AccessorDeclarationSyntax } => "accessors",
            BlockSyntax { Parent: AnonymousMethodExpressionSyntax } => "anonymous_methods",
            BlockSyntax { Parent: BaseMethodDeclarationSyntax } => "methods",
            BlockSyntax { Parent: LocalFunctionStatementSyntax } => "local_functions",
            BlockSyntax { Parent: ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax } => "lambdas",
            BlockSyntax => "control_blocks",
            BaseTypeDeclarationSyntax => "types",
            InitializerExpressionSyntax => "object_collection_array_initializers",
            _ => null
        };
}

enum NewLineKind
{
    OpenBrace,
    Else,
    Catch,
    Finally,
    ObjectInitializerMembers,
    AnonymousTypeMembers,
    QueryClauses
}

sealed class IndentationRule(string key, IndentationKind kind, ImmutableArray<string> values, int order) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new(
        key,
        values,
        "owned line indentation",
        RuleSafetyClass.Layout,
        "Only leading indentation changes",
        order);

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        if (kind == IndentationKind.Labels && preference == "no_change")
            return root;

        var targets = Targets(root, preference, context).DistinctBy(item => item.Token).ToArray();
        var replacements = new Dictionary<SyntaxToken, SyntaxToken>();
        foreach (var item in targets)
        {
            var previous = item.Token.GetPreviousToken();
            var rewrittenPrevious = replacements.GetValueOrDefault(previous, previous);
            var rewrittenToken = replacements.GetValueOrDefault(item.Token, item.Token);
            replacements[previous] = rewrittenPrevious.WithTrailingTrivia(SetIndent(previous.TrailingTrivia, item.Token.LeadingTrivia, item.Indent));
            replacements[item.Token] = rewrittenToken.WithLeadingTrivia(default(SyntaxTriviaList));
        }

        return TokenRewriting.ReplaceTokens(root, replacements);
    }

    IEnumerable<(SyntaxToken Token, string Indent)> Targets(SyntaxNode root, string preference, RuleContext context)
    {
        switch (kind)
        {
            case IndentationKind.BlockContents:
                foreach (var block in root.DescendantNodes().OfType<BlockSyntax>().Where(block => Safe(block, context)))
                foreach (var statement in block.Statements)
                    yield return (statement.GetFirstToken(), IndentOf(block.OpenBraceToken) + (preference == "true" ? context.IndentUnit : ""));
                break;
            case IndentationKind.Braces:
                foreach (var pair in BracePairs(root).Where(pair => Safe(pair.Owner, context)))
                {
                    var ownerIndent = IndentOf(pair.Owner.GetFirstToken());
                    var indent = ownerIndent + (preference == "true" ? context.IndentUnit : "");
                    yield return (pair.Open, indent);
                    yield return (pair.Close, indent);
                }

                break;
            case IndentationKind.SwitchLabels:
                foreach (var section in root.DescendantNodes().OfType<SwitchSectionSyntax>().Where(section => Safe(section, context)))
                foreach (var label in section.Labels)
                    yield return (label.GetFirstToken(), IndentOf(section.Parent!.ChildTokens().First(token => token.IsKind(SyntaxKind.OpenBraceToken))) + (preference == "true" ? context.IndentUnit : ""));
                break;
            case IndentationKind.CaseContents:
                foreach (var section in root.DescendantNodes().OfType<SwitchSectionSyntax>().Where(section => Safe(section, context)))
                foreach (var statement in section.Statements)
                    yield return (statement.GetFirstToken(), IndentOf(section.Labels[0].GetFirstToken()) + (preference == "true" ? context.IndentUnit : ""));
                break;
            case IndentationKind.CaseBlock:
                foreach (var section in root.DescendantNodes().OfType<SwitchSectionSyntax>().Where(section => Safe(section, context)))
                foreach (var block in section.Statements.OfType<BlockSyntax>())
                {
                    var indent = IndentOf(section.Labels[0].GetFirstToken()) + (preference == "true" ? context.IndentUnit : "");
                    yield return (block.OpenBraceToken, indent);
                    yield return (block.CloseBraceToken, indent);
                }

                break;
            case IndentationKind.Labels:
                foreach (var label in root.DescendantNodes().OfType<LabeledStatementSyntax>().Where(label => Safe(label, context)))
                {
                    var statementIndent = IndentOf(label.Statement.GetFirstToken());
                    yield return (label.GetFirstToken(), preference == "flush_left" ? "" : RemoveUnit(statementIndent, context.IndentUnit));
                }

                break;
        }
    }

    static bool Safe(SyntaxNode node, RuleContext context) => !context.IsUnsafe(node)
        && node.GetLeadingTrivia().None(trivia => trivia.IsDirective || trivia.IsComment() || trivia.IsKind(SyntaxKind.DisabledTextTrivia));

    static IEnumerable<(SyntaxNode Owner, SyntaxToken Open, SyntaxToken Close)> BracePairs(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case BlockSyntax block:
                    yield return (block.Parent!, block.OpenBraceToken, block.CloseBraceToken);
                    break;
                case BaseTypeDeclarationSyntax type:
                    yield return (type, type.OpenBraceToken, type.CloseBraceToken);
                    break;
                case NamespaceDeclarationSyntax ns:
                    yield return (ns, ns.OpenBraceToken, ns.CloseBraceToken);
                    break;
                case AccessorListSyntax accessors:
                    yield return (accessors.Parent!, accessors.OpenBraceToken, accessors.CloseBraceToken);
                    break;
                case InitializerExpressionSyntax initializer:
                    yield return (initializer, initializer.OpenBraceToken, initializer.CloseBraceToken);
                    break;
                case AnonymousObjectCreationExpressionSyntax anonymous:
                    yield return (anonymous, anonymous.OpenBraceToken, anonymous.CloseBraceToken);
                    break;
                case SwitchStatementSyntax statement:
                    yield return (statement, statement.OpenBraceToken, statement.CloseBraceToken);
                    break;
            }
        }
    }

    static SyntaxTriviaList SetIndent(SyntaxTriviaList trailing, SyntaxTriviaList leading, string indent)
    {
        var trivia = trailing.Concat(leading).ToList();
        var lastLine = trivia.FindLastIndex(item => item.IsKind(SyntaxKind.EndOfLineTrivia));
        if (lastLine < 0)
            return SyntaxFactory.TriviaList(trivia);
        if (lastLine + 1 < trivia.Count && trivia[lastLine + 1].IsKind(SyntaxKind.WhitespaceTrivia))
            trivia[lastLine + 1] = SyntaxFactory.Whitespace(indent);
        else if (indent.Length > 0)
            trivia.Insert(lastLine + 1, SyntaxFactory.Whitespace(indent));
        return SyntaxFactory.TriviaList(trivia);
    }

    static string IndentOf(SyntaxToken token)
    {
        var trivia = token.GetPreviousToken().TrailingTrivia.Concat(token.LeadingTrivia).ToList();
        var lastLine = -1;
        for (var index = trivia.Count - 1; index >= 0; index--)
        {
            if (trivia[index].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                lastLine = index;
                break;
            }
        }

        return lastLine >= 0 && lastLine + 1 < trivia.Count && trivia[lastLine + 1].IsKind(SyntaxKind.WhitespaceTrivia) ? trivia[lastLine + 1].ToString() : "";
    }

    static string RemoveUnit(string indent, string unit) => indent.EndsWith(unit, StringComparison.Ordinal) ? indent[..^unit.Length] : "";
}

enum IndentationKind
{
    CaseContents,
    SwitchLabels,
    Labels,
    BlockContents,
    Braces,
    CaseBlock
}