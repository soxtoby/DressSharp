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
        return new(Encode(document, transformed.Text), transformed.SkippedOccurrences, _representation.Encoding ?? document.SourceEncoding);
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
        var transformed = TransformOnce(root, source, options, cancellationToken);
        _timing.AddTransform(Stopwatch.GetElapsedTime(transformStart));
        return transformed;
    }

    TransformedDocument TransformOnce(
        SyntaxNode root,
        string source,
        CSharpParseOptions options,
        CancellationToken cancellationToken)
    {
        var structural = ApplyFileScopedRules(root);
        var text = ReferenceEquals(structural.Root, root) ? source : structural.Root.ToFullString();
        var emitted = Emit(structural.Root, text, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        if (!_embeddedStatements.NeedsBracePlanning)
            return EmitWithoutBracePlanning(root, text);

        var context = new RuleContext(root, _settings);
        var minimized = SyntaxRewritePlan.For(
            root,
            _memberRules,
            context,
            _embeddedStatements.NeedsBraceFreeCandidate
                ? member => EmbeddedStatementBraces.MinimizeMember(member, context)
                : null);
        var rewriteSkipped = context.TakeSkippedOccurrences();
        cancellationToken.ThrowIfCancellationRequested();

        // Whether a statement keeps its braces is settled against the layout of the file without
        // them, which the plan holds before anything is written. This plan is only read, and the
        // one the file is written from counts the occurrences it had to leave alone.
        var braceFree = _layoutPlanner.Plan(root, text, minimized, context);
        context.TakeSkippedOccurrences();
        var lines = SinglePassEmitter.ForReading(root, _emitterPlan, context, braceFree);
        var braced = minimized.Then(
            root,
            (member, segment) => EmbeddedStatementBraces.ApplyMember(
                member,
                segment,
                _embeddedStatements,
                context,
                lines));
        rewriteSkipped += context.TakeSkippedOccurrences();
        cancellationToken.ThrowIfCancellationRequested();

        var layout = ReferenceEquals(braced, minimized)
            ? braceFree
            : _layoutPlanner.Plan(root, text, braced, context);
        var formatted = SinglePassEmitter.Emit(root, _emitterPlan, context, text, layout);
        return new(
            formatted,
            rewriteSkipped + layout.SkippedOccurrences + context.TakeSkippedOccurrences());
    }

    EmittedDocument EmitWithoutBracePlanning(SyntaxNode root, string text)
    {
        // The rest are scoped to the member they change, so a member no rule wants
        // is never copied, and the file's tree is never rebuilt around one that is.
        var ruleContext = new RuleContext(root, _settings);
        var rewrites = SyntaxRewritePlan.For(root, _memberRules, ruleContext);
        var skipped = ruleContext.TakeSkippedOccurrences();
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
            skipped + layout.SkippedOccurrences + ruleContext.TakeSkippedOccurrences());
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
sealed record FormattedDocument(ReadOnlyMemory<byte> Content, int SkippedOccurrences, SourceEncoding Encoding);
