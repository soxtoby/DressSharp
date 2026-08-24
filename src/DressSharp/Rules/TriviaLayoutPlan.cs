using System.Globalization;
using System.Text;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

/// <summary>
/// Sparse trivia edits prepared against the effective token stream without rebuilding the tree.
/// </summary>
sealed class TriviaLayoutPlan
{
    readonly EffectiveTokenStream.Piece[] _pieces;
    readonly TokenEdit?[]? _edits;

    TriviaLayoutPlan(
        EffectiveTokenStream.Piece[] pieces,
        TokenEdit?[]? edits,
        int skippedOccurrences)
    {
        _pieces = pieces;
        _edits = edits;
        SkippedOccurrences = skippedOccurrences;
    }

    internal int SkippedOccurrences { get; }

    internal static PreparedSettings Prepare(
        RuleCatalog catalog,
        FormattingConfiguration configuration) =>
        new(
            BlankLineSettings.From(catalog.BlankLineRules, configuration),
            CommentSettings.From(catalog.CommentRules, configuration));

    internal static TriviaLayoutPlan For(
        SyntaxNode root,
        EffectiveTokenStream stream,
        PreparedSettings settings,
        RuleContext context)
    {
        if (!settings.Enabled)
            return new(stream.Pieces, null, 0);

        var builder = new Builder(root, stream, context);
        builder.Apply(settings.BlankLines, settings.Comments);
        return builder.Finish(context.TakeSkippedOccurrences());
    }

    internal SyntaxTriviaList Leading(int index) =>
        _edits?[index] is { HasLeading: true } edit
            ? edit.Leading
            : _pieces[index].Token.LeadingTrivia;

    internal SyntaxTriviaList Trailing(int index) =>
        _edits?[index] is { HasTrailing: true } edit
            ? edit.Trailing
            : _pieces[index].Token.TrailingTrivia;

    internal bool HasLeadingEdit(int index) =>
        _edits?[index] is { HasLeading: true };

    internal bool HasTrailingEdit(int index) =>
        _edits?[index] is { HasTrailing: true };

    internal bool HasGapEdit(int rightIndex) =>
        HasTrailingEdit(rightIndex - 1) || HasLeadingEdit(rightIndex);

    internal bool HasMeaningfulGap(int rightIndex) =>
        HasMeaningfulTrivia(Trailing(rightIndex - 1))
        || HasMeaningfulTrivia(Leading(rightIndex));

    internal bool HasLineBreak(int rightIndex) =>
        ContainsLineBreak(Trailing(rightIndex - 1))
        || ContainsLineBreak(Leading(rightIndex));

    internal sealed record PreparedSettings(
        BlankLineSettings BlankLines,
        CommentSettings Comments)
    {
        internal bool Enabled => BlankLines.Enabled || Comments.Enabled;
    }

    static bool HasMeaningfulTrivia(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (!item.IsKind(SyntaxKind.WhitespaceTrivia)
                && !item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                return true;
            }
        }

        return false;
    }

    static bool ContainsLineBreak(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (item.IsKind(SyntaxKind.EndOfLineTrivia))
                return true;
        }

        return false;
    }

    sealed class TokenEdit
    {
        internal bool HasLeading { get; set; }
        internal SyntaxTriviaList Leading { get; set; }
        internal bool HasTrailing { get; set; }
        internal SyntaxTriviaList Trailing { get; set; }
    }

    sealed class Builder
    {
        readonly SyntaxNode _root;
        readonly EffectiveTokenStream _stream;
        readonly EffectiveTokenStream.Piece[] _pieces;
        readonly RuleContext _context;
        TokenEdit?[]? _edits;

        internal Builder(SyntaxNode root, EffectiveTokenStream stream, RuleContext context)
        {
            _root = root;
            _stream = stream;
            _pieces = stream.Pieces;
            _context = context;
        }

        internal void Apply(BlankLineSettings blankLines, CommentSettings comments)
        {
            Dictionary<int, (int Priority, int Count)>? targets = null;
            if (blankLines.HasBoundaryRules)
            {
                targets = [];
                Collect(_root);
            }

            for (var index = 0; index < _pieces.Length; index++)
            {
                if (blankLines.Enabled)
                {
                    var leading = Leading(index);
                    if (targets is not null && targets.TryGetValue(index, out var target))
                        leading = Separate(index, leading, target.Count, _context.LineEnding);

                    if (blankLines.Maximum is { } cap
                        && cap != int.MaxValue
                        && ContainsLineBreak(leading)
                        && !Unsafe(index, _pieces[index].Token))
                    {
                        var previousBreaks = index == 0 ? 0 : EndingLineBreaks(Trailing(index - 1));
                        leading = Cap(
                            leading,
                            Math.Max(0, RequiredLineBreaks(cap) - previousBreaks),
                            RequiredLineBreaks(cap));
                    }

                    SetLeading(index, leading);
                }

                if (!comments.Enabled)
                    continue;

                var attached = comments.AttachedPlacement;
                if (attached is not null)
                {
                    if (index == 0)
                        PrepareBoundary(0, leading: true, attached);
                    else
                        PrepareGap(index, attached);
                }

                SetLeading(index, Apply(Leading(index), index, comments));
                if (index > 0)
                {
                    SetTrailing(
                        index - 1,
                        Apply(Trailing(index - 1), index - 1, comments));
                }
            }

            if (comments.Enabled && _pieces.Length != 0)
            {
                var last = _pieces.Length - 1;
                if (comments.AttachedPlacement is { } attached)
                    PrepareBoundary(last, leading: false, attached);
                SetTrailing(last, Apply(Trailing(last), last, comments));
            }

            void Collect(SyntaxNode node)
            {
                switch (node)
                {
                    case CompilationUnitSyntax unit:
                        AddUsingGroupTargets(unit.Usings);
                        foreach (var member in unit.Members)
                            CollectMember(member);
                        break;
                    case BaseNamespaceDeclarationSyntax declaration:
                        if (blankLines.AroundNamespaces is { } namespaceCount)
                            AddBoundaryTargets(declaration, namespaceCount, 0);
                        AddUsingGroupTargets(declaration.Usings);
                        foreach (var member in declaration.Members)
                            CollectMember(member);
                        break;
                    case BaseTypeDeclarationSyntax declaration:
                        if (blankLines.AroundTypes is { } typeCount)
                            AddBoundaryTargets(declaration, typeCount, 1);
                        if (declaration is TypeDeclarationSyntax type)
                        {
                            AddMemberTargets(type.Members);
                            foreach (var member in type.Members)
                                CollectMember(member);
                        }
                        break;
                }
            }

            void CollectMember(MemberDeclarationSyntax member)
            {
                if (member is BaseNamespaceDeclarationSyntax or BaseTypeDeclarationSyntax)
                    Collect(member);
            }

            void AddTarget(SyntaxNode node, int count, int priority)
            {
                if (_context.IsUnsafe(node))
                    return;
                SetTarget(node.GetFirstToken(), count, priority);
            }

            void AddBoundaryTargets(SyntaxNode node, int count, int priority)
            {
                if (_context.IsUnsafe(node))
                    return;
                var first = node.GetFirstToken();
                if (first.GetPreviousToken().RawKind != 0)
                    SetTarget(first, count, priority);
                var next = node.GetLastToken().GetNextToken();
                if (next.RawKind != 0 && !next.IsKind(SyntaxKind.EndOfFileToken))
                    SetTarget(next, count, priority);
            }

            void SetTarget(SyntaxToken token, int count, int priority)
            {
                if (!_stream.TryIndexOfOriginalTarget(token, out var index))
                    return;
                if (!targets.TryGetValue(index, out var existing)
                    || existing.Priority <= priority)
                {
                    targets[index] = (priority, count);
                }
            }

            void AddUsingGroupTargets(SyntaxList<UsingDirectiveSyntax> directives)
            {
                if (blankLines.BetweenUsingGroups is not { } count)
                    return;
                for (var index = 1; index < directives.Count; index++)
                {
                    if (UsingCategory(directives[index - 1]) != UsingCategory(directives[index]))
                        AddTarget(directives[index], count, 3);
                }
            }

            void AddMemberTargets(SyntaxList<MemberDeclarationSyntax> members)
            {
                for (var index = 1; index < members.Count; index++)
                {
                    if (blankLines.BetweenMembers is { } memberCount)
                        AddTarget(members[index], memberCount, 2);
                    if (blankLines.BetweenMemberCategories is { } categoryCount
                        && MemberCategory(members[index - 1]) != MemberCategory(members[index]))
                    {
                        AddTarget(members[index], categoryCount, 4);
                    }
                }
            }
        }

        internal TriviaLayoutPlan Finish(int skippedOccurrences) =>
            new(_pieces, _edits, skippedOccurrences);

        void PrepareBoundary(int tokenIndex, bool leading, string preference)
        {
            var original = leading ? Leading(tokenIndex) : Trailing(tokenIndex);
            if (!ContainsOrdinaryComment(original))
                return;
            var combined = original
                .Select(item => new OwnedTrivia(item, leading, _pieces[tokenIndex].IsOriginal))
                .ToList();
            if (!RewriteAttached(
                    combined,
                    preference,
                    hasLeftToken: !leading,
                    hasRightToken: leading,
                    trailingCommentsAttachRight: leading))
            {
                return;
            }

            var rewritten = SyntaxFactory.TriviaList(combined.Select(item => item.Trivia));
            if (leading)
                SetLeading(tokenIndex, rewritten);
            else
                SetTrailing(tokenIndex, rewritten);
        }

        void PrepareGap(int rightIndex, string preference)
        {
            var trailing = Trailing(rightIndex - 1);
            var leading = Leading(rightIndex);
            if (!ContainsOrdinaryComment(trailing) && !ContainsOrdinaryComment(leading))
                return;
            var combined = trailing
                .Select(item => new OwnedTrivia(item, Leading: false, _pieces[rightIndex - 1].IsOriginal))
                .Concat(leading.Select(item => new OwnedTrivia(item, Leading: true, _pieces[rightIndex].IsOriginal)))
                .ToList();
            if (!RewriteAttached(
                    combined,
                    preference,
                    hasLeftToken: true,
                    hasRightToken: true,
                    trailingCommentsAttachRight: StartsAttachedSyntax(_pieces[rightIndex - 1].Token)))
            {
                return;
            }

            SetTrailing(
                rightIndex - 1,
                SyntaxFactory.TriviaList(combined.Where(item => !item.Leading).Select(item => item.Trivia)));
            SetLeading(
                rightIndex,
                SyntaxFactory.TriviaList(combined.Where(item => item.Leading).Select(item => item.Trivia)));
        }

        bool RewriteAttached(
            List<OwnedTrivia> combined,
            string preference,
            bool hasLeftToken,
            bool hasRightToken,
            bool trailingCommentsAttachRight)
        {
            var changed = false;
            for (var index = 0; index < combined.Count; index++)
            {
                var owned = combined[index];
                var comment = owned.Trivia;
                if (!IsOrdinaryComment(comment)
                    || owned.IsOriginal && _context.IsUnsafe(comment.FullSpan))
                {
                    continue;
                }

                var before = index;
                while (before > 0 && IsWhitespaceOrEndOfLine(combined[before - 1].Trivia))
                    before--;
                if (preference == "own_line" && hasLeftToken)
                {
                    var replacement = before < index
                        ? OwnLineWhitespace(combined, before, index, _context.LineEnding)
                        : [new(SyntaxFactory.EndOfLine(_context.LineEnding), combined[index].Leading, owned.IsOriginal)];
                    changed |= Replace(combined, before, index, replacement);
                    index = before + replacement.Count;
                }

                var after = index + 1;
                while (after < combined.Count && IsWhitespaceOrEndOfLine(combined[after].Trivia))
                    after++;
                if (preference == "own_line" && hasRightToken)
                {
                    var replacement = after > index + 1
                        ? OwnLineWhitespace(combined, index + 1, after, _context.LineEnding)
                        : [new(SyntaxFactory.EndOfLine(_context.LineEnding), combined[index].Leading, owned.IsOriginal)];
                    changed |= Replace(combined, index + 1, after, replacement);
                }
                else if (preference == "same_line"
                    && (combined[index].Leading || trailingCommentsAttachRight)
                    && !comment.IsKind(SyntaxKind.SingleLineCommentTrivia)
                    && hasRightToken
                    && after == combined.Count)
                {
                    var owner = after > index + 1 ? combined[index + 1] : combined[index];
                    changed |= Replace(
                        combined,
                        index + 1,
                        after,
                        [new(SyntaxFactory.Space, owner.Leading, owner.IsOriginal)]);
                }
            }

            return changed;
        }

        SyntaxTriviaList Apply(SyntaxTriviaList trivia, int tokenIndex, CommentSettings settings)
        {
            if (settings.OnlyXmlElementLayout is { } onlyLayout)
            {
                return CouldChangeXmlElementLayout(trivia)
                    ? ApplyXmlElementLayout(trivia, tokenIndex, onlyLayout)
                    : trivia;
            }

            if (!HasRelevantTrivia(trivia, settings))
                return trivia;

            var items = trivia.ToList();
            ApplyXmlPlacement(items, tokenIndex, settings[CommentKind.XmlPlacement]);
            ApplyCommentSpacing(items, tokenIndex, settings);
            var rewritten = SyntaxFactory.TriviaList(items);
            return settings[CommentKind.XmlElementLayout] is { } layout
                ? ApplyXmlElementLayout(rewritten, tokenIndex, layout)
                : rewritten;
        }

        void ApplyXmlPlacement(List<SyntaxTrivia> items, int tokenIndex, string? preference)
        {
            if (preference is null)
                return;

            for (var index = 0; index < items.Count; index++)
            {
                var comment = items[index];
                if (!IsDocumentationComment(comment) || Unsafe(tokenIndex, comment))
                    continue;

                var end = index + 1;
                while (end < items.Count && IsWhitespaceOrEndOfLine(items[end]))
                    end++;
                if (end < items.Count)
                    continue;
                var indent = LastIndent(SyntaxFactory.TriviaList(items.GetRange(index + 1, end - index - 1)));
                items.RemoveRange(index + 1, end - index - 1);
                var breaks = (preference == "separated" ? 1 : 0)
                    + (comment.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia) ? 1 : 0);
                for (var count = 0; count < breaks; count++)
                    items.Insert(index + 1 + count, SyntaxFactory.EndOfLine(_context.LineEnding));
                if (indent.Length != 0)
                {
                    items.Insert(
                        index + 1 + breaks,
                        SyntaxFactory.Whitespace(indent));
                }
            }
        }

        void ApplyCommentSpacing(List<SyntaxTrivia> items, int tokenIndex, CommentSettings settings)
        {
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                if (settings[CommentKind.LineSpacing] is { } line
                    && item.IsKind(SyntaxKind.SingleLineCommentTrivia))
                {
                    if (Unsafe(tokenIndex, item))
                        continue;
                    var text = item.ToString();
                    items[index] = SyntaxFactory.Comment(
                        "//" + (line == "single" ? " " : "") + text[2..].TrimStart(' ', '\t'));
                }
                else if (settings[CommentKind.BlockSpacing] is { } block
                    && item.IsKind(SyntaxKind.MultiLineCommentTrivia))
                {
                    if (Unsafe(tokenIndex, item))
                        continue;
                    var text = item.ToString();
                    var content = text[2..^2];
                    if (content.IndexOfAny(['\r', '\n']) >= 0)
                        continue;
                    content = content.Trim(' ', '\t');
                    var space = block == "single" ? " " : "";
                    items[index] = SyntaxFactory.Comment("/*" + space + content + space + "*/");
                }
            }
        }

        SyntaxTriviaList ApplyXmlElementLayout(
            SyntaxTriviaList trivia,
            int tokenIndex,
            string preference)
        {
            if (!CouldChangeXmlElementLayout(trivia))
                return trivia;

            var found = false;
            foreach (var item in trivia)
            {
                if (!IsDocumentationComment(item))
                    continue;
                found = true;
                if (Unsafe(tokenIndex, item))
                    return trivia;
            }

            if (!found)
                return trivia;

            var text = trivia.ToFullString();
            var lineEnding = FirstLineEnding(text) ?? _context.LineEnding;
            var rewritten = RewriteXmlElementLayout(text, preference == "multi_line", lineEnding);
            return rewritten == text ? trivia : SyntaxFactory.ParseLeadingTrivia(rewritten);
        }

        bool Unsafe(int tokenIndex, SyntaxToken token) =>
            _pieces[tokenIndex].IsOriginal && _context.IsUnsafe(token);

        bool Unsafe(int tokenIndex, SyntaxTrivia trivia) =>
            _pieces[tokenIndex].IsOriginal && _context.IsUnsafe(trivia.FullSpan);

        SyntaxTriviaList Leading(int index) =>
            _edits?[index] is { HasLeading: true } edit
                ? edit.Leading
                : _pieces[index].Token.LeadingTrivia;

        SyntaxTriviaList Trailing(int index) =>
            _edits?[index] is { HasTrailing: true } edit
                ? edit.Trailing
                : _pieces[index].Token.TrailingTrivia;

        void SetLeading(int index, SyntaxTriviaList trivia)
        {
            if (trivia == Leading(index))
                return;
            var edit = Edit(index);
            edit.HasLeading = true;
            edit.Leading = trivia;
        }

        void SetTrailing(int index, SyntaxTriviaList trivia)
        {
            if (trivia == Trailing(index))
                return;
            var edit = Edit(index);
            edit.HasTrailing = true;
            edit.Trailing = trivia;
        }

        TokenEdit Edit(int index)
        {
            _edits ??= new TokenEdit?[_pieces.Length];
            if (_edits[index] is { } edit)
                return edit;
            edit = new();
            _edits[index] = edit;
            return edit;
        }

        SyntaxTriviaList Separate(
            int tokenIndex,
            SyntaxTriviaList leading,
            int count,
            string lineEnding)
        {
            var boundaryEnd = 0;
            while (boundaryEnd < leading.Count && IsWhitespaceOrEndOfLine(leading[boundaryEnd]))
                boundaryEnd++;
            if (leading.Skip(boundaryEnd).Any(item => !item.IsComment()))
                return leading;
            var trailingBreaks = tokenIndex == 0 ? 0 : EndingLineBreaks(Trailing(tokenIndex - 1));
            var breaks = Math.Max(0, RequiredLineBreaks(count) - trailingBreaks);
            var current = 0;
            for (var index = 0; index < boundaryEnd; index++)
            {
                if (leading[index].IsKind(SyntaxKind.EndOfLineTrivia))
                    current++;
            }
            if (breaks == current)
                return leading;

            var boundary = SyntaxFactory.TriviaList(leading.Take(boundaryEnd));
            var replacement = SyntaxFactory.TriviaList(
                Enumerable.Repeat(SyntaxFactory.EndOfLine(lineEnding), breaks)
                    .Append(SyntaxFactory.Whitespace(LastIndent(boundary))));
            return replacement.AddRange(leading.Skip(boundaryEnd));
        }
    }

    internal sealed record BlankLineSettings(
        int? AroundNamespaces,
        int? AroundTypes,
        int? BetweenMembers,
        int? BetweenUsingGroups,
        int? BetweenMemberCategories,
        int? Maximum)
    {
        internal bool Enabled => HasBoundaryRules || Maximum is not null;
        internal bool HasBoundaryRules => AroundNamespaces is not null
            || AroundTypes is not null
            || BetweenMembers is not null
            || BetweenUsingGroups is not null
            || BetweenMemberCategories is not null;

        internal static BlankLineSettings From(
            IReadOnlyList<BlankLineRule> rules,
            FormattingConfiguration configuration)
        {
            var values = new int?[Enum.GetValues<BlankLineKind>().Length];
            foreach (var rule in rules)
            {
                if (!TryPreference(configuration, rule.Metadata.RuleKey, out var preference))
                    continue;
                if (!int.TryParse(preference, out var count) || count < 0)
                {
                    throw new InvalidOperationException(
                        $"Invalid value '{preference}' for '{rule.Metadata.RuleKey.ToName()}'.");
                }

                values[(int)rule.Kind] = count;
            }

            return new(values[0], values[1], values[2], values[3], values[4], values[5]);
        }
    }

    internal sealed class CommentSettings
    {
        readonly string?[] _values = new string?[Enum.GetValues<CommentKind>().Length];

        internal bool Enabled { get; private set; }
        internal string? this[CommentKind kind] => _values[(int)kind];
        internal string? AttachedPlacement { get; private set; }
        internal string? OnlyXmlElementLayout { get; private set; }

        internal static CommentSettings From(
            IReadOnlyList<CommentRule> rules,
            FormattingConfiguration configuration)
        {
            var settings = new CommentSettings();
            foreach (var rule in rules)
            {
                if (!TryPreference(configuration, rule.Metadata.RuleKey, out var preference))
                    continue;
                if (!rule.Metadata.Accepts(preference))
                {
                    throw new InvalidOperationException(
                        $"Invalid value '{preference}' for '{rule.Metadata.RuleKey.ToName()}'.");
                }

                settings._values[(int)rule.Kind] = preference;
                if (rule.Kind != CommentKind.AttachedPlacement || preference != "auto")
                    settings.Enabled = true;
            }

            var attached = settings[CommentKind.AttachedPlacement];
            settings.AttachedPlacement = attached == "auto" ? null : attached;
            if (settings[CommentKind.XmlElementLayout] is { } layout
                && settings[CommentKind.LineSpacing] is null
                && settings[CommentKind.BlockSpacing] is null
                && settings.AttachedPlacement is null
                && settings[CommentKind.XmlPlacement] is null)
            {
                settings.OnlyXmlElementLayout = layout;
            }

            return settings;
        }
    }

    readonly record struct OwnedTrivia(SyntaxTrivia Trivia, bool Leading, bool IsOriginal);

    static bool TryPreference(
        FormattingConfiguration configuration,
        RuleKey key,
        out string preference)
    {
        if (configuration.Preferences.TryGetValue(key, out preference!)
            && !preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        preference = "";
        return false;
    }

    static int UsingCategory(UsingDirectiveSyntax directive) =>
        directive.Alias is not null
            ? 2
            : directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? 1 : 0;

    static int MemberCategory(MemberDeclarationSyntax member) =>
        member switch
        {
            FieldDeclarationSyntax or EventFieldDeclarationSyntax => 0,
            ConstructorDeclarationSyntax => 1,
            PropertyDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax => 2,
            MethodDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => 3,
            BaseTypeDeclarationSyntax or DelegateDeclarationSyntax => 4,
            _ => 5
        };

    static int RequiredLineBreaks(int blankLines) =>
        blankLines == int.MaxValue ? int.MaxValue : blankLines + 1;

    static int EndingLineBreaks(SyntaxTriviaList trivia)
    {
        var count = 0;
        for (var index = trivia.Count - 1; index >= 0 && IsWhitespaceOrEndOfLine(trivia[index]); index--)
        {
            if (trivia[index].IsKind(SyntaxKind.EndOfLineTrivia))
                count++;
        }

        return count;
    }

    static SyntaxTriviaList Cap(
        SyntaxTriviaList trivia,
        int initialAllowedLineBreaks,
        int allowedLineBreaks)
    {
        if (!NeedsCap(trivia, initialAllowedLineBreaks, allowedLineBreaks))
            return trivia;

        var result = new List<SyntaxTrivia>(trivia.Count);
        var lineBreaks = 0;
        var allowed = initialAllowedLineBreaks;
        foreach (var item in trivia)
        {
            if (item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                lineBreaks++;
                if (lineBreaks > allowed)
                    continue;
            }
            else if (!item.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                lineBreaks = 0;
                allowed = allowedLineBreaks;
            }

            result.Add(item);
        }

        return SyntaxFactory.TriviaList(result);
    }

    static bool NeedsCap(
        SyntaxTriviaList trivia,
        int initialAllowedLineBreaks,
        int allowedLineBreaks)
    {
        var lineBreaks = 0;
        var allowed = initialAllowedLineBreaks;
        foreach (var item in trivia)
        {
            if (item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                if (++lineBreaks > allowed)
                    return true;
            }
            else if (!item.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                lineBreaks = 0;
                allowed = allowedLineBreaks;
            }
        }

        return false;
    }

    static bool IsWhitespaceOrEndOfLine(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.WhitespaceTrivia)
        || trivia.IsKind(SyntaxKind.EndOfLineTrivia);

    static string LastIndent(SyntaxTriviaList trivia)
    {
        for (var index = trivia.Count - 1; index >= 0; index--)
        {
            if (trivia[index].IsKind(SyntaxKind.WhitespaceTrivia))
                return trivia[index].ToString();
        }

        return "";
    }

    static bool ContainsOrdinaryComment(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (IsOrdinaryComment(item))
                return true;
        }

        return false;
    }

    static bool HasRelevantTrivia(SyntaxTriviaList trivia, CommentSettings settings)
    {
        foreach (var item in trivia)
        {
            if (settings[CommentKind.LineSpacing] is not null
                && item.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || settings[CommentKind.BlockSpacing] is not null
                && item.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || settings[CommentKind.AttachedPlacement] is { } attached
                && attached != "auto"
                && IsOrdinaryComment(item)
                || (settings[CommentKind.XmlPlacement] is not null
                    || settings[CommentKind.XmlElementLayout] is not null)
                && IsDocumentationComment(item))
            {
                return true;
            }
        }

        return false;
    }

    static bool IsOrdinaryComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);

    static bool IsDocumentationComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    static bool CouldChangeXmlElementLayout(SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            if (item.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                return true;
        }

        return false;
    }

    static bool StartsAttachedSyntax(SyntaxToken token) =>
        token.IsKind(SyntaxKind.OpenBraceToken)
        || token.IsKind(SyntaxKind.OpenParenToken)
        || token.IsKind(SyntaxKind.OpenBracketToken)
        || token.IsKind(SyntaxKind.CommaToken);

    static bool Replace(
        List<OwnedTrivia> combined,
        int start,
        int end,
        IReadOnlyList<OwnedTrivia> replacement)
    {
        var count = end - start;
        if (count == replacement.Count)
        {
            var equal = true;
            for (var index = 0; index < count; index++)
            {
                var current = combined[start + index];
                var wanted = replacement[index];
                if (current.Leading != wanted.Leading
                    || current.Trivia.RawKind != wanted.Trivia.RawKind
                    || current.Trivia.ToString() != wanted.Trivia.ToString())
                {
                    equal = false;
                    break;
                }
            }

            if (equal)
                return false;
        }

        combined.RemoveRange(start, count);
        combined.InsertRange(start, replacement);
        return true;
    }

    static List<OwnedTrivia> OwnLineWhitespace(
        List<OwnedTrivia> combined,
        int start,
        int end,
        string lineEnding)
    {
        var owner = combined[start];
        var whitespace = SyntaxFactory.TriviaList(
            combined.Skip(start).Take(end - start).Select(item => item.Trivia));
        var result = new List<OwnedTrivia>
        {
            new(SyntaxFactory.EndOfLine(lineEnding), owner.Leading, owner.IsOriginal)
        };
        var indent = LastIndent(whitespace);
        if (indent.Length != 0)
            result.Add(new(SyntaxFactory.Whitespace(indent), owner.Leading, owner.IsOriginal));
        return result;
    }

    static string? FirstLineEnding(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
                return "\n";
            if (text[index] == '\r')
                return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
        }

        return null;
    }

    static string RewriteXmlElementLayout(string text, bool multiLine, string lineEnding)
    {
        StringBuilder? rewritten = null;
        var copiedThrough = 0;
        for (var position = 0; position < text.Length;)
        {
            var first = ReadLine(text, position);
            if (multiLine
                ? TrySingleLineElement(text, first, out var element)
                : TryMultiLineElement(text, first, out element))
            {
                rewritten ??= new(text.Length + (multiLine ? lineEnding.Length * 2 + element.PrefixLength * 2 : 0));
                rewritten.Append(text.AsSpan(copiedThrough, element.Start - copiedThrough));
                rewritten.Append(text.AsSpan(element.Start, element.PrefixLength));
                rewritten.Append('<');
                rewritten.Append(text.AsSpan(element.NameStart, element.NameLength));
                rewritten.Append('>');
                if (multiLine)
                {
                    rewritten.Append(lineEnding);
                    rewritten.Append(text.AsSpan(element.Start, element.PrefixLength));
                }
                rewritten.Append(text.AsSpan(element.ContentStart, element.ContentLength));
                if (multiLine)
                {
                    rewritten.Append(lineEnding);
                    rewritten.Append(text.AsSpan(element.Start, element.PrefixLength));
                }
                rewritten.Append("</");
                rewritten.Append(text.AsSpan(element.NameStart, element.NameLength));
                rewritten.Append('>');
                copiedThrough = element.End;
                position = element.NextLine;
                continue;
            }

            if (first.Next == position)
                break;
            position = first.Next;
        }

        if (rewritten is null)
            return text;
        rewritten.Append(text.AsSpan(copiedThrough));
        return rewritten.ToString();
    }

    static bool TrySingleLineElement(string text, Line line, out XmlElementMatch match)
    {
        match = default;
        if (!TryPrefix(text, line, out var contentStart)
            || !TryOpenTag(text, contentStart, line.End, out var nameStart, out var nameLength, out var afterOpen))
        {
            return false;
        }

        var closeStart = text.AsSpan(afterOpen, line.End - afterOpen).IndexOf('<');
        if (closeStart <= 0)
            return false;
        closeStart += afterOpen;
        var afterClose = CloseTagEnd(text, closeStart, line.End, nameStart, nameLength);
        if (afterClose < 0 || !OnlyHorizontalWhitespace(text, afterClose, line.End))
            return false;

        match = new(
            line.Start,
            contentStart - line.Start,
            nameStart,
            nameLength,
            afterOpen,
            closeStart - afterOpen,
            line.End,
            line.Next);
        return true;
    }

    static bool TryMultiLineElement(string text, Line first, out XmlElementMatch match)
    {
        match = default;
        if (first.Next >= text.Length
            || !TryPrefix(text, first, out var contentStart)
            || !TryOpenTag(text, contentStart, first.End, out var nameStart, out var nameLength, out var afterOpen)
            || !OnlyHorizontalWhitespace(text, afterOpen, first.End))
        {
            return false;
        }

        var middle = ReadLine(text, first.Next);
        if (middle.Next >= text.Length
            || !TryPrefix(text, middle, out var middleContent)
            || middleContent == middle.End
            || text.AsSpan(middleContent, middle.End - middleContent).Contains('<'))
        {
            return false;
        }

        var close = ReadLine(text, middle.Next);
        if (!TryPrefix(text, close, out var closeStart))
            return false;
        var afterClose = CloseTagEnd(text, closeStart, close.End, nameStart, nameLength);
        if (afterClose < 0 || !OnlyHorizontalWhitespace(text, afterClose, close.End))
            return false;

        match = new(
            first.Start,
            contentStart - first.Start,
            nameStart,
            nameLength,
            middleContent,
            middle.End - middleContent,
            close.End,
            close.Next);
        return true;
    }

    static bool TryPrefix(string text, Line line, out int contentStart)
    {
        var position = line.Start;
        while (position < line.End && IsHorizontalWhitespace(text[position]))
            position++;
        if (position + 3 > line.End || text.AsSpan(position, 3) is not "///")
        {
            contentStart = 0;
            return false;
        }

        position += 3;
        if (position < line.End && IsHorizontalWhitespace(text[position]))
            position++;
        contentStart = position;
        return true;
    }

    static bool TryOpenTag(
        string text,
        int start,
        int end,
        out int nameStart,
        out int nameLength,
        out int afterTag)
    {
        nameStart = start + 1;
        nameLength = 0;
        afterTag = 0;
        if (start + 3 > end || text[start] != '<' || !IsAsciiLetter(text[nameStart]))
            return false;

        var position = nameStart + 1;
        while (position < end && IsSimpleNameCharacter(text[position]))
            position++;
        if (position >= end || text[position] != '>')
            return false;

        nameLength = position - nameStart;
        afterTag = position + 1;
        return true;
    }

    static int CloseTagEnd(
        string text,
        int start,
        int end,
        int nameStart,
        int nameLength)
    {
        var length = nameLength + 3;
        if (start + length > end
            || text[start] != '<'
            || text[start + 1] != '/'
            || text[start + length - 1] != '>'
            || !text.AsSpan(start + 2, nameLength).SequenceEqual(text.AsSpan(nameStart, nameLength)))
        {
            return -1;
        }

        return start + length;
    }

    static bool OnlyHorizontalWhitespace(string text, int start, int end)
    {
        for (var position = start; position < end; position++)
        {
            if (!IsHorizontalWhitespace(text[position]))
                return false;
        }

        return true;
    }

    static bool IsHorizontalWhitespace(char character) =>
        character is not '\r' and not '\n' && char.IsWhiteSpace(character);

    static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    static bool IsSimpleNameCharacter(char character)
    {
        if (character is '.' or '-' or '_')
            return true;
        return char.GetUnicodeCategory(character) is
            UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation;
    }

    static Line ReadLine(string text, int start)
    {
        var end = start;
        while (end < text.Length && text[end] is not '\r' and not '\n')
            end++;
        var next = end;
        if (next < text.Length && text[next++] == '\r'
            && next < text.Length && text[next] == '\n')
        {
            next++;
        }
        return new(start, end, next);
    }

    readonly record struct Line(int Start, int End, int Next);

    readonly record struct XmlElementMatch(
        int Start,
        int PrefixLength,
        int NameStart,
        int NameLength,
        int ContentStart,
        int ContentLength,
        int End,
        int NextLine);
}
