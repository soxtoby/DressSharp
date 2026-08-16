using System.Text;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Rules;

/// <summary>
/// Writes a formatted file in one walk, deciding whitespace as it goes rather than rewriting the
/// tree once per rule.
/// </summary>
/// <remarks>
/// This is a spike, not a replacement. It exists to measure what the emitter shape costs: the token
/// walk, the per-gap spacing decision, indentation tracking, the width lookup a wrapping decision
/// needs, and building the output text. It carries the real token-spacing rules but only a sketch of
/// the line-break and wrapping rules, so its output is not the catalog's output. Enable it with
/// <c>DRESSSHARP_EMITTER=1</c>.
/// </remarks>
sealed class SinglePassEmitter
{
    internal static bool Enabled { get; } = Environment.GetEnvironmentVariable("DRESSSHARP_EMITTER") == "1";

    readonly EmitterPlan _plan;
    readonly string _source;
    readonly StringBuilder _output;
    readonly SyntaxToken[] _tokens;

    /// <summary>
    /// Cumulative visual width of the tokens before each index, used to size a construct without
    /// walking it. Wrapping decisions ask for these, and asking the tree costs a subtree walk each.
    /// </summary>
    readonly int[] _cumulativeWidth;

    int _column;

    /// <summary>
    /// The indentation in force on the line each token was written on, so that a construct can be
    /// indented relative to the line its owner opened on without re-reading the tree.
    /// </summary>
    readonly string[] _lineIndentAt;

    string _lineIndent = "";
    readonly List<string> _levels = [""];

    SinglePassEmitter(SyntaxNode root, EmitterPlan plan, string source)
    {
        _plan = plan;
        _source = source;
        _output = new StringBuilder(source.Length);
        _tokens = [.. root.DescendantTokens()];
        _cumulativeWidth = new int[_tokens.Length + 1];
        for (var index = 0; index < _tokens.Length; index++)
            _cumulativeWidth[index + 1] = _cumulativeWidth[index] + _tokens[index].Text.Length + 1;
        _lineIndentAt = new string[_tokens.Length];
    }

    internal static string Emit(SyntaxNode root, EmitterPlan plan, string source) =>
        new SinglePassEmitter(root, plan, source).Run();

    string Run()
    {
        var pair = new TokenPair();
        for (var index = 0; index < _tokens.Length; index++)
        {
            var token = _tokens[index];
            if (index == 0)
                Copy(token.FullSpan.Start, token.SpanStart);
            else
                EmitGap(pair, index);

            _lineIndentAt[index] = _lineIndent;
            EnterOrLeave(token, index);
            Append(token.Text);
        }

        if (_tokens.Length > 0)
            Copy(_tokens[^1].Span.End, _tokens[^1].FullSpan.End);
        return _output.ToString();
    }

    /// <summary>
    /// Writes whatever belongs between the previous token and this one: any trivia that carries
    /// meaning, otherwise the whitespace the rules ask for.
    /// </summary>
    void EmitGap(TokenPair pair, int index)
    {
        var left = _tokens[index - 1];
        var right = _tokens[index];

        // Inside an interpolated string the characters between tokens are content, not trivia, and
        // the braces that open a hole are not the braces the layout rules mean. Editing either
        // rewrites the string.
        if (InsideInterpolatedString(left) || InsideInterpolatedString(right))
        {
            Copy(left.Span.End, right.SpanStart);
            return;
        }

        // Comments, directives and disabled text have to survive, and where they sit decides the
        // line structure around them, so a gap holding any of them is copied rather than decided.
        if (HasMeaningfulTrivia(left.TrailingTrivia) || HasMeaningfulTrivia(right.LeadingTrivia))
        {
            Copy(left.Span.End, right.SpanStart);
            return;
        }

        // A line-break rule that owns this boundary decides it outright, in either direction.
        if (ClaimsBreak(right) is { } wantsBreak)
        {
            if (wantsBreak)
                StartLine(right);
            else
                Append(" ");
            return;
        }

        // Indentation rules only ever restate the whitespace after an existing line break; where the
        // source kept two tokens on one line, no indentation rule has an opinion.
        var gap = _source.AsSpan(left.Span.End, right.SpanStart - left.Span.End);
        var lastBreak = gap.LastIndexOfAny('\n', '\r');
        if (lastBreak >= 0)
        {
            _output.Append(_source, left.Span.End, lastBreak + 1);
            WriteIndent(right);
            return;
        }

        // No rule owns this gap, so it keeps whatever it had. This is what makes the emitter a
        // formatter of explicit preferences rather than a reflowing pretty-printer: the catalog says
        // nothing about the space between a keyword and an identifier, and deciding one here would
        // rewrite every file over whitespace nobody asked about.
        pair.Reset(left, right);
        var desired = DesiredSpace(pair);
        if (desired is null)
        {
            Copy(left.Span.End, right.SpanStart);
            return;
        }

        if (desired.Value)
            Append(" ");
    }

    /// <summary>
    /// Whether any enabled line-break rule owns the boundary in front of the token.
    /// </summary>
    bool? ClaimsBreak(SyntaxToken token)
    {
        foreach (var (rule, categories) in _plan.NewLines)
        {
            if (rule.ClaimsBreakBefore(token, categories) is { } claim)
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

    /// <summary>
    /// Tracks the indentation a brace opens and closes.
    /// </summary>
    /// <remarks>
    /// A brace's own indentation is measured from the line its owning construct began on, and its
    /// contents from the brace, which is what <c>csharp_indent_braces</c> and
    /// <c>csharp_indent_block_contents</c> each select. Recording the indent every token was written
    /// at lets both be answered without asking the tree where a line starts.
    /// </remarks>
    void EnterOrLeave(SyntaxToken token, int index)
    {
        if (token.IsKind(SyntaxKind.OpenBraceToken))
        {
            var owner = token.Parent;
            var ownerIndent = owner is null ? _lineIndent : _lineIndentAt[IndexOf(owner.GetFirstToken(), 0)] ?? _lineIndent;
            var braceIndent = ownerIndent + (_plan.IndentBraces ? _plan.IndentUnit : "");
            _levels.Add(braceIndent + (_plan.IndentBlockContents ? _plan.IndentUnit : ""));
            BraceIndents.Push(braceIndent);
        }
        else if (token.IsKind(SyntaxKind.CloseBraceToken) && _levels.Count > 1)
        {
            _levels.RemoveAt(_levels.Count - 1);
            if (BraceIndents.Count > 0)
                BraceIndents.Pop();
        }
    }

    readonly Stack<string> BraceIndents = new();

    /// <summary>
    /// The indentation a token starting a line should be written at.
    /// </summary>
    string IndentFor(SyntaxToken token) =>
        token.IsKind(SyntaxKind.CloseBraceToken) && BraceIndents.Count > 0 ? BraceIndents.Peek() : _levels[^1];

    /// <summary>
    /// Whether the construct opening at this token would run past the configured line length. The
    /// wrapping rules are not ported yet; the width table they need is built and exercised here.
    /// </summary>
    internal bool WouldOverflow(int index)
    {
        var enclosing = EnclosingDelimitedEnd(index);
        return enclosing > index && _column + (_cumulativeWidth[enclosing] - _cumulativeWidth[index]) > _plan.MaximumLineLength;
    }

    /// <summary>
    /// The token index just past the delimited construct the token opens, or the index itself when it
    /// opens none.
    /// </summary>
    int EnclosingDelimitedEnd(int index)
    {
        var token = _tokens[index];
        if (!token.IsKind(SyntaxKind.OpenParenToken) && !token.IsKind(SyntaxKind.OpenBracketToken))
            return index;
        var parent = token.Parent;
        if (parent is null)
            return index;
        return IndexOf(parent.GetLastToken(), index);
    }

    /// <summary>
    /// The position of a token in the document-order walk, found by search rather than by scanning,
    /// so that sizing a construct stays cheap however deeply constructs nest.
    /// </summary>
    int IndexOf(SyntaxToken token, int notBefore)
    {
        var start = token.FullSpan.Start;
        var low = notBefore;
        var high = _tokens.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var candidate = _tokens[middle].FullSpan.Start;
            if (candidate < start)
                low = middle + 1;
            else if (candidate > start)
                high = middle - 1;
            else
                return middle;
        }

        return notBefore;
    }

    /// <summary>
    /// Whether a spacing rule claims the gap, and if so what it asks for. Null means no rule owns it.
    /// </summary>
    bool? DesiredSpace(TokenPair pair)
    {
        var candidates = _plan.Trigger(pair.Left.RawKind) | _plan.Trigger(pair.Right.RawKind);
        if (candidates == 0)
            return null;

        var desired = default(bool?);
        var rules = _plan.Spacing;
        for (var index = 0; index < rules.Length; index++)
        {
            if ((candidates & (1UL << index)) == 0)
                continue;
            var candidate = rules[index].Rule.DesiredSpace(pair.Left, pair.Right, rules[index].Preference);
            if (candidate is not null)
                desired = candidate;
        }

        return desired;
    }

    static bool InsideInterpolatedString(SyntaxToken token)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.InterpolatedStringExpressionSyntax)
                return true;
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax or Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax)
                return false;
        }

        return false;
    }

    static bool HasMeaningfulTrivia(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (!item.IsKind(SyntaxKind.WhitespaceTrivia) && !item.IsKind(SyntaxKind.EndOfLineTrivia))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Writes a range of the original file straight through.
    /// </summary>
    /// <remarks>
    /// Gaps no rule owns are the common case, and copying the source range keeps them byte-exact
    /// without materialising a string per trivia node.
    /// </remarks>
    void Copy(int start, int end)
    {
        if (end <= start)
            return;
        _output.Append(_source, start, end - start);
        var lastBreak = _source.LastIndexOfAny(LineBreaks, end - 1, end - start);
        if (lastBreak < 0)
        {
            _column += end - start;
            return;
        }

        // What was preserved after the line break is the indentation this line actually starts at,
        // and constructs opening on it are measured from there, so it has to be recorded.
        _column = end - lastBreak - 1;
        _lineIndent = _source[(lastBreak + 1)..end];
    }

    void Append(string text)
    {
        _output.Append(text);
        var lastBreak = text.LastIndexOfAny(LineBreaks);
        _column = lastBreak < 0 ? _column + text.Length : text.Length - lastBreak - 1;
    }

    static readonly char[] LineBreaks = ['\n', '\r'];
}

/// <summary>
/// Everything the emitter needs that depends on configuration rather than on the file, resolved once
/// so that per-file work is only walking and deciding.
/// </summary>
sealed class EmitterPlan
{
    readonly Dictionary<int, ulong> _triggers;

    EmitterPlan(
        (TokenSpacingRule Rule, string Preference)[] spacing,
        (NewLineRule Rule, NewLineRule.BraceCategories Categories)[] newLines,
        Dictionary<int, ulong> triggers,
        RuleSettings settings,
        bool indentBraces,
        bool indentBlockContents)
    {
        Spacing = spacing;
        NewLines = newLines;
        _triggers = triggers;
        MaximumLineLength = settings.MaximumLineLength;
        IndentUnit = settings.IndentUnit;
        IndentBraces = indentBraces;
        IndentBlockContents = indentBlockContents;
    }

    internal (TokenSpacingRule Rule, string Preference)[] Spacing { get; }
    internal (NewLineRule Rule, NewLineRule.BraceCategories Categories)[] NewLines { get; }
    internal int MaximumLineLength { get; }
    internal string IndentUnit { get; }
    internal bool IndentBraces { get; }
    internal bool IndentBlockContents { get; }
    internal string LineEnding { get; set; } = "\n";

    internal ulong Trigger(int rawKind) => _triggers.GetValueOrDefault(rawKind);

    internal static EmitterPlan From(RuleCatalog catalog, FormattingConfiguration configuration)
    {
        var spacing = new List<(TokenSpacingRule, string)>();
        var newLines = new List<(NewLineRule, NewLineRule.BraceCategories)>();
        foreach (var rule in catalog.Rules)
        {
            if (!configuration.Preferences.TryGetValue(rule.Metadata.PreferenceKey, out var preference)
                || preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
                continue;
            if (rule is TokenSpacingRule { ParticipatesInBatch: true } spacingRule)
                spacing.Add((spacingRule, preference));
            else if (rule is NewLineRule newLine)
                newLines.Add((newLine, NewLineRule.BraceCategories.From(newLine.Kind, preference)));
        }

        var triggers = new Dictionary<int, ulong>();
        for (var index = 0; index < spacing.Count; index++)
        {
            foreach (var kind in spacing[index].Item1.TriggerKinds)
                triggers[(int)kind] = triggers.GetValueOrDefault((int)kind) | (1UL << index);
        }

        return new(
            [.. spacing],
            [.. newLines],
            triggers,
            RuleSettings.From(configuration),
            IsTrue(configuration, "csharp_indent_braces"),
            IsTrue(configuration, "csharp_indent_block_contents"));
    }

    static bool IsTrue(FormattingConfiguration configuration, string key) =>
        configuration.Preferences.GetValueOrDefault(key, "false").Equals("true", StringComparison.OrdinalIgnoreCase);
}
