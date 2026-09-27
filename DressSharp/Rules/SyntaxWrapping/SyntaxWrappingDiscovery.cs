using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static DressSharp.Rules.SyntaxWrappingRendering;

namespace DressSharp.Rules;

sealed class SyntaxWrappingDiscovery
{
    readonly SyntaxNode _root;
    readonly EffectiveTokenStream _stream;
    readonly SyntaxWrappingSettings.Setting?[] _byKind;
    readonly RuleContext? _safetyContext;
    readonly List<Occurrence> _occurrences = [];
    readonly List<Boundary> _boundaries = [];
    int _currentSegmentIndex;

    SyntaxWrappingDiscovery(
        SyntaxNode root,
        EffectiveTokenStream stream,
        SyntaxWrappingSettings.Setting?[] byKind,
        RuleSettings settings)
    {
        _root = root;
        _stream = stream;
        _byKind = byKind;
        if (root.ContainsDiagnostics || root.ContainsDirectives)
            _safetyContext = new(root, settings);
    }

    internal static Result Find(
        SyntaxNode root,
        EffectiveTokenStream stream,
        SyntaxWrappingSettings.Setting?[] byKind,
        RuleSettings settings)
    {
        var discovery = new SyntaxWrappingDiscovery(root, stream, byKind, settings);
        discovery.Discover();
        return new(
            discovery._occurrences.ToArray(),
            discovery._boundaries.ToArray(),
            discovery._safetyContext?.TakeSkippedOccurrences() ?? 0);
    }

    void Discover()
    {
        foreach (var index in _stream.TriggerIndices)
        {
            _currentSegmentIndex = _stream.Pieces[index].SegmentIndex;
            Discover(_stream.Pieces[index].Token);
        }

        SortAndLinkOccurrences();
    }

    void Discover(SyntaxToken token)
    {
        switch ((SyntaxKind)token.RawKind)
        {
            case SyntaxKind.OpenParenToken:
                if (Enabled(SyntaxWrappingKind.Arguments) is not null
                    && token.Parent is ArgumentListSyntax { Arguments.Count: > 0 } arguments
                    && token == arguments.OpenParenToken)
                {
                    AddOccurrence(arguments, SyntaxWrappingKind.Arguments);
                }
                else if (Enabled(SyntaxWrappingKind.Parameters) is not null
                    && token.Parent is ParameterListSyntax { Parameters.Count: > 0 } parameters
                    && token == parameters.OpenParenToken)
                {
                    AddOccurrence(parameters, SyntaxWrappingKind.Parameters);
                }

                break;
            case SyntaxKind.OpenBracketToken:
                DiscoverOpenBracket(token);
                break;
            case SyntaxKind.OpenBraceToken:
                if (token.Parent is AnonymousObjectCreationExpressionSyntax { Initializers.Count: > 0 } anonymous
                    && token == anonymous.OpenBraceToken
                    && Enabled(SyntaxWrappingKind.ObjectInitializers) is not null)
                {
                    AddOccurrence(anonymous, SyntaxWrappingKind.ObjectInitializers);
                }
                else if (token.Parent is InitializerExpressionSyntax initializer
                    && token == initializer.OpenBraceToken
                    && InitializerIndentationRule.KindOf(initializer) is { } initializerKind
                    && SyntaxWrappingRule.KindFor(initializerKind) is { } wrappingKind
                    && Enabled(wrappingKind) is not null)
                {
                    AddOccurrence(initializer, wrappingKind);
                }

                break;
            case SyntaxKind.ColonToken:
                if (Enabled(SyntaxWrappingKind.BaseTypeLists) is not null
                    && token.Parent is BaseListSyntax { Types.Count: > 0 } baseList
                    && token == baseList.ColonToken)
                {
                    AddOccurrence(baseList, SyntaxWrappingKind.BaseTypeLists);
                }

                break;
            case SyntaxKind.WhereKeyword:
                DiscoverConstraints(token);
                break;
            case SyntaxKind.FromKeyword:
                DiscoverQuery(token);
                break;
            case SyntaxKind.DotToken:
            case SyntaxKind.MinusGreaterThanToken:
                DiscoverMemberAccess(token);
                break;
            case SyntaxKind.QuestionToken:
                DiscoverQuestion(token);
                break;
            default:
                DiscoverBinary(token);
                break;
        }
    }

    void DiscoverOpenBracket(SyntaxToken token)
    {
        if (Enabled(SyntaxWrappingKind.Arguments) is not null
            && token.Parent is BracketedArgumentListSyntax { Arguments.Count: > 0 } arguments
            && token == arguments.OpenBracketToken)
        {
            AddOccurrence(arguments, SyntaxWrappingKind.Arguments);
        }
        else if (Enabled(SyntaxWrappingKind.Parameters) is not null
            && token.Parent is BracketedParameterListSyntax { Parameters.Count: > 0 } parameters
            && token == parameters.OpenBracketToken)
        {
            AddOccurrence(parameters, SyntaxWrappingKind.Parameters);
        }
        else if (Enabled(SyntaxWrappingKind.CollectionExpressions) is not null
            && token.Parent is CollectionExpressionSyntax { Elements.Count: > 0 } collection
            && token == collection.OpenBracketToken)
        {
            AddOccurrence(collection, SyntaxWrappingKind.CollectionExpressions);
        }
        else if (Enabled(SyntaxWrappingKind.Attributes) is not null
            && token.Parent is AttributeListSyntax { Attributes.Count: > 0 } attributes
            && token == attributes.OpenBracketToken)
        {
            AddOccurrence(attributes, SyntaxWrappingKind.Attributes);
        }
    }

    void DiscoverConstraints(SyntaxToken token)
    {
        if (Enabled(SyntaxWrappingKind.ConstraintClauses) is null
            || token.Parent is not TypeParameterConstraintClauseSyntax clause
            || token != clause.WhereKeyword
            || clause.Parent is not SyntaxNode declaration
            || !IsFirstConstraint(declaration, clause))
        {
            return;
        }

        AddOccurrence(declaration, SyntaxWrappingKind.ConstraintClauses);
    }

    static bool IsFirstConstraint(SyntaxNode declaration, TypeParameterConstraintClauseSyntax clause) =>
        declaration switch
            {
                TypeDeclarationSyntax { ConstraintClauses.Count: > 0 } item =>
                    item.ConstraintClauses[0] == clause,
                MethodDeclarationSyntax { ConstraintClauses.Count: > 0 } item =>
                    item.ConstraintClauses[0] == clause,
                LocalFunctionStatementSyntax { ConstraintClauses.Count: > 0 } item =>
                    item.ConstraintClauses[0] == clause,
                DelegateDeclarationSyntax { ConstraintClauses.Count: > 0 } item =>
                    item.ConstraintClauses[0] == clause,
                _ => false
            };

    void DiscoverQuery(SyntaxToken token)
    {
        if (Enabled(SyntaxWrappingKind.QueryClauses) is null
            || token.Parent is not FromClauseSyntax from
            || token != from.FromKeyword)
        {
            return;
        }

        for (var parent = from.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is QueryExpressionSyntax query)
            {
                if (query.FromClause == from)
                    AddOccurrence(query, SyntaxWrappingKind.QueryClauses);
                return;
            }

            if (parent is not (QueryClauseSyntax or QueryContinuationSyntax))
                return;
        }
    }

    void DiscoverMemberAccess(SyntaxToken token)
    {
        if (Enabled(SyntaxWrappingKind.MemberAccessChains) is not null
            && token.Parent is MemberAccessExpressionSyntax access
            && token == access.OperatorToken
            && !ContinuesChain(access)
            && !IsInsideArgument(access))
        {
            AddOccurrence(access, SyntaxWrappingKind.MemberAccessChains);
        }
    }

    /// <summary>
    /// Whether an access is a link in a longer chain, which owns its operator.
    /// </summary>
    /// <remarks>
    /// A chain's calls sit between its links, so <c>order.Lines.Where(...)</c> is held by the invocation that
    /// <c>.Sum</c> then reaches. Were it a chain of its own, it would decide the same breaks again after the whole
    /// chain had, from lines the whole chain had already broken, and undo them for everything measured after.
    /// </remarks>
    static bool ContinuesChain(SyntaxNode access)
    {
        var end = ChainEnd(access);
        return end.Parent is MemberAccessExpressionSyntax outer && outer.Expression == end
            || end.Parent is ConditionalAccessExpressionSyntax;
    }

    void DiscoverQuestion(SyntaxToken token)
    {
        if (Enabled(SyntaxWrappingKind.MemberAccessChains) is not null
            && token.Parent is ConditionalAccessExpressionSyntax access
            && token == access.OperatorToken
            && !ContinuesChain(access)
            && !IsInsideArgument(access))
        {
            AddOccurrence(access, SyntaxWrappingKind.MemberAccessChains);
        }

        if (Enabled(SyntaxWrappingKind.ConditionalExpressions) is not null
            && token.Parent is ConditionalExpressionSyntax conditional
            && token == conditional.QuestionToken
            && conditional.Parent is not ConditionalExpressionSyntax)
        {
            AddOccurrence(conditional, SyntaxWrappingKind.ConditionalExpressions);
        }
    }

    void DiscoverBinary(SyntaxToken token)
    {
        if (Enabled(SyntaxWrappingKind.BinaryExpressions) is null
            || !SyntaxWrappingTriggerMask.IsBinaryOperator((SyntaxKind)token.RawKind)
            || token.Parent is not BinaryExpressionSyntax binary
            || token != binary.OperatorToken
            || binary.Parent is BinaryExpressionSyntax parent
                && (Enabled(SyntaxWrappingKind.BinaryExpressions)?.Mode != SyntaxWrappingSettings.WrappingMode.Auto
                || parent.RawKind == binary.RawKind))
        {
            return;
        }

        AddOccurrence(binary, SyntaxWrappingKind.BinaryExpressions);
    }

    void AddOccurrence(SyntaxNode node, SyntaxWrappingKind kind)
    {
        if (Enabled(kind) is not { } setting || Unsafe(node))
            return;

        var start = _boundaries.Count;
        var firstToken = _stream.IndexOf(node.GetFirstToken(), _currentSegmentIndex);
        var lastToken = _stream.IndexOf(OccurrenceLastToken(node), _currentSegmentIndex);

        switch (node)
        {
            case ArgumentListSyntax list:
                Delimited(list.Arguments, list.CloseParenToken);
                break;
            case BracketedArgumentListSyntax list:
                Delimited(list.Arguments, list.CloseBracketToken);
                break;
            case ParameterListSyntax list:
                Delimited(list.Parameters, list.CloseParenToken);
                break;
            case BracketedParameterListSyntax list:
                Delimited(list.Parameters, list.CloseBracketToken);
                break;
            case InitializerExpressionSyntax initializer:
                AddBoundary(initializer.OpenBraceToken, GapStyle.InitializerOpen);
                Delimited(initializer.Expressions, initializer.CloseBraceToken, spacesInside: true);
                break;
            case AnonymousObjectCreationExpressionSyntax anonymous:
                AddBoundary(anonymous.OpenBraceToken, GapStyle.InitializerOpen);
                Delimited(anonymous.Initializers, anonymous.CloseBraceToken, spacesInside: true);
                break;
            case CollectionExpressionSyntax collection:
                Delimited(collection.Elements, collection.CloseBracketToken);
                break;
            case AttributeListSyntax attributes:
                Delimited(attributes.Attributes, attributes.CloseBracketToken);
                break;
            case BaseListSyntax baseList:
                AddBoundary(baseList.ColonToken, GapStyle.SeparatedFirst);
                for (var index = 0; index < baseList.Types.Count; index++)
                {
                    var right = baseList.Types[index].GetFirstToken();
                    AddBoundary(
                        right,
                        index == 0 || !right.GetPreviousToken().IsKind(SyntaxKind.CommaToken)
                            ? GapStyle.SeparatedFirst
                            : GapStyle.SeparatedLater,
                        breakWhenMulti: index != 0);
                }

                break;
            case TypeDeclarationSyntax declaration:
                Constraints(declaration.ConstraintClauses);
                break;
            case MethodDeclarationSyntax declaration:
                Constraints(declaration.ConstraintClauses);
                break;
            case LocalFunctionStatementSyntax declaration:
                Constraints(declaration.ConstraintClauses);
                break;
            case DelegateDeclarationSyntax declaration:
                Constraints(declaration.ConstraintClauses);
                break;
            case MemberAccessExpressionSyntax access:
                MemberAccess(access, firstToken, lastToken);
                break;
            case ConditionalAccessExpressionSyntax access:
                MemberAccess(access, firstToken, lastToken);
                break;
            case BinaryExpressionSyntax binary:
                Binary(binary, firstToken, lastToken);
                break;
            case ConditionalExpressionSyntax conditional:
                Conditional(conditional, firstToken, lastToken);
                break;
            case QueryExpressionSyntax query:
                QueryBody(query.Body);
                break;
        }

        _occurrences.Add(new(
            node,
            kind,
            setting,
            start,
            _boundaries.Count - start,
            -1,
            firstToken,
            lastToken));
    }

    void QueryBody(QueryBodySyntax body)
    {
        while (true)
        {
            foreach (var clause in body.Clauses)
                AddBoundary(clause.GetFirstToken(), GapStyle.Item);
            AddBoundary(body.SelectOrGroup.GetFirstToken(), GapStyle.Item);
            if (body.Continuation is not { } continuation)
                return;
            AddBoundary(continuation.IntoKeyword, GapStyle.Item);
            body = continuation.Body;
        }
    }

    /// <remarks>
    /// Members reached before the chain's first call are the receiver, as in <c>order.Lines.Where(...)</c>, so they
    /// stay with it; a chain that calls nothing is all receiver and breaks anywhere.
    /// </remarks>
    void MemberAccess(SyntaxNode occurrence, int firstToken, int lastToken)
    {
        var pieces = _stream.Pieces;
        var start = _boundaries.Count;
        var firstCall = -1;
        for (var index = firstToken; index <= lastToken; index++)
        {
            var token = pieces[index].Token;
            if (BelongsToOccurrence(token, occurrence)
                && (token.IsKind(SyntaxKind.QuestionToken)
                    && token.Parent is ConditionalAccessExpressionSyntax
                || (token.IsKind(SyntaxKind.DotToken)
                    && !token.GetPreviousToken().IsKind(SyntaxKind.QuestionToken))
                || token.IsKind(SyntaxKind.MinusGreaterThanToken)))
            {
                if (firstCall < 0 && CallsMember(token))
                    firstCall = _boundaries.Count;
                AddBoundary(index, GapStyle.CompactItem);
            }
        }

        for (var index = start; index < firstCall; index++)
            _boundaries[index] = _boundaries[index] with { BreakWhenMulti = false };
    }

    /// <summary>Whether the member after a chain's access operator is called.</summary>
    static bool CallsMember(SyntaxToken accessOperator)
    {
        var name = accessOperator.GetNextToken();
        if (accessOperator.IsKind(SyntaxKind.QuestionToken))
            name = name.GetNextToken();
        var member = name.Parent?.Parent;
        return member is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax
            && member.Parent is InvocationExpressionSyntax invocation
            && invocation.Expression == member;
    }

    /// <remarks>
    /// Only the links between a chain's operator and the chain itself make the operator the chain's. Anything else,
    /// such as an argument, a parenthesized or awaited receiver, or a type argument, holds an operator that belongs
    /// to a chain of its own or to none.
    /// </remarks>
    static bool BelongsToOccurrence(SyntaxToken token, SyntaxNode occurrence)
    {
        for (var node = token.Parent; node is not null && !ReferenceEquals(node, occurrence); node = node.Parent)
        {
            if (!IsChainLink(node))
                return false;
        }

        return true;
    }

    internal static bool IsChainLink(SyntaxNode node) =>
        node is MemberAccessExpressionSyntax
            or InvocationExpressionSyntax
            or ElementAccessExpressionSyntax
            or ConditionalAccessExpressionSyntax
            or MemberBindingExpressionSyntax
            or ElementBindingExpressionSyntax
        || node.IsKind(SyntaxKind.SuppressNullableWarningExpression);

    static bool IsInsideArgument(SyntaxNode node) =>
        node.Ancestors().Any(parent => parent is ArgumentSyntax or AttributeArgumentSyntax);

    void Binary(BinaryExpressionSyntax root, int firstToken, int lastToken)
    {
        var pieces = _stream.Pieces;
        for (var index = firstToken; index <= lastToken; index++)
        {
            var token = pieces[index].Token;
            if (token.Parent is BinaryExpressionSyntax expression
                && token == expression.OperatorToken
                && (Enabled(SyntaxWrappingKind.BinaryExpressions)?.Mode != SyntaxWrappingSettings.WrappingMode.Auto
                || BelongsToBinary(expression, root)))
            {
                OperatorBoundary(index, SyntaxWrappingKind.BinaryExpressions);
            }
        }
    }

    void Conditional(ConditionalExpressionSyntax root, int firstToken, int lastToken)
    {
        var pieces = _stream.Pieces;
        for (var index = firstToken; index <= lastToken; index++)
        {
            var token = pieces[index].Token;
            if (token.Parent is ConditionalExpressionSyntax expression
                && BelongsToConditional(expression, root)
                && (token == expression.QuestionToken || token == expression.ColonToken))
            {
                OperatorBoundary(index, SyntaxWrappingKind.ConditionalExpressions);
            }
        }
    }

    static SyntaxToken OccurrenceLastToken(SyntaxNode node) => node switch
        {
            TypeDeclarationSyntax { ConstraintClauses.Count: > 0 } declaration =>
                declaration.ConstraintClauses[^1].GetLastToken(),
            MethodDeclarationSyntax { ConstraintClauses.Count: > 0 } declaration =>
                declaration.ConstraintClauses[^1].GetLastToken(),
            LocalFunctionStatementSyntax { ConstraintClauses.Count: > 0 } declaration =>
                declaration.ConstraintClauses[^1].GetLastToken(),
            DelegateDeclarationSyntax { ConstraintClauses.Count: > 0 } declaration =>
                declaration.ConstraintClauses[^1].GetLastToken(),
            MemberAccessExpressionSyntax or ConditionalAccessExpressionSyntax => ChainEnd(node).GetLastToken(),
            _ => node.GetLastToken()
        };

    /// <summary>
    /// The call or indexer a chain ends with, whose arguments share the line of the chain's last member.
    /// </summary>
    /// <remarks>
    /// The chain's own node stops at its last member name, but the arguments after that name are on the same line
    /// until something breaks it. A chain that measured only up to the name would find room its line does not
    /// have, and leave the break to the arguments.
    /// </remarks>
    internal static SyntaxNode ChainEnd(SyntaxNode chain)
    {
        while (chain.Parent is InvocationExpressionSyntax invocation && invocation.Expression == chain
            || chain.Parent is ElementAccessExpressionSyntax access && access.Expression == chain
            || chain.Parent is PostfixUnaryExpressionSyntax suppression
                && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            chain = chain.Parent;
        }
        return chain;
    }

    void Delimited<T>(SeparatedSyntaxList<T> items, SyntaxToken close, bool spacesInside = false) where T : SyntaxNode
    {
        for (var index = 0; index < items.Count; index++)
        {
            AddBoundary(
                items[index].GetFirstToken(),
                index == 0
                    ? spacesInside
                        ? GapStyle.DelimitedSpacedFirst
                        : GapStyle.DelimitedFirst
                    : GapStyle.DelimitedLater);
        }
        AddBoundary(close, spacesInside ? GapStyle.DelimitedSpacedClose : GapStyle.DelimitedClose);
    }

    void Constraints<T>(SyntaxList<T> clauses) where T : SyntaxNode
    {
        foreach (var clause in clauses)
            AddBoundary(clause.GetFirstToken(), GapStyle.Item);
    }

    void AddBoundary(SyntaxToken right, GapStyle style, bool breakWhenMulti = true) =>
        _boundaries.Add(new(_stream.IndexOf(right, _currentSegmentIndex), style, BreakWhenMulti: breakWhenMulti));

    void AddBoundary(int rightIndex, GapStyle style) =>
        _boundaries.Add(new(rightIndex, style));

    void SortAndLinkOccurrences()
    {
        if (_occurrences.Count < 2)
            return;

        _occurrences.Sort(static (left, right) =>
            {
                var first = left.FirstToken.CompareTo(right.FirstToken);
                return first != 0
                    ? first
                    : right.LastToken.CompareTo(left.LastToken);
            });

        var ancestors = new List<int>();
        for (var index = 0; index < _occurrences.Count; index++)
        {
            var occurrence = _occurrences[index];
            while (ancestors.Count != 0)
            {
                var candidate = _occurrences[ancestors[^1]];
                if (ReferenceEquals(candidate.Node.SyntaxTree, occurrence.Node.SyntaxTree)
                    && candidate.FirstToken <= occurrence.FirstToken
                    && occurrence.LastToken <= candidate.LastToken
                    && (candidate.FirstToken != occurrence.FirstToken
                    || candidate.LastToken != occurrence.LastToken))
                {
                    break;
                }
                ancestors.RemoveAt(ancestors.Count - 1);
            }

            var parent = ancestors.Count == 0 ? -1 : ancestors[^1];
            _occurrences[index] = occurrence with { Parent = parent };
            ancestors.Add(index);
        }
    }

    static bool BelongsToBinary(BinaryExpressionSyntax expression, BinaryExpressionSyntax root)
    {
        while (expression != root)
        {
            if (expression.RawKind != root.RawKind || expression.Parent is not BinaryExpressionSyntax parent)
                return false;
            expression = parent;
        }
        return true;
    }

    static bool BelongsToConditional(ConditionalExpressionSyntax expression, ConditionalExpressionSyntax root)
    {
        for (SyntaxNode? node = expression; node is ConditionalExpressionSyntax; node = node.Parent)
        {
            if (node == root)
                return true;
        }
        return false;
    }

    void OperatorBoundary(int index, SyntaxWrappingKind kind)
    {
        var setting = Enabled(kind)!.Value;
        if (!setting.ShapesOperators)
        {
            AddBoundary(index, GapStyle.Item);
            return;
        }
        var trailing = setting.OperatorPlacement == "end_of_line";
        _boundaries.Add(new(index, GapStyle.Item, index, !trailing));
        _boundaries.Add(new(index + 1, GapStyle.Item, index, trailing));
    }

    SyntaxWrappingSettings.Setting? Enabled(SyntaxWrappingKind kind) => _byKind[(int)kind];

    bool Unsafe(SyntaxNode node) => node.ContainsDirectives
        || ReferenceEquals(node.SyntaxTree, _root.SyntaxTree)
            && _safetyContext?.IsUnsafe(node) == true;

    internal sealed record Result(
        Occurrence[] Occurrences,
        Boundary[] Boundaries,
        int SkippedOccurrences);
}
