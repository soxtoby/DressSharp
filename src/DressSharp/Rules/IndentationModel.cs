using Microsoft.CodeAnalysis;
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
        return indent.StartsWith(ownerIndent, StringComparison.Ordinal)
            ? ForNode(owner) + indent[ownerIndent.Length..]
            : null;
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
