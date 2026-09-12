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
sealed class IndentationModel
{
    readonly EmitterPlan _plan;
    readonly Dictionary<SyntaxNode, string> _anchors = [];
    readonly Dictionary<SyntaxNode, string> _braces = [];
    readonly Dictionary<SyntaxNode, SyntaxNode> _originalOwners = [];

    internal IndentationModel(EmitterPlan plan, SyntaxNode root, SyntaxRewritePlan rewrites)
    {
        _plan = plan;
        foreach (var replacement in rewrites.Replacements)
            _originalOwners[replacement.Rewritten] = root.FindNode(replacement.Original, getInnermostNodeForTie: true);
    }

    internal void Remember(SyntaxNode node, string indent)
    {
        if (node is not BlockSyntax)
            _anchors[node] = indent;
    }

    internal void RememberBrace(SyntaxNode node, string indent) => _braces[node] = indent;

    internal bool TryGet(SyntaxNode node, out string indent) => _anchors.TryGetValue(node, out indent!);

    internal string Continuation(SyntaxNode node, int levels, string preservedIndent) =>
        (_plan.IndentBlockContents is not null ? ForNode(node) : preservedIndent)
        + string.Concat(Enumerable.Repeat(_plan.IndentUnit, levels));

    internal string? ExistingContinuation(SyntaxToken token)
    {
        var owner = token.Parent?.AncestorsAndSelf()
            .FirstOrDefault(node => node is StatementSyntax or MemberDeclarationSyntax);
        if (owner is null)
            return null;

        var text = owner.SyntaxTree.GetText();
        var indent = LeadingIndent(text, text.Lines.GetLineFromPosition(token.SpanStart));
        if (_plan.IndentBlockContents is null)
            return indent;

        var ownerIndent = LeadingIndent(text, text.Lines.GetLineFromPosition(owner.SpanStart));
        return Rebase(ownerIndent, ForNode(owner), indent);
    }

    internal static string? Rebase(string sourceOwnerIndent, string emittedOwnerIndent, string sourceLineIndent) =>
        sourceLineIndent.StartsWith(sourceOwnerIndent, StringComparison.Ordinal)
            ? emittedOwnerIndent + sourceLineIndent[sourceOwnerIndent.Length..]
            : null;

    internal static SyntaxNode? DirectContentFor(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is StatementSyntax && node.Parent is BlockSyntax or SwitchSectionSyntax
                || node is MemberDeclarationSyntax && node.Parent is BaseTypeDeclarationSyntax or BaseNamespaceDeclarationSyntax
                || node is AccessorDeclarationSyntax && node.Parent is AccessorListSyntax
                || node is EnumMemberDeclarationSyntax && node.Parent is EnumDeclarationSyntax
                || node is ExpressionSyntax && node.Parent is InitializerExpressionSyntax
                || node is CollectionElementSyntax && node.Parent is CollectionExpressionSyntax
                || node is SwitchExpressionArmSyntax && node.Parent is SwitchExpressionSyntax
                || node is AnonymousObjectMemberDeclaratorSyntax && node.Parent is AnonymousObjectCreationExpressionSyntax
                || node is SubpatternSyntax && node.Parent is PropertyPatternClauseSyntax)
            {
                return node;
            }
        }

        return null;
    }

    internal string RebaseTokenText(SyntaxToken token, string sourceOwnerIndent, string emittedOwnerIndent)
    {
        var text = token.Text;
        if (token.Kind() is not (SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken))
            return text;

        if (DirectContentFor(token) is { } owner && _anchors.TryGetValue(owner, out var emittedOwner))
        {
            var syntaxText = owner.SyntaxTree.GetText();
            sourceOwnerIndent = LeadingIndent(syntaxText, syntaxText.Lines.GetLineFromPosition(owner.SpanStart));
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
            ArrowExpressionClauseSyntax clause when node is ConditionalExpressionSyntax or InvocationExpressionSyntax
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

    static string PreservedContinuation(SyntaxNode node, SyntaxNode owner)
    {
        var text = node.SyntaxTree.GetText();
        var line = text.Lines.GetLineFromPosition(node.SpanStart);
        var ownerLine = text.Lines.GetLineFromPosition(owner.SpanStart);
        if (line.LineNumber == ownerLine.LineNumber)
            return "";

        var indent = LeadingIndent(text, line);
        var ownerIndent = LeadingIndent(text, ownerLine);
        return indent.StartsWith(ownerIndent, StringComparison.Ordinal)
            ? indent[ownerIndent.Length..]
            : "";
    }

    static bool StartsAfterArrow(ArrowExpressionClauseSyntax clause) =>
        clause.Expression.GetLocation().GetLineSpan().StartLinePosition.Line
        > clause.ArrowToken.GetLocation().GetLineSpan().StartLinePosition.Line;

    static string LeadingIndent(SourceText text, TextLine line)
    {
        var end = line.Start;
        while (end < line.End && text[end] is ' ' or '\t')
            end++;
        return text.ToString(TextSpan.FromBounds(line.Start, end));
    }

    string Unit(bool indented) => indented ? _plan.IndentUnit : "";
}
