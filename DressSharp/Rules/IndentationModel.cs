using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

/// <summary>
/// Resolves indentation from syntax owners. Wrapping uses planned anchors; emission records
/// actual anchors so descendants follow any preserved or newly wrapped owner.
/// </summary>
/// <summary>
/// Whether syntax wrapping gives an item of a list a line of its own.
/// </summary>
/// <remarks>
/// A list is settled before anything inside it is, so by the time something inside asks, the
/// answer is there. Null when it is not, or when the list is not wrapping's to lay out.
/// </remarks>
interface IWrappedItems
{
    bool? StartsItsOwnLine(SyntaxNode list, SyntaxNode item);
}

sealed class IndentationModel
{
    readonly EmitterPlan _plan;
    readonly LayoutFacts _facts;
    readonly Dictionary<SyntaxNode, string> _anchors = [];
    readonly Dictionary<SyntaxNode, string> _braces = [];
    readonly Dictionary<SyntaxNode, SyntaxNode> _originalOwners = [];
    IWrappedItems? _wrapping;

    internal IndentationModel(EmitterPlan plan, SyntaxNode root, SyntaxRewritePlan rewrites, LayoutFacts? facts = null)
    {
        _plan = plan;
        _facts = facts ?? LayoutFacts.Syntax;
        foreach (var replacement in rewrites.Replacements)
            _originalOwners[replacement.Rewritten] = root.FindNode(replacement.Original, getInnermostNodeForTie: true);
    }

    /// <summary>
    /// Names the wrapping that is about to lay this file out, so that where an item of a list
    /// stands can be asked of it rather than of the file as it arrived.
    /// </summary>
    internal void Follows(IWrappedItems wrapping) => _wrapping = wrapping;

    internal void Remember(SyntaxNode node, string indent)
    {
        if (node is not BlockSyntax)
            _anchors[node] = indent;
    }

    internal void RememberBrace(SyntaxNode node, string indent) => _braces[node] = indent;

    internal bool TryGet(SyntaxNode node, out string indent) => _anchors.TryGetValue(node, out indent!);

    /// <summary>
    /// Where <paramref name="node"/> sits, crossing out of a member a syntax rule rewrote.
    /// </summary>
    /// <remarks>
    /// A rewritten member is its own detached root, so walking up from inside it stops at the member
    /// rather than reaching the type that holds it, and every question about what contains it
    /// answers null. This model is where the standing-in relation is recorded, so it answers them.
    /// </remarks>
    internal SyntaxNode? ParentOf(SyntaxNode node) =>
        node.Parent ?? _originalOwners.GetValueOrDefault(node)?.Parent;

    internal string Continuation(SyntaxNode node, int levels, string preservedIndent) =>
        (_plan.IndentBlockContents is not null ? ForNode(node) : preservedIndent)
        + string.Concat(Enumerable.Repeat(_plan.IndentUnit, levels));

    internal string? ExistingContinuation(SyntaxToken token)
    {
        var owner = token.Parent?.AncestorsAndSelf()
            .FirstOrDefault(node => node is StatementSyntax or MemberDeclarationSyntax);
        if (owner is null)
            return null;

        var indent = _facts.LeadingIndent(token);
        if (_plan.IndentBlockContents is null)
            return indent;

        var ownerIndent = _facts.LeadingIndent(owner.GetFirstToken(includeZeroWidth: true));
        return Rebase(ownerIndent, ForNode(owner), indent);
    }

    internal static string? Rebase(string sourceOwnerIndent, string emittedOwnerIndent, string sourceLineIndent) =>
        sourceLineIndent.StartsWith(sourceOwnerIndent, StringComparison.Ordinal)
            ? emittedOwnerIndent + sourceLineIndent[sourceOwnerIndent.Length..]
            : null;

    internal SyntaxNode? DirectContentFor(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            var parent = ParentOf(node);
            if (node is StatementSyntax && parent is BlockSyntax or SwitchSectionSyntax
                || node is MemberDeclarationSyntax && parent is BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax
                || node is AccessorDeclarationSyntax && parent is AccessorListSyntax
                || node is EnumMemberDeclarationSyntax && parent is EnumDeclarationSyntax
                || node is ExpressionSyntax && parent is InitializerExpressionSyntax
                || node is CollectionElementSyntax && parent is CollectionExpressionSyntax
                || node is SwitchExpressionArmSyntax && parent is SwitchExpressionSyntax
                || node is AnonymousObjectMemberDeclaratorSyntax && parent is AnonymousObjectCreationExpressionSyntax
                || node is SubpatternSyntax && parent is PropertyPatternClauseSyntax)
            {
                return node;
            }
        }

        return null;
    }

    internal string RebaseTokenText(SyntaxToken token, string sourceOwnerIndent, string emittedOwnerIndent)
    {
        if (token.Kind() is not (SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken))
            return token.Text;
        var text = _facts.TokenText(token);

        if (DirectContentFor(token) is { } owner && _anchors.TryGetValue(owner, out var emittedOwner))
        {
            sourceOwnerIndent = _facts.LeadingIndent(owner.GetFirstToken(includeZeroWidth: true));
            emittedOwnerIndent = emittedOwner;
        }
        var textSource = SourceText.From(text);
        var sourceMargin = LeadingIndent(textSource, textSource.Lines[^1]);
        if (Rebase(sourceOwnerIndent, emittedOwnerIndent, sourceMargin) is not { } emittedMargin
            || emittedMargin == sourceMargin)
        {
            return text;
        }

        var output = new StringBuilder(text.Length);
        output.Append(text, 0, textSource.Lines[1].Start);
        for (var index = 1; index < textSource.Lines.Count; index++)
        {
            var line = textSource.Lines[index];
            if (line.Start == text.Length)
                break;
            var lineIndent = LeadingIndent(textSource, line);
            var rebased = Rebase(sourceMargin, emittedMargin, lineIndent);
            if (rebased is null && lineIndent.Length != line.Span.Length)
                return text;

            output.Append(rebased ?? emittedMargin);
            output.Append(text, line.Start + lineIndent.Length, line.EndIncludingLineBreak - line.Start - lineIndent.Length);
        }

        return output.ToString();
    }

    internal string ForNode(SyntaxNode? node)
    {
        if (node is null)
            return "";
        if (_anchors.TryGetValue(node, out var indent))
            return indent;
        if (_originalOwners.TryGetValue(node, out var original))
            return ForNode(original);
        return node.Parent switch
            {
                ArgumentListSyntax arguments when node is ArgumentSyntax => ForNode(arguments) + PreservedContinuation(node, arguments),
                ArrowExpressionClauseSyntax clause when node is ConditionalExpressionSyntax or InvocationExpressionSyntax or AssignmentExpressionSyntax
                    && node == clause.Expression =>
                    ForNode(clause.Parent) + Unit(StartsAfterArrow(clause)),
                ConditionalExpressionSyntax conditional when node == conditional.WhenTrue || node == conditional.WhenFalse =>
                    ForNode(conditional) + _plan.IndentUnit,
                BlockSyntax block => Contents(block),
                SwitchSectionSyntax section => CaseContents(section, node is BlockSyntax),
                ElseClauseSyntax clause => ForNode(clause) + Unit(node is not (BlockSyntax or IfStatementSyntax)),
                StatementSyntax statement when node is StatementSyntax => ForNode(statement) + Unit(node is not BlockSyntax),
                BaseTypeDeclarationSyntax type when node is MemberDeclarationSyntax or EnumMemberDeclarationSyntax => Contents(type),
                NamespaceDeclarationSyntax ns => Contents(ns),
                AccessorListSyntax accessors => Contents(accessors),
                InitializerExpressionSyntax initializer => Contents(initializer),
                CollectionExpressionSyntax collection when node is CollectionElementSyntax => Contents(collection),
                AnonymousObjectCreationExpressionSyntax anonymous => Contents(anonymous),
                SwitchExpressionSyntax expression when node is SwitchExpressionArmSyntax => Contents(expression),
                PropertyPatternClauseSyntax pattern => Contents(pattern),
                _ => ForNode(node.Parent)
            };
    }

    internal string Brace(SyntaxNode owner)
    {
        if (_braces.TryGetValue(owner, out var indent))
            return indent;
        if (!owner.ContainsDiagnostics
            && !owner.ContainsDirectives
            && InitializerIndentationRule.KindOf(owner) is { } initializerKind
            && _plan.InitializerIndentation(initializerKind) is { } indentInitializer)
        {
            return ForNode(owner) + Unit(indentInitializer);
        }
        if (owner is BlockSyntax { Parent: LambdaExpressionSyntax function })
            return ForNode(function) + Unit(_plan.IndentLambdaBlock ?? _plan.IndentBraces ?? false);
        if (owner is BlockSyntax { Parent: SwitchSectionSyntax section })
            return CaseContents(section, true);
        return ForNode(owner) + Unit(owner is SwitchExpressionSyntax
            ? _plan.IndentSwitchExpression ?? _plan.IndentBraces ?? false
            : _plan.IndentBraces == true);
    }

    internal string Contents(SyntaxNode owner) =>
        Brace(owner) + Unit(_plan.IndentBlockContents == true
        || owner is BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax or AccessorListSyntax);

    internal string SwitchLabel(SwitchSectionSyntax section) =>
        Brace(section.Parent!) + Unit(_plan.IndentSwitchLabels ?? _plan.IndentBlockContents ?? false);

    string CaseContents(SwitchSectionSyntax section, bool block) =>
        SwitchLabel(section) + Unit((block ? _plan.IndentCaseContentsWhenBlock : _plan.IndentCaseContents) == true);

    /// <summary>
    /// How far an argument stands in from the list that holds it.
    /// </summary>
    /// <remarks>
    /// An argument that shares its line with the call is where the call is; one that has a line of
    /// its own is a continuation of it, and a continuation is one level in. Which of those it will
    /// be is wrapping's to say, and it says so before anything inside the argument is laid out.
    /// The file as it arrived answers only for a list wrapping has left alone.
    /// </remarks>
    string PreservedContinuation(SyntaxNode node, SyntaxNode owner) =>
        StartsItsOwnLine(node, owner) ? _plan.IndentUnit : "";

    bool StartsItsOwnLine(SyntaxNode node, SyntaxNode owner) =>
        _wrapping?.StartsItsOwnLine(owner, node)
        ?? _facts.StartLine(node.GetFirstToken(includeZeroWidth: true))
            != _facts.StartLine(owner.GetFirstToken(includeZeroWidth: true));

    bool StartsAfterArrow(ArrowExpressionClauseSyntax clause) =>
        _facts.StartLine(clause.Expression.GetFirstToken(includeZeroWidth: true))
        > _facts.StartLine(clause.ArrowToken);

    static string LeadingIndent(SourceText text, TextLine line)
    {
        var end = line.Start;
        while (end < line.End && text[end] is ' ' or '\t')
            end++;
        return text.ToString(TextSpan.FromBounds(line.Start, end));
    }

    string Unit(bool indented) => indented ? _plan.IndentUnit : "";
}
