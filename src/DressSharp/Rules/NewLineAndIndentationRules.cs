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

    internal NewLineKind Kind => kind;

    public SyntaxNode Transform(SyntaxNode root, string preference, RuleContext context)
    {
        var buffer = new TriviaBuffer();
        Fill(buffer, root, preference, context);
        return buffer.Apply(root);
    }

    internal void Fill(TriviaBuffer buffer, SyntaxNode root, string preference, RuleContext context)
    {
        var boundaries = kind == NewLineKind.OpenBrace
            ? root.DescendantTokens()
                .Where(token => token.IsKind(SyntaxKind.OpenBraceToken) && BraceCategory(token) is not null)
                .Select(token => (
                    Token: token,
                    NewLine: preference == "all" || preference.Split(',', StringSplitOptions.TrimEntries).Contains(BraceCategory(token)!)))
            : Targets(root).Select(token => (Token: token, NewLine: preference == "true"));

        foreach (var (token, newLine) in boundaries.Where(item => SafeBoundary(buffer, item.Token, context)))
        {
            var previous = token.GetPreviousToken();
            var combined = buffer.Trailing(previous).Concat(buffer.Leading(token)).ToList();
            var lastLine = combined.FindLastIndex(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
            var indent = lastLine >= 0 && lastLine + 1 < combined.Count && combined[lastLine + 1].IsKind(SyntaxKind.WhitespaceTrivia)
                ? combined[lastLine + 1].ToString()
                : "";
            var separator = newLine
                ? SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(context.LineEnding), SyntaxFactory.Whitespace(indent))
                : SyntaxFactory.TriviaList(SyntaxFactory.Space);
            buffer.SetTrailing(previous, separator);
            buffer.SetLeading(token, default);
        }
    }

    /// <summary>
    /// Whether this rule owns the boundary in front of the token, and if so whether it wants a line
    /// break there. Null means the rule has no opinion about that boundary.
    /// </summary>
    /// <remarks>
    /// The emitter reaches tokens one at a time and needs an answer per boundary, where the pipeline
    /// gathers targets by querying the whole tree. Both ask the same question of the same syntax.
    /// </remarks>
    internal bool? ClaimsBreakBefore(SyntaxToken token, BraceCategories categories) => kind switch
        {
            NewLineKind.OpenBrace => token.IsKind(SyntaxKind.OpenBraceToken) && BraceCategory(token) is { } category
                ? categories.Contains(category)
                : null,
            NewLineKind.Else => token.IsKind(SyntaxKind.ElseKeyword) ? categories.Enabled : null,
            NewLineKind.Catch => token.IsKind(SyntaxKind.CatchKeyword) ? categories.Enabled : null,
            NewLineKind.Finally => token.IsKind(SyntaxKind.FinallyKeyword) ? categories.Enabled : null,
            NewLineKind.ObjectInitializerMembers => StartsLaterElement(token) is InitializerExpressionSyntax initializer
                && initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
                    ? categories.Enabled
                    : null,
            NewLineKind.AnonymousTypeMembers => StartsLaterElement(token) is AnonymousObjectCreationExpressionSyntax
                ? categories.Enabled
                : null,
            NewLineKind.QueryClauses => StartsQueryClause(token) ? categories.Enabled : null,
            _ => null
        };

    /// <summary>
    /// The list owner whose second or later element starts at this token, if any.
    /// </summary>
    static SyntaxNode? StartsLaterElement(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node.GetFirstToken() != token)
                return null;
            var owner = node.Parent;
            var elements = owner switch
                {
                    InitializerExpressionSyntax initializer => (IReadOnlyList<SyntaxNode>)initializer.Expressions,
                    AnonymousObjectCreationExpressionSyntax anonymous => anonymous.Initializers,
                    _ => null
                };
            if (elements is not null)
                return elements.Count > 0 && !ReferenceEquals(elements[0], node) ? owner : null;
        }

        return null;
    }

    static bool StartsQueryClause(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node.GetFirstToken() != token)
                return false;
            if (node.Parent is QueryBodySyntax body)
                return body.Clauses.Contains(node) || ReferenceEquals(body.SelectOrGroup, node);
        }

        return false;
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

    static bool SafeBoundary(TriviaBuffer buffer, SyntaxToken token, RuleContext context)
    {
        var previous = token.GetPreviousToken();
        return !context.IsUnsafe(token) && !context.IsUnsafe(previous)
            && buffer.Trailing(previous).Concat(buffer.Leading(token))
                .None(trivia => trivia.IsDirective
                    || trivia.IsComment()
                    || trivia.IsKind(SyntaxKind.DisabledTextTrivia));
    }

    /// <summary>
    /// A rule's preference value resolved ahead of the walk, so that deciding a boundary is a lookup
    /// rather than a string split per token.
    /// </summary>
    internal sealed class BraceCategories
    {
        readonly HashSet<string>? _selected;

        BraceCategories(bool enabled, HashSet<string>? selected)
        {
            Enabled = enabled;
            _selected = selected;
        }

        internal bool Enabled { get; }

        internal bool Contains(string category) => _selected is null ? Enabled : _selected.Contains(category);

        internal static BraceCategories From(NewLineKind kind, string preference)
        {
            if (kind != NewLineKind.OpenBrace)
                return new(preference.Equals("true", StringComparison.OrdinalIgnoreCase), null);
            if (preference.Equals("all", StringComparison.OrdinalIgnoreCase))
                return new(true, null);
            if (preference.Equals("none", StringComparison.OrdinalIgnoreCase))
                return new(false, null);
            return new(false, [.. preference.Split(',', StringSplitOptions.TrimEntries)]);
        }
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
        var buffer = new TriviaBuffer();
        Fill(buffer, root, preference, context);
        return buffer.Apply(root);
    }

    internal void Fill(TriviaBuffer buffer, SyntaxNode root, string preference, RuleContext context)
    {
        if (kind == IndentationKind.Labels && preference == "no_change")
            return;

        var targets = Targets(buffer, root, preference, context).DistinctBy(item => item.Token).ToArray();
        foreach (var item in targets)
        {
            var previous = item.Token.GetPreviousToken();
            buffer.SetTrailing(previous, SetIndent(buffer.Trailing(previous), buffer.Leading(item.Token), item.Indent));
            buffer.SetLeading(item.Token, default);
        }
    }

    IEnumerable<(SyntaxToken Token, string Indent)> Targets(TriviaBuffer buffer, SyntaxNode root, string preference, RuleContext context)
    {
        switch (kind)
        {
            case IndentationKind.BlockContents:
                foreach (var block in root.DescendantNodes().OfType<BlockSyntax>().Where(block => Safe(buffer, block, context)))
                foreach (var statement in block.Statements)
                    yield return (statement.GetFirstToken(), IndentOf(buffer, block.OpenBraceToken) + (preference == "true" ? context.IndentUnit : ""));
                break;
            case IndentationKind.Braces:
                foreach (var pair in BracePairs(root).Where(pair => Safe(buffer, pair.Owner, context)))
                {
                    var ownerIndent = IndentOf(buffer, pair.Owner.GetFirstToken());
                    var indent = ownerIndent + (preference == "true" ? context.IndentUnit : "");
                    yield return (pair.Open, indent);
                    yield return (pair.Close, indent);
                }

                break;
            case IndentationKind.SwitchLabels:
                foreach (var section in root.DescendantNodes().OfType<SwitchSectionSyntax>().Where(section => Safe(buffer, section, context)))
                foreach (var label in section.Labels)
                    yield return (label.GetFirstToken(), IndentOf(buffer, section.Parent!.ChildTokens().First(token => token.IsKind(SyntaxKind.OpenBraceToken))) + (preference == "true" ? context.IndentUnit : ""));
                break;
            case IndentationKind.CaseContents:
                foreach (var section in root.DescendantNodes().OfType<SwitchSectionSyntax>().Where(section => Safe(buffer, section, context)))
                foreach (var statement in section.Statements)
                    yield return (statement.GetFirstToken(), IndentOf(buffer, section.Labels[0].GetFirstToken()) + (preference == "true" ? context.IndentUnit : ""));
                break;
            case IndentationKind.CaseBlock:
                foreach (var section in root.DescendantNodes().OfType<SwitchSectionSyntax>().Where(section => Safe(buffer, section, context)))
                foreach (var block in section.Statements.OfType<BlockSyntax>())
                {
                    var indent = IndentOf(buffer, section.Labels[0].GetFirstToken()) + (preference == "true" ? context.IndentUnit : "");
                    yield return (block.OpenBraceToken, indent);
                    yield return (block.CloseBraceToken, indent);
                }

                break;
            case IndentationKind.Labels:
                foreach (var label in root.DescendantNodes().OfType<LabeledStatementSyntax>().Where(label => Safe(buffer, label, context)))
                {
                    var statementIndent = IndentOf(buffer, label.Statement.GetFirstToken());
                    yield return (label.GetFirstToken(), preference == "flush_left" ? "" : RemoveUnit(statementIndent, context.IndentUnit));
                }

                break;
        }
    }

    static bool Safe(TriviaBuffer buffer, SyntaxNode node, RuleContext context) => !context.IsUnsafe(node)
        && buffer.Leading(node.GetFirstToken()).None(trivia => trivia.IsDirective || trivia.IsComment() || trivia.IsKind(SyntaxKind.DisabledTextTrivia));

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

    static string IndentOf(TriviaBuffer buffer, SyntaxToken token)
    {
        var trivia = buffer.Trailing(token.GetPreviousToken()).Concat(buffer.Leading(token)).ToList();
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

/// <summary>
/// Applies a run of line-break and indentation rules in a single walk.
/// </summary>
/// <remarks>
/// These rules all express themselves as edits to the trivia between a token and the one before it,
/// and they read that same trivia to decide. Sharing a <see cref="TriviaBuffer"/> lets a later rule
/// see an earlier one's edits, which is exactly what it would see if the earlier rule had rebuilt
/// the tree first, so the run collapses to one rebuild.
/// </remarks>
static class LineLayoutBatch
{
    internal static SyntaxNode Apply(
        SyntaxNode root,
        ReadOnlySpan<(IFormattingRule Rule, string Preference)> rules,
        RuleContext context)
    {
        var buffer = new TriviaBuffer();
        foreach (var (rule, preference) in rules)
        {
            switch (rule)
            {
                case NewLineRule newLine:
                    newLine.Fill(buffer, root, preference, context);
                    break;
                case IndentationRule indentation:
                    indentation.Fill(buffer, root, preference, context);
                    break;
                default:
                    throw new InvalidOperationException($"{rule.Metadata.PreferenceKey} does not belong to this run.");
            }
        }

        return buffer.Apply(root);
    }

    internal static bool Handles(IFormattingRule rule) => rule is NewLineRule or IndentationRule;
}
