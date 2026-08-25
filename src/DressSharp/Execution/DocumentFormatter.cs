using System.Diagnostics;
using DressSharp.Architecture;
using DressSharp.IO;
using DressSharp.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Execution;

sealed class DocumentFormatter
{
    readonly FormattingConfiguration _configuration;
    readonly BenchmarkTiming _timing;
    readonly EmitterPlan _emitterPlan;
    readonly EmissionLayoutPlanner _layoutPlanner;
    readonly MemberRuleSet _memberRules;
    readonly RuleSettings _settings;
    readonly RepresentationPreferences _representation;
    readonly EmbeddedStatementSettings _embeddedStatements;

    internal DocumentFormatter(FormattingConfiguration configuration, BenchmarkTiming timing)
    {
        _configuration = configuration;
        _timing = timing;
        _emitterPlan = EmitterPlan.From(RuleCatalog.BuiltIn, configuration);
        _memberRules = MemberRuleSet.From(RuleCatalog.BuiltIn, configuration);
        _settings = RuleSettings.From(configuration);
        _embeddedStatements = EmbeddedStatementSettings.From(configuration);
        _layoutPlanner = new(
            RuleCatalog.BuiltIn,
            configuration,
            _settings,
            _emitterPlan);
        _representation = Representation(configuration);
    }

    internal async ValueTask<FormattedDocument> Format(SourceDocument document, CSharpParseOptions options, CancellationToken cancellationToken)
    {
        var root = await Parse(document, options, cancellationToken);
        var transformed = Transform(root, document.Text, options, cancellationToken);
        return new(Encode(document, transformed.Text), transformed.SkippedOccurrences);
    }

    internal string FormatSyntax(
        SyntaxNode root,
        string source,
        CSharpParseOptions options,
        CancellationToken cancellationToken = default) =>
        Transform(root, source, options, cancellationToken).Text;

    async ValueTask<SyntaxNode> Parse(
        SourceDocument document,
        CSharpParseOptions options,
        CancellationToken cancellationToken)
    {
        var parseStart = Stopwatch.GetTimestamp();
        var tree = CSharpSyntaxTree.ParseText(document.Text, options, document.Path, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);
        _timing.AddParse(Stopwatch.GetElapsedTime(parseStart));
        return root;
    }

    TransformedDocument Transform(
        SyntaxNode root,
        string source,
        CSharpParseOptions options,
        CancellationToken cancellationToken)
    {
        var transformStart = Stopwatch.GetTimestamp();
        var structural = ApplyFileScopedRules(root);
        var text = ReferenceEquals(structural.Root, root) ? source : structural.Root.ToFullString();
        var emitted = Emit(structural.Root, text, options, cancellationToken);
        _timing.AddTransform(Stopwatch.GetElapsedTime(transformStart));
        return new(
            emitted.Text,
            structural.SkippedOccurrences + emitted.SkippedOccurrences);
    }

    TransformationResult ApplyFileScopedRules(SyntaxNode root)
    {
        // Whole-file syntax rules still rewrite the tree because their effect is
        // not contained in a member. There are three, and they rarely apply.
        var structural = FileScopedStructuralRules.Transform(root, RuleCatalog.BuiltIn, _configuration);
        return structural.Succeeded
            ? structural
            : throw structural.Failure;
    }

    EmittedDocument Emit(
        SyntaxNode root,
        string text,
        CSharpParseOptions options,
        CancellationToken cancellationToken)
    {
        if (!_embeddedStatements.NeedsBracePlanning)
        {
            var only = EmitStage(root, text, _memberRules);
            return new(only.Text, only.RewriteSkippedOccurrences + only.LayoutSkippedOccurrences);
        }

        var candidate = EmitStage(
            root,
            text,
            _memberRules,
            _embeddedStatements.NeedsBraceFreeCandidate
                ? EmbeddedStatementBraces.MinimizeMember
                : null);
        var candidateRoot = ParseCandidate(candidate.Text, options, cancellationToken);
        var final = EmitStage(
            candidateRoot,
            candidate.Text,
            MemberRuleSet.Empty,
            (member, context) => EmbeddedStatementBraces.ApplyMember(
                member,
                _embeddedStatements,
                context));
        return new(
            final.Text,
            candidate.RewriteSkippedOccurrences
                + final.RewriteSkippedOccurrences
                + final.LayoutSkippedOccurrences);
    }

    SyntaxNode ParseCandidate(string text, CSharpParseOptions options, CancellationToken cancellationToken)
    {
        var parseStart = Stopwatch.GetTimestamp();
        var root = CSharpSyntaxTree.ParseText(
                text,
                options,
                cancellationToken: cancellationToken)
            .GetRoot(cancellationToken);
        _timing.AddParse(Stopwatch.GetElapsedTime(parseStart));
        return root;
    }

    EmissionStage EmitStage(
        SyntaxNode root,
        string text,
        MemberRuleSet memberRules,
        Func<SyntaxNode, RuleContext, SyntaxNode>? finishMember = null)
    {
        // The rest are scoped to the member they change, so a member no rule wants
        // is never copied, and the file's tree is never rebuilt around one that is.
        var ruleContext = new RuleContext(root, _settings);
        var rewrites = SyntaxRewritePlan.For(
            root,
            memberRules,
            ruleContext,
            finishMember is null ? null : member => finishMember(member, ruleContext));
        var rewriteSkipped = ruleContext.TakeSkippedOccurrences();
        var layout = _layoutPlanner.Plan(
            root,
            text,
            rewrites,
            ruleContext);
        var formatted = SinglePassEmitter.Emit(
            root,
            _emitterPlan,
            ruleContext,
            text,
            layout);
        return new(
            formatted,
            rewriteSkipped,
            layout.SkippedOccurrences + ruleContext.TakeSkippedOccurrences());
    }

    ReadOnlyMemory<byte> Encode(SourceDocument document, string formatted)
    {
        var encodeStart = Stopwatch.GetTimestamp();
        var content = document.Encode(formatted, _representation);
        _timing.AddEncode(Stopwatch.GetElapsedTime(encodeStart));
        return content;
    }

    static RepresentationPreferences Representation(FormattingConfiguration configuration) => new(
        Encoding(configuration.Preferences.GetValueOrDefault(RuleKey.Charset)),
        LineEnding(configuration.Preferences.GetValueOrDefault(RuleKey.EndOfLine)),
        Boolean(configuration.Preferences.GetValueOrDefault(RuleKey.InsertFinalNewline)),
        Boolean(configuration.Preferences.GetValueOrDefault(RuleKey.TrimTrailingWhitespace)));

    static SourceEncoding? Encoding(string? value) => value switch
        {
            "utf-8" => SourceEncoding.Utf8,
            "utf-8-bom" => SourceEncoding.Utf8Bom,
            "utf-16le" => SourceEncoding.Utf16LittleEndian,
            "utf-16be" => SourceEncoding.Utf16BigEndian,
            "latin1" => SourceEncoding.Latin1,
            _ => null
        };

    static string? LineEnding(string? value) => value switch
        {
            "lf" => "\n",
            "crlf" => "\r\n",
            "cr" => "\r",
            _ => null
        };

    static bool? Boolean(string? value) => bool.TryParse(value, out var parsed) ? parsed : null;
}

sealed record TransformedDocument(string Text, int SkippedOccurrences);
sealed record EmittedDocument(string Text, int SkippedOccurrences);
sealed record EmissionStage(string Text, int RewriteSkippedOccurrences, int LayoutSkippedOccurrences);
sealed record FormattedDocument(ReadOnlyMemory<byte> Content, int SkippedOccurrences);
