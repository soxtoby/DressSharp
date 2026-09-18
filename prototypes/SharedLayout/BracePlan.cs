using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

// PROTOTYPE: default balanced braces as edits over resolved token positions.
sealed class BracePlan(ResolvedTokenLayout layout, EffectiveTokenStream stream, RuleContext context)
{
    readonly List<Insertion> _insertions = [];
    readonly HashSet<SyntaxNode> _visited = [];
    readonly HashSet<StatementSyntax> _bodies = [];
    readonly HashSet<StatementSyntax> _multilineBodies = [];
    internal int Count => _insertions.Count;
    internal int BodyStartIndentChanges { get; private set; }

    internal void Solve()
    {
        for (var index = 0; index < layout.Pieces.Length; index++)
        {
            var piece = layout.Pieces[index];
            var node = piece.Token.Parent;
            if (node is null || Body(node) is null || !_visited.Add(node)) continue;
            if (node.AncestorsAndSelf().Any(parent => Body(parent) is not null && !SyntaxRuleSafety.CanRewriteWithoutCounting(parent, context))) continue;
            if (node is IfStatementSyntax conditional)
            {
                if (conditional.Parent is ElseClauseSyntax) continue;
                var branches = Chain(conditional).ToArray();
                if (branches.Any(branch => Required(branch, piece.SegmentIndex)))
                    foreach (var branch in branches) Add(branch, piece.SegmentIndex);
            }
            else if (node is not ElseClauseSyntax && Required(node, piece.SegmentIndex)) Add(node, piece.SegmentIndex);
        }
    }

    bool Required(SyntaxNode owner, int segment)
    {
        var body = Body(owner)!;
        if (body is BlockSyntax { Statements.Count: > 1 }) return true;
        var measured = body is BlockSyntax { Statements: [var only] } ? only : body;
        var bodyMulti = body is not BlockSyntax { Statements.Count: 0 }
            && layout.IsMultiline(Index(measured.GetFirstToken(), segment), Index(measured.GetLastToken(), segment));
        var header = Header(owner);
        return bodyMulti || header is { } tokens && layout.IsMultiline(Index(tokens.First, segment), Index(tokens.Last, segment));
    }

    void Add(SyntaxNode owner, int segment)
    {
        var body = Body(owner)!;
        if (body is BlockSyntax) return;
        var first = Index(body.GetFirstToken(), segment);
        var last = Index(body.GetLastToken(), segment);
        var ownerIndex = Index(owner.GetFirstToken(), segment);
        var indent = layout.LeadingIndent(ownerIndex);
        if (layout.LeadingIndent(first) != indent + context.IndentUnit) BodyStartIndentChanges++;
        _bodies.Add(body);
        if (layout.IsMultiline(first, last)) _multilineBodies.Add(body);
        _insertions.Add(new(first, last, indent));
    }

    internal SyntaxNode RewriteMember(SyntaxNode member) => new ApplyEdits(_bodies, _multilineBodies, context.LineEnding).Visit(member)!;

    sealed class ApplyEdits(HashSet<StatementSyntax> bodies, HashSet<StatementSyntax> multiline, string lineEnding) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            var current = base.Visit(node);
            if (node is not StatementSyntax statement || !bodies.Contains(statement)) return current;
            var rewritten = (StatementSyntax)current!;
            var leading = rewritten.GetLeadingTrivia();
            var trailing = rewritten.GetTrailingTrivia();
            var inner = rewritten.WithoutLeadingTrivia().WithoutTrailingTrivia();
            var block = GeneratedSyntax.Mark(SyntaxFactory.Block(inner));
            if (multiline.Contains(statement))
            {
                var ending = SyntaxFactory.EndOfLine(lineEnding);
                var indentation = leading.Reverse().TakeWhile(trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse();
                block = block.WithOpenBraceToken(block.OpenBraceToken.WithTrailingTrivia(ending))
                    .WithStatements(SyntaxFactory.SingletonList(inner.WithLeadingTrivia(indentation).WithTrailingTrivia(ending)));
            }
            else
                block = block.WithOpenBraceToken(block.OpenBraceToken.WithTrailingTrivia(SyntaxFactory.Space))
                    .WithStatements(SyntaxFactory.SingletonList(inner.WithTrailingTrivia(SyntaxFactory.Space)));
            return block.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
        }
    }

    // Deliberately incomplete control arm: adding delimiters without dependent replanning.
    internal string RenderOverlay()
    {
        var edits = new List<Edit>();
        foreach (var insertion in _insertions)
        {
            var before = layout.Extent(insertion.First - 1).End;
            var start = layout.Extent(insertion.First).Start;
            var end = layout.Extent(insertion.Last).End;
            edits.Add(new(before, start, context.LineEnding + insertion.Indent + "{" + layout.Slice(before, start), insertion.First));
            edits.Add(new(end, end, context.LineEnding + insertion.Indent + "}", -insertion.First));
        }
        var output = new System.Text.StringBuilder();
        var position = 0;
        foreach (var edit in edits.OrderBy(edit => edit.Start).ThenBy(edit => edit.Order))
        {
            if (edit.Start < position) throw new InvalidOperationException("Overlapping brace edits");
            output.Append(layout.Slice(position, edit.Start));
            output.Append(edit.Text);
            position = edit.End;
        }
        output.Append(layout.Slice(position, layout.Length));
        return output.ToString();
    }

    int Index(SyntaxToken token, int segment) => stream.IndexOf(token, segment);
    static IEnumerable<SyntaxNode> Chain(IfStatementSyntax conditional)
    {
        while (true)
        {
            yield return conditional;
            if (conditional.Else is not { } alternative) yield break;
            if (alternative.Statement is IfStatementSyntax nested) conditional = nested;
            else { yield return alternative; yield break; }
        }
    }

    static StatementSyntax? Body(SyntaxNode owner) => owner switch
    {
        IfStatementSyntax node => node.Statement,
        ElseClauseSyntax node => node.Statement,
        WhileStatementSyntax node => node.Statement,
        DoStatementSyntax node => node.Statement,
        ForStatementSyntax node => node.Statement,
        CommonForEachStatementSyntax node => node.Statement,
        UsingStatementSyntax node => node.Statement,
        LockStatementSyntax node => node.Statement,
        FixedStatementSyntax node => node.Statement,
        _ => null
    };

    static (SyntaxToken First, SyntaxToken Last)? Header(SyntaxNode owner) => owner switch
    {
        IfStatementSyntax node => (node.IfKeyword, node.CloseParenToken),
        WhileStatementSyntax node => (node.WhileKeyword, node.CloseParenToken),
        DoStatementSyntax node => (node.WhileKeyword, node.CloseParenToken),
        ForStatementSyntax node => (node.ForKeyword, node.CloseParenToken),
        CommonForEachStatementSyntax node => (node.ForEachKeyword, node.CloseParenToken),
        UsingStatementSyntax node => (node.UsingKeyword, node.CloseParenToken),
        LockStatementSyntax node => (node.LockKeyword, node.CloseParenToken),
        FixedStatementSyntax node => (node.FixedKeyword, node.CloseParenToken),
        _ => null
    };

    sealed record Insertion(int First, int Last, string Indent);
    sealed record Edit(int Start, int End, string Text, int Order);
}
