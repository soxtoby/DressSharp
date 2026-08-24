using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

/// <summary>
/// Writes a formatted file in one walk, deciding whitespace as it goes rather than rewriting the
/// tree once per rule.
/// </summary>
/// <remarks>
/// Members a syntax rule wanted are handed over already rewritten, and are written from their own
/// text; everything else is written from the original source. So a file where no syntax rule applies
/// is never copied at all, and one where a rule applies pays only for the members it touched.
/// The emitter carries line-break, indentation, spacing, blank-line, comment, and syntax-wrapping
/// decisions.
/// </remarks>
sealed class SinglePassEmitter
{
    readonly EmitterPlan _plan;
    readonly SyntaxWrappingPlan _syntaxWrapping;
    readonly TriviaLayoutPlan _triviaLayout;
    readonly RuleContext _context;
    readonly SyntaxNode _root;
    readonly bool _checkMalformedRegions;
    readonly StringBuilder _output;
    readonly EffectiveTokenStream.Piece[] _pieces;

    int _column;
    string _lineIndent = "";
    bool _previousGapHadMeaningfulTrivia;
    SwitchSectionSyntax? _lastCaseBlockSection;
    bool _lastCaseBlockSectionIsSafe;
    SyntaxNode? _lastPreservationContainer;
    bool _lastPreservationContainerIsSafe;

    /// <summary>
    /// The indentation for content at each open brace, innermost last. Nesting alone decides it,
    /// so no lookup of where an owning syntax node started is needed.
    /// </summary>
    readonly List<string> _contentIndents = [""];
    readonly Stack<string> _braceIndents = new();
    readonly Stack<InitializerFrame> _initializerFrames = new();

    readonly record struct InitializerFrame(SyntaxNode Initializer, string Indent);

    SinglePassEmitter(
        EmitterPlan plan,
        EmissionLayoutPlan layout,
        RuleContext context,
        SyntaxNode root,
        bool checkMalformedRegions,
        int capacity)
    {
        _plan = plan;
        _syntaxWrapping = layout.Wrapping;
        _triviaLayout = layout.Trivia;
        _context = context;
        _root = root;
        _checkMalformedRegions = checkMalformedRegions;
        _pieces = layout.Stream.Pieces;
        _output = new StringBuilder(capacity);
    }

    internal static string Emit(
        SyntaxNode root,
        EmitterPlan plan,
        RuleContext context,
        string source,
        EmissionLayoutPlan layout) =>
        new SinglePassEmitter(
            plan,
            layout,
            context,
            root,
            root.ContainsDiagnostics,
            source.Length).Run();

    string Run()
    {
        var pair = new TokenPair();
        for (var index = 0; index < _pieces.Length; index++)
        {
            var piece = _pieces[index];
            if (index == 0)
            {
                if (_triviaLayout.HasLeadingEdit(index))
                    AppendTrivia(_triviaLayout.Leading(index));
                else
                    Copy(piece.Source, piece.Token.FullSpan.Start, piece.Token.SpanStart);
            }
            else
            {
                EnterInitializer(piece.Token);
                EmitGap(pair, index);
            }

            EnterOrLeave(piece.Token);
            Append(piece.Token.Text);
        }

        if (_pieces.Length > 0)
        {
            var last = _pieces[^1];
            if (_triviaLayout.HasTrailingEdit(_pieces.Length - 1))
                AppendTrivia(_triviaLayout.Trailing(_pieces.Length - 1));
            else
                Copy(last.Source, last.Token.Span.End, last.Token.FullSpan.End);
        }

        return _output.ToString();
    }

    void EnterInitializer(SyntaxToken token)
    {
        if (InitializerIndentationRule.FindDelimiter(token) is not { IsOpening: true } delimiter
            || _plan.InitializerIndentation(delimiter.Kind) is not { } indented
            || delimiter.Initializer.ContainsDirectives
            || _checkMalformedRegions && IsUnsafeOriginal(delimiter.Initializer))
        {
            return;
        }

        _initializerFrames.Push(new(
            delimiter.Initializer,
            _lineIndent + (indented ? _plan.IndentUnit : "")));
    }

    /// <summary>
    /// Writes whatever belongs between the previous token and this one.
    /// </summary>
    void EmitGap(TokenPair pair, int index)
    {
        var leftPiece = _pieces[index - 1];
        var rightPiece = _pieces[index];
        var left = leftPiece.Token;
        var right = rightPiece.Token;
        var boundaryBeforeLeft = _previousGapHadMeaningfulTrivia;
        _previousGapHadMeaningfulTrivia = false;

        // Inside an interpolated string the characters between tokens are content, not trivia, and
        // the braces that open a hole are not the braces the layout rules mean. Editing either
        // rewrites the string.
        if (InsideInterpolatedString(left) || InsideInterpolatedString(right))
        {
            CopyGap(index);
            return;
        }

        // Comments, directives and disabled text have to survive, and where they sit decides the
        // line structure around them, so a gap holding any of them is copied rather than decided.
        if (_triviaLayout.HasMeaningfulGap(index))
        {
            _previousGapHadMeaningfulTrivia = true;
            if (_syntaxWrapping.GapBefore(index) is { } wrappingGap)
                EmitWrappingGap(wrappingGap, right);
            else
                CopyGap(index);
            return;
        }

        // New-line rules follow syntax wrapping in catalog order, so they get the last word on a
        // boundary both rules own.
        if (ClaimsBreak(right) is { } wantsBreak)
        {
            if (wantsBreak)
                StartLine(right);
            else
                Append(" ");
            return;
        }

        if (_syntaxWrapping.GapBefore(index) is { } gap)
        {
            EmitWrappingGap(gap, right);
            return;
        }

        var preservationBreaks = _plan.ExpandSingleLineBlocks || _plan.SeparateSingleLineStatements
            ? PreservationBreaks(left, right)
            : 0;
        if (preservationBreaks != 0)
        {
            for (var count = 0; count < preservationBreaks; count++)
                _output.Append(_context.LineEnding);
            WriteIndent(right);
            return;
        }

        if (PreservesSourceIndent(right) && _triviaLayout.HasLineBreak(index))
        {
            CopyGap(index);
            return;
        }

        // Indentation only restates the whitespace after a line break; where the source kept two
        // tokens on one line, no indentation rule has an opinion about them.
        if (EmitUpToLastLineBreak(
                index,
                leftPiece.Source,
                left,
                rightPiece.Source,
                right))
        {
            WriteIndent(right);
            return;
        }

        // No rule owns this gap, so it keeps whatever it had. The catalog says nothing about the
        // space between a keyword and an identifier, and deciding one here would rewrite every file
        // over whitespace nobody asked about.
        pair.Reset(left, right);
        if (DesiredSpace(
                pair,
                boundaryBeforeLeft,
                leftPiece.IsOriginal && rightPiece.IsOriginal) is not { } desired)
        {
            CopyGap(index);
            return;
        }

        if (desired)
            Append(" ");
    }

    void EmitWrappingGap(string gap, SyntaxToken right)
    {
        var lastBreak = gap.LastIndexOfAny(LineBreaks);
        if (lastBreak >= 0 && InitializerIndentFor(right) is not null)
        {
            var indentStart = gap.Length;
            while (indentStart > lastBreak + 1 && gap[indentStart - 1] is ' ' or '\t')
                indentStart--;
            Append(gap[..indentStart]);
            WriteIndent(right);
            return;
        }

        Append(gap);
    }

    /// <summary>
    /// Writes the gap up to and including its last line break, and reports whether it had one. What
    /// follows that break is indentation the caller is about to restate.
    /// </summary>
    /// <remarks>
    /// A gap spans the previous token's trailing trivia and this one's leading trivia, which sit in
    /// different texts wherever a rewritten member begins or ends. A blank line puts a break in each,
    /// so the trailing part still has to be written when the last break is in the leading part.
    /// </remarks>
    bool EmitUpToLastLineBreak(
        int rightIndex,
        string leftSource,
        SyntaxToken left,
        string rightSource,
        SyntaxToken right)
    {
        if (_triviaLayout.HasGapEdit(rightIndex))
        {
            var leading = _triviaLayout.Leading(rightIndex).ToFullString();
            var inLeadingEdit = leading.LastIndexOfAny(LineBreaks);
            if (inLeadingEdit >= 0)
            {
                AppendTrivia(_triviaLayout.Trailing(rightIndex - 1));
                Append(leading[..(inLeadingEdit + 1)]);
                return true;
            }

            var trailing = _triviaLayout.Trailing(rightIndex - 1).ToFullString();
            var inTrailingEdit = trailing.LastIndexOfAny(LineBreaks);
            if (inTrailingEdit < 0)
                return false;
            Append(trailing[..(inTrailingEdit + 1)]);
            return true;
        }

        var trailingStart = left.Span.End;
        var trailingEnd = left.FullSpan.End;
        var leadingStart = right.FullSpan.Start;
        var leadingEnd = right.SpanStart;

        var inLeading = rightSource.AsSpan(leadingStart, leadingEnd - leadingStart).LastIndexOfAny(LineBreaks);
        if (inLeading >= 0)
        {
            Copy(leftSource, trailingStart, trailingEnd);
            Copy(rightSource, leadingStart, leadingStart + inLeading + 1);
            return true;
        }

        var inTrailing = leftSource.AsSpan(trailingStart, trailingEnd - trailingStart).LastIndexOfAny(LineBreaks);
        if (inTrailing < 0)
            return false;

        Copy(leftSource, trailingStart, trailingStart + inTrailing + 1);
        return true;
    }

    void CopyGap(int index)
    {
        if (_triviaLayout.HasGapEdit(index))
        {
            AppendTrivia(_triviaLayout.Trailing(index - 1));
            AppendTrivia(_triviaLayout.Leading(index));
            return;
        }

        var left = _pieces[index - 1];
        var right = _pieces[index];
        Copy(left.Source, left.Token.Span.End, left.Token.FullSpan.End);
        Copy(right.Source, right.Token.FullSpan.Start, right.Token.SpanStart);
    }

    bool? ClaimsBreak(SyntaxToken token)
    {
        var candidates = _plan.NewLineTrigger(token.RawKind);
        if (candidates == 0 || _checkMalformedRegions && IsUnsafeOriginal(token))
            return null;
        var laterElement = (candidates & _plan.LaterElementRules) != 0
            ? NewLineRule.StartsLaterElement(token)
            : null;
        var rules = _plan.NewLines;
        for (var index = 0; index < rules.Length; index++)
        {
            if ((candidates & (1UL << index)) != 0
                && rules[index].Rule.ClaimsBreakBefore(token, rules[index].Categories, laterElement) is { } claim)
                return claim;
        }

        return null;
    }

    void StartLine(SyntaxToken token)
    {
        _output.Append(_plan.LineEnding);
        WriteIndent(token);
    }

    void WriteIndent(SyntaxToken token)
    {
        _lineIndent = IndentFor(token);
        _output.Append(_lineIndent);
        _column = _lineIndent.Length;
    }

    string IndentFor(SyntaxToken token)
    {
        if (InitializerIndentFor(token) is { } initializerIndent)
            return initializerIndent;

        if (_plan.LabelIndentation is { } labelIndentation
            && LabelFor(token) is { } labeledStatement
            && (!_checkMalformedRegions || !IsUnsafeOriginal(labeledStatement)))
        {
            return labelIndentation switch
            {
                LabelIndentationStyle.FlushLeft => "",
                LabelIndentationStyle.OneLessThanCurrent => RemoveIndentUnit(_contentIndents[^1]),
                _ => _contentIndents[^1]
            };
        }

        if (_plan.IndentSwitchLabels is { } indentSwitchLabels
            && token.Parent is SwitchLabelSyntax label
            && token == label.GetFirstToken()
            && (!_checkMalformedRegions || !IsUnsafeOriginal(label)))
        {
            var switchIndent = _braceIndents.Count != 0 ? _braceIndents.Peek() : "";
            return switchIndent + (indentSwitchLabels ? _plan.IndentUnit : "");
        }

        if (CaseBlockIndent(token) is { } caseBlockIndent)
            return caseBlockIndent;

        if (_plan.IndentCaseContents is { } indentCaseContents
            && DirectSwitchSectionStatement(token) is { } section
            && (!_checkMalformedRegions || !IsUnsafeOriginal(section)))
        {
            var labelIndent = SwitchLabelIndent();
            return labelIndent + (indentCaseContents ? _plan.IndentUnit : "");
        }

        return token.IsKind(SyntaxKind.CloseBraceToken) && _braceIndents.Count != 0
            ? _braceIndents.Peek()
            : _contentIndents[^1];
    }

    string SwitchLabelIndent()
    {
        if (_plan.IndentSwitchLabels is not { } indentSwitchLabels)
            return _contentIndents[^1];

        var switchIndent = _braceIndents.Count != 0 ? _braceIndents.Peek() : "";
        return SwitchLabelIndentFrom(switchIndent, indentSwitchLabels);
    }

    string SwitchLabelIndentFrom(string switchIndent, bool indentSwitchLabels) =>
        switchIndent + (indentSwitchLabels ? _plan.IndentUnit : "");

    static SwitchSectionSyntax? DirectSwitchSectionStatement(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is not StatementSyntax statement)
                continue;
            return statement.Parent is SwitchSectionSyntax section && token == statement.GetFirstToken()
                ? section
                : null;
        }

        return null;
    }

    string? CaseBlockIndent(SyntaxToken token)
    {
        if (_plan.IndentCaseContentsWhenBlock is not { } indentCaseContentsWhenBlock
            || DirectSwitchSectionBlock(token) is not { } section
            || !CaseBlockSectionIsSafe(section))
        {
            return null;
        }

        var switchIndent = token.IsKind(SyntaxKind.CloseBraceToken) && _braceIndents.Count > 1
            ? _braceIndents.ElementAt(1)
            : _braceIndents.Count != 0 ? _braceIndents.Peek() : "";
        var labelIndent = _plan.IndentSwitchLabels is { } indentSwitchLabels
            ? SwitchLabelIndentFrom(switchIndent, indentSwitchLabels)
            : token.IsKind(SyntaxKind.CloseBraceToken) && _contentIndents.Count > 1
                ? _contentIndents[^2]
                : _contentIndents[^1];
        return labelIndent + (indentCaseContentsWhenBlock ? _plan.IndentUnit : "");
    }

    bool CaseBlockSectionIsSafe(SwitchSectionSyntax section)
    {
        if (ReferenceEquals(_lastCaseBlockSection, section))
            return _lastCaseBlockSectionIsSafe;

        _lastCaseBlockSection = section;
        _lastCaseBlockSectionIsSafe = !_checkMalformedRegions || !IsUnsafeOriginal(section);
        return _lastCaseBlockSectionIsSafe;
    }

    int PreservationBreaks(SyntaxToken left, SyntaxToken right)
    {
        if (_plan.ExpandSingleLineBlocks
            && SingleLineBraceOwner(left, opening: true) is { } leftContainer
            && IsSafeSingleLine(leftContainer))
        {
            return 1;
        }

        if (_plan.ExpandSingleLineBlocks
            && SingleLineBraceOwner(right, opening: false) is { } rightContainer
            && IsSafeSingleLine(rightContainer))
        {
            return 1;
        }

        if (_plan.SeparateSingleLineStatements
            && FirstStatementAt(right) is { Parent: { } statementParent and (BlockSyntax or SwitchSectionSyntax) } statement
            && PreviousStatementIn(statementParent, statement) is { } previous
            && SharesLine(previous, statement)
            && IsSafePreservationContainer(statementParent))
        {
            return 1;
        }

        if (_plan.SeparateSingleLineStatements
            && FirstMemberAt(right) is { Parent: { } parent } member
            && PreviousMemberIn(parent, member) is { } previousMember
            && SharesLine(previousMember, member)
            && CanRewritePreservingTrivia(previousMember)
            && CanRewritePreservingTrivia(member))
        {
            return 1;
        }

        return 0;
    }

    static SyntaxNode? SingleLineBraceOwner(SyntaxToken token, bool opening) => token.Parent switch
    {
        BlockSyntax block when token == (opening ? block.OpenBraceToken : block.CloseBraceToken) => block,
        AccessorListSyntax accessors when token == (opening ? accessors.OpenBraceToken : accessors.CloseBraceToken) => accessors,
        _ => null
    };

    bool IsSafeSingleLine(SyntaxNode container)
    {
        if (!IsSingleLine(container))
            return false;
        return IsSafePreservationContainer(container);
    }

    bool IsSafePreservationContainer(SyntaxNode container)
    {
        if (ReferenceEquals(_lastPreservationContainer, container))
            return _lastPreservationContainerIsSafe;

        _lastPreservationContainer = container;
        _lastPreservationContainerIsSafe = CanRewritePreservingTrivia(container);
        return _lastPreservationContainerIsSafe;
    }

    static StatementSyntax? PreviousStatementIn(SyntaxNode parent, StatementSyntax statement)
    {
        for (var node = statement.GetFirstToken().GetPreviousToken().Parent; node is not null; node = node.Parent)
        {
            if (node is StatementSyntax previous && previous.Parent == parent)
                return previous;
            if (node == parent)
                return null;
        }

        return null;
    }

    static MemberDeclarationSyntax? PreviousMemberIn(SyntaxNode parent, MemberDeclarationSyntax member)
    {
        for (var node = member.GetFirstToken().GetPreviousToken().Parent; node is not null; node = node.Parent)
        {
            if (node is MemberDeclarationSyntax previous && previous.Parent == parent)
                return previous;
            if (node == parent)
                return null;
        }

        return null;
    }

    static bool SharesLine(SyntaxNode left, SyntaxNode right) =>
        left.GetLocation().GetLineSpan().EndLinePosition.Line
        == right.GetLocation().GetLineSpan().StartLinePosition.Line;

    static bool IsSingleLine(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line
        == node.GetLocation().GetLineSpan().EndLinePosition.Line;

    static StatementSyntax? FirstStatementAt(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is StatementSyntax statement)
                return token == statement.GetFirstToken() ? statement : null;
            if (node is MemberDeclarationSyntax)
                return null;
        }

        return null;
    }

    static MemberDeclarationSyntax? FirstMemberAt(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is MemberDeclarationSyntax member)
                return token == member.GetFirstToken() ? member : null;
        }

        return null;
    }

    static SwitchSectionSyntax? DirectSwitchSectionBlock(SyntaxToken token) =>
        token.Parent is BlockSyntax { Parent: SwitchSectionSyntax section } block
        && (token == block.OpenBraceToken || token == block.CloseBraceToken)
            ? section
            : null;

    bool PreservesSourceIndent(SyntaxToken token)
    {
        if (InitializerIndentFor(token) is not null)
            return false;

        if (_plan.LabelIndentation is not null && LabelFor(token) is { } label)
        {
            return _plan.LabelIndentation == LabelIndentationStyle.NoChange
                || _checkMalformedRegions && IsUnsafeOriginal(label);
        }

        if (_plan.IndentSwitchLabels is not null
            && token.Parent is SwitchLabelSyntax switchLabel
            && token == switchLabel.GetFirstToken())
        {
            return _checkMalformedRegions && IsUnsafeOriginal(switchLabel);
        }

        if (_plan.IndentCaseContentsWhenBlock is not null
            && DirectSwitchSectionBlock(token) is { } blockSection)
        {
            return !CaseBlockSectionIsSafe(blockSection);
        }

        if (_plan.IndentCaseContents is not null
            && DirectSwitchSectionStatement(token) is { } statementSection)
        {
            return _checkMalformedRegions && IsUnsafeOriginal(statementSection);
        }

        if (_plan.IndentBraces is not null
            && (token.IsKind(SyntaxKind.OpenBraceToken) || token.IsKind(SyntaxKind.CloseBraceToken)))
        {
            return false;
        }

        return _plan.IndentBlockContents is null
            || token.IsKind(SyntaxKind.CloseBraceToken)
            || _contentIndents.Count == 1;
    }

    static LabeledStatementSyntax? LabelFor(SyntaxToken token) =>
        token.Parent is LabeledStatementSyntax label && token == label.Identifier
            ? label
            : null;

    string RemoveIndentUnit(string indent) =>
        indent.EndsWith(_plan.IndentUnit, StringComparison.Ordinal)
            ? indent[..^_plan.IndentUnit.Length]
            : "";

    /// <summary>
    /// Tracks the indentation a brace opens and closes.
    /// </summary>
    /// <remarks>
    /// A brace is indented from the content around it and its own contents from the brace, which is
    /// what <c>csharp_indent_braces</c> and <c>csharp_indent_block_contents</c> each select.
    /// </remarks>
    void EnterOrLeave(SyntaxToken token)
    {
        if (InitializerIndentationRule.FindDelimiter(token) is { IsOpening: false } delimiter
            && _initializerFrames.TryPeek(out var frame)
            && ReferenceEquals(frame.Initializer, delimiter.Initializer))
        {
            _initializerFrames.Pop();
        }

        if (token.IsKind(SyntaxKind.OpenBraceToken))
        {
            var braceIndent = CaseBlockIndent(token)
                ?? (_plan.IndentBraces is { } indentBraces
                    ? _contentIndents[^1] + (indentBraces ? _plan.IndentUnit : "")
                    : _lineIndent);
            _braceIndents.Push(braceIndent);
            _contentIndents.Add(_plan.IndentBlockContents is { } indentBlockContents
                ? braceIndent + (indentBlockContents ? _plan.IndentUnit : "")
                : braceIndent);
        }
        else if (token.IsKind(SyntaxKind.CloseBraceToken) && _contentIndents.Count > 1)
        {
            _contentIndents.RemoveAt(_contentIndents.Count - 1);
            _braceIndents.Pop();
        }
    }

    string? InitializerIndentFor(SyntaxToken token)
    {
        if (_initializerFrames.TryPeek(out var frame)
            && InitializerIndentationRule.FindDelimiter(token) is { } delimiter
            && ReferenceEquals(frame.Initializer, delimiter.Initializer))
        {
            return frame.Indent;
        }

        return null;
    }

    bool? DesiredSpace(TokenPair pair, bool boundaryBeforeLeft, bool originalOccurrence)
    {
        if ((_plan.Trigger(pair.Left.RawKind) | _plan.Trigger(pair.Right.RawKind)) == 0
            || boundaryBeforeLeft
            || originalOccurrence
            && _checkMalformedRegions
            && _context.IsUnsafe(TextSpan.FromBounds(pair.Left.FullSpan.Start, pair.Right.FullSpan.End)))
        {
            return null;
        }

        return _plan.DesiredSpace(pair.Left, pair.Right);
    }

    bool IsUnsafeOriginal(SyntaxNode node) =>
        IsFromOriginalRoot(node) && _context.IsUnsafe(node);

    bool IsUnsafeOriginal(SyntaxToken token) =>
        token.Parent is { } parent && IsFromOriginalRoot(parent) && _context.IsUnsafe(token);

    bool IsFromOriginalRoot(SyntaxNode node)
    {
        while (node.Parent is { } parent)
            node = parent;
        return ReferenceEquals(node, _root);
    }

    bool CanRewritePreservingTrivia(SyntaxNode node) =>
        !IsUnsafeOriginal(node)
        && node.DescendantTrivia(descendIntoTrivia: true).None(trivia =>
            trivia.IsDirective
            || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

    static bool InsideInterpolatedString(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case InterpolatedStringExpressionSyntax:
                    return true;
                case StatementSyntax or MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    void Copy(string source, int start, int end)
    {
        if (end <= start)
            return;
        _output.Append(source, start, end - start);
        TrackCopiedText(source.AsSpan(start, end - start));
    }

    void TrackCopiedText(ReadOnlySpan<char> text)
    {
        var lastBreak = text.LastIndexOfAny('\n', '\r');
        if (lastBreak < 0)
        {
            if (_column == _lineIndent.Length)
                _lineIndent += text[..InitialIndentLength(text)].ToString();
            _column += text.Length;
            return;
        }

        var currentLine = text[(lastBreak + 1)..];
        _column = currentLine.Length;
        _lineIndent = currentLine[..InitialIndentLength(currentLine)].ToString();
    }

    static int InitialIndentLength(ReadOnlySpan<char> text)
    {
        var length = 0;
        while (length < text.Length && text[length] is ' ' or '\t')
            length++;
        return length;
    }

    void Append(string text)
    {
        _output.Append(text);
        var lastBreak = text.LastIndexOfAny(LineBreaks);
        _column = lastBreak < 0 ? _column + text.Length : text.Length - lastBreak - 1;
    }

    void AppendTrivia(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            var text = item.ToFullString();
            _output.Append(text);
            TrackCopiedText(text);
        }
    }

    static readonly char[] LineBreaks = ['\n', '\r'];
}
