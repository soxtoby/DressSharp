using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

/// <summary>
/// The line breaks a rule other than wrapping puts into a file: a brace on its own line, an else
/// after one, a query clause given a line each.
/// </summary>
/// <remarks>
/// The emitter asks as it writes, and syntax wrapping asks before anything is written, because
/// whether a construct spans lines decides how the one holding it is laid out. Both have to get
/// the same answer, so both ask this.
/// </remarks>
sealed class ClaimedBreaks(EmitterPlan plan, RuleContext context, SyntaxNode root)
{
    readonly bool _checkMalformedRegions = root.ContainsDiagnostics;
    readonly Dictionary<SyntaxNode, bool?> _singleLine = [];
    SyntaxNode? _lastPreservationContainer;
    bool _lastPreservationContainerIsSafe;
    EffectiveTokenStream? _stream;
    TriviaLayoutPlan? _trivia;
    Func<int, bool>? _wrappedBreaks;
    bool _settled;

    /// <summary>
    /// Gives this the file's token stream and trivia, so that a block single-line in the source
    /// can be asked whether it stays that way once the breaks the rules claim are in it.
    /// </summary>
    internal void Follows(EffectiveTokenStream stream, TriviaLayoutPlan trivia)
    {
        _stream = stream;
        _trivia = trivia;
    }

    /// <summary>
    /// Tells this which gaps wrapping breaks, and whether those answers are final. Until they
    /// are, nothing about a block's line structure is remembered, because the solver deciding
    /// wrapping asks while its own answers are still changing.
    /// </summary>
    internal void Follows(Func<int, bool> wrappedBreaks, bool settled)
    {
        _wrappedBreaks = wrappedBreaks;
        _settled = settled;
        _singleLine.Clear();
    }

    /// <summary>
    /// Whether a rule puts <paramref name="token"/> on a line of its own, or null when none of
    /// them owns the boundary before it.
    /// </summary>
    /// <param name="leftOpenedItsLine">
    /// Whether the token before it began the line it sits on, which only a brace following a
    /// parameter list asks about. A caller that has not written the file yet passes false: the
    /// answer needs a break it would already have found.
    /// </param>
    internal bool? Before(SyntaxToken left, SyntaxToken token, bool leftOpenedItsLine)
    {
        // Where an embedded statement is placed is decided before any brace it might open is
        // looked at, and before the rules that would otherwise own that brace.
        if (EmbeddedStatementBreak(token) is { } placed)
            return placed;

        if (plan.PreserveSingleLineBlocks
            && token.IsKind(SyntaxKind.OpenBraceToken)
            && SingleLineBraceOwner(token, true) is { } owner
            && StaysSafeSingleLine(owner))
        {
            return false;
        }

        if (MultilineParameterListBraceBreak(left, token, leftOpenedItsLine) is { } parameterListBraceBreak)
            return parameterListBraceBreak;

        var candidates = plan.NewLineTrigger(token.RawKind);
        if (candidates == 0 || _checkMalformedRegions && IsUnsafeOriginal(token))
            return null;
        var initializerAtMemberBoundary = (candidates & plan.InitializerMemberBoundaryRules) != 0
            ? NewLineRule.InitializerAtMemberBoundary(token)
            : null;
        var initializer = token.Parent is AnonymousObjectCreationExpressionSyntax or InitializerExpressionSyntax
            ? token.Parent
            : initializerAtMemberBoundary;
        if (initializer is not null
            && InitializerIndentationRule.KindOf(initializer) is { } kind
            && plan.HasInitializerLayout(kind))
        {
            return null;
        }

        var rules = plan.NewLines;
        for (var index = 0; index < rules.Length; index++)
        {
            if ((candidates & (1UL << index)) != 0
                && rules[index].Rule.ClaimsBreakBefore(token, rules[index].Categories, initializerAtMemberBoundary) is { } claim)
                return claim;
        }

        return null;
    }

    bool? MultilineParameterListBraceBreak(SyntaxToken left, SyntaxToken right, bool leftOpenedItsLine)
    {
        if (plan.MultilineParameterListOpenBracePosition is not { } position
            || !right.IsKind(SyntaxKind.OpenBraceToken)
            || left.Parent is not ParameterListSyntax { Parameters.Count: > 0 } parameters
            || left != parameters.CloseParenToken
            || !IsDirectDeclarationBody(parameters, right)
            || !leftOpenedItsLine
            || _checkMalformedRegions && IsUnsafeOriginal(right))
        {
            return null;
        }

        return position == "next_line";
    }

    static bool IsDirectDeclarationBody(ParameterListSyntax parameters, SyntaxToken openBrace) =>
        openBrace.Parent switch
        {
            TypeDeclarationSyntax { ParameterList: { } typeParameters, BaseList: null } =>
                parameters == typeParameters,
            BlockSyntax { Parent: MethodDeclarationSyntax { ParameterList: { } methodParameters } } =>
                parameters == methodParameters,
            BlockSyntax { Parent: ConstructorDeclarationSyntax { ParameterList: { } constructorParameters, Initializer: null } } =>
                parameters == constructorParameters,
            _ => false
        };

    internal static SyntaxNode? SingleLineBraceOwner(SyntaxToken token, bool opening) => token.Parent switch
    {
        BlockSyntax block when token == (opening ? block.OpenBraceToken : block.CloseBraceToken) => block,
        AccessorListSyntax accessors when token == (opening ? accessors.OpenBraceToken : accessors.CloseBraceToken) => accessors,
        BaseTypeDeclarationSyntax type when token == (opening ? type.OpenBraceToken : type.CloseBraceToken) => type,
        NamespaceDeclarationSyntax @namespace when token == (opening ? @namespace.OpenBraceToken : @namespace.CloseBraceToken) => @namespace,
        SwitchStatementSyntax @switch when token == (opening ? @switch.OpenBraceToken : @switch.CloseBraceToken) => @switch,
        _ => null
    };

    bool? EmbeddedStatementBreak(SyntaxToken token)
    {
        var placement = plan.EmbeddedStatements.Placement;
        return placement is null
            || _checkMalformedRegions && IsUnsafeOriginal(token)
            || !EmbeddedStatements.StartsBody(FirstStatementAt(token), out _)
                ? null
                : placement == "next_line";
    }

    static StatementSyntax? FirstStatementAt(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is StatementSyntax statement)
                return statement.GetFirstToken() == token ? statement : null;
            if (node is MemberDeclarationSyntax)
                return null;
        }

        return null;
    }

    /// <summary>
    /// Whether the source keeps this container on one line and it is safe to lay out. This is
    /// what expanding single-line blocks asks: the block the author wrote on one line is the one
    /// to expand, whatever else this pass does inside it.
    /// </summary>
    internal bool IsSafeSingleLine(SyntaxNode container) =>
        IsSingleLine(container) && IsSafePreservationContainer(container);

    /// <summary>
    /// Whether this container is safe to lay out and stays on one line once written. This is
    /// what preserving single-line blocks asks: a block that will span lines has nothing left
    /// to preserve.
    /// </summary>
    bool StaysSafeSingleLine(SyntaxNode container) =>
        IsSingleLine(container) && IsSafePreservationContainer(container) && StaysSingleLine(container);

    static bool IsSingleLine(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return span.StartLinePosition.Line == span.EndLinePosition.Line;
    }

    /// <summary>
    /// Whether a container the source keeps on one line is still on one line once written.
    /// </summary>
    /// <remarks>
    /// A rule can put a break inside it: a clause of a query given its own line, an embedded
    /// statement placed on the next one, a call wrapping because it was too long. The container
    /// is then no longer single-line, and preserving its braces where they are would leave them
    /// to be moved by the next run. Asking the container itself while it is being asked about
    /// — its own closing brace is one of the gaps walked — answers as though nothing inside it
    /// breaks, which is the only answer that does not depend on itself.
    /// </remarks>
    bool StaysSingleLine(SyntaxNode node)
    {
        if (_stream is null || _trivia is null)
            return true;
        if (_singleLine.TryGetValue(node, out var known))
            return known ?? true;

        _singleLine[node] = null;
        var single = StaysOnOneLine(node);
        if (_settled)
            _singleLine[node] = single;
        else
            _singleLine.Remove(node);
        return single;
    }

    bool StaysOnOneLine(SyntaxNode node)
    {
        if (!_stream!.TryFind(node.GetFirstToken(), out var start)
            || !_stream.TryFind(node.GetLastToken(), out var end))
        {
            return true;
        }

        var pieces = _stream.Pieces;
        for (var index = start + 1; index <= end; index++)
        {
            if (_trivia!.HasLineBreak(index)
                || _wrappedBreaks?.Invoke(index) == true
                || Before(pieces[index - 1].Token, pieces[index].Token, leftOpenedItsLine: false) == true)
            {
                return false;
            }
        }

        return true;
    }

    internal bool IsSafePreservationContainer(SyntaxNode container)
    {
        if (ReferenceEquals(_lastPreservationContainer, container))
            return _lastPreservationContainerIsSafe;

        _lastPreservationContainer = container;
        _lastPreservationContainerIsSafe = CanRewritePreservingTrivia(container);
        return _lastPreservationContainerIsSafe;
    }

    internal bool IsUnsafeOriginal(SyntaxNode node) =>
        IsFromOriginalRoot(node) && context.IsUnsafe(node);

    internal bool IsUnsafeOriginal(SyntaxToken token) =>
        token.Parent is { } parent && IsFromOriginalRoot(parent) && context.IsUnsafe(token);

    bool IsFromOriginalRoot(SyntaxNode node)
    {
        while (node.Parent is { } parent)
            node = parent;
        return ReferenceEquals(node, root);
    }

    internal bool CanRewritePreservingTrivia(SyntaxNode node)
    {
        if (IsUnsafeOriginal(node))
            return false;
        foreach (var token in node.DescendantTokens())
        {
            if (SyntaxRuleSafety.HasSignificantTrivia(token.LeadingTrivia)
                || SyntaxRuleSafety.HasSignificantTrivia(token.TrailingTrivia))
            {
                return false;
            }
        }

        return true;
    }
}
