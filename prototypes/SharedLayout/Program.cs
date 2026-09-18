using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DressSharp.Architecture;
using DressSharp.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
if (!File.Exists(Path.Combine(repo, "CONTEXT.md")))
    throw new InvalidOperationException("Run from the default prototype build output.");
var configuration = new FormattingConfiguration(File.ReadAllLines(Path.Combine(repo, "tests/DressSharp.UnitTests/Fixtures/Default.editorconfig"))
    .Where(line => line.Contains('='))
    .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
    .Select(pair => new KeyValuePair<RuleKey, string>(RuleKeys.Parse(pair[0]), pair[1])));
var settings = RuleSettings.From(configuration);
var emitter = EmitterPlan.From(RuleCatalog.BuiltIn, configuration);
var members = MemberRuleSet.From(RuleCatalog.BuiltIn, configuration);
var planner = new EmissionLayoutPlanner(RuleCatalog.BuiltIn, configuration, settings, emitter);
var corpus = Path.Combine(repo, ".benchmarks/corpus/files");
var inputs = Directory.GetFiles(corpus, "*.cs", SearchOption.AllDirectories)
    .Where(path => !path.Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj"))
    .Order(StringComparer.Ordinal)
    .Select(path => new Input(Path.GetRelativePath(corpus, path), File.ReadAllText(path), CSharpParseOptions.Default)).ToArray();
var cases = new[]
{
    "class C { void M() { if (a) { A(); } else if (b) { B(); C(); } else D(); } }",
    "class C { void M() { if (a) { if (b) A(); } else B(); do Work(aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb); while (a && b); } }",
    "class C { void M() { if (a) { /* attached */ A(); } else { B(); } while (ready) ; for (;;) Work(); foreach (var x in xs) Work(); using (var x = Open()) Work(); lock (gate) Work(); fixed (int* p = data) Work(); } }",
    "class C { void M() { if (a) {\n#if ACTIVE\n Work();\n#else\n broken    untouched( ;\n#endif\n } if (b) Broken(,); } }",
    "class C { void M() { if (a) Log(\"\"\"\n    first\n    second\n    \"\"\"); else Stop(); } }",
    "class C { void M() { if (a\n && b) Work(); else Stop(); do Work(); while (a\n && b); } }",
    "class C { void M() { if (a) Log(@\"first\u0085second\u2028third\u2029last\"); else Stop(); } }",
    "class C { void M() { if (\n) Call(); else Stop(); } }",
    "class C { object x = new Widget {\n// attached\nCallback = () => { if (a) A(); }\n}; }"
};
inputs = inputs.Concat(cases.SelectMany((source, index) => new[] { "\n", "\r\n" }.SelectMany(ending => new[] { false, true }.Select(active =>
    new Input($"focused-{index}-{(ending == "\n" ? "lf" : "crlf")}-{active}", source.ReplaceLineEndings(ending),
        active ? CSharpParseOptions.Default.WithPreprocessorSymbols("ACTIVE") : CSharpParseOptions.Default))))).ToArray();
var parallel = new ParallelOptions { MaxDegreeOfParallelism = 11 };
if (args.Contains("--format-benchmark"))
    return FormatProbe.Run(inputs, configuration, args[Array.IndexOf(args, "--format-benchmark") + 1]);
if (args.Contains("--braces"))
    return BraceProbe.Run(inputs, Prepare, emitter, planner, settings, EmbeddedStatementSettings.From(configuration), repo, args.Contains("--benchmark"));
var failures = new ConcurrentQueue<object>();
var fallbacks = new ConcurrentQueue<object>();
long queryCount = 0;
long tokenCount = 0;
var watch = Stopwatch.StartNew();
Parallel.ForEach(inputs, parallel, input =>
{
    var baseline = Prepare(input);
    var expected = SinglePassEmitter.Emit(baseline.Root, emitter, baseline.Context, baseline.Text, baseline.Layout);
    var expectedSkipped = baseline.Skipped + baseline.Layout.SkippedOccurrences + baseline.Context.TakeSkippedOccurrences();
    var expectedQueries = ReferenceQueries(CSharpSyntaxTree.ParseText(expected, input.Options).GetRoot());
    var candidate = Prepare(input);
    var resolved = PrototypeTokenLayoutResolver.Resolve(candidate.Root, emitter, candidate.Context, candidate.Text, candidate.Layout);
    var plannedQueries = PlannedQueries(resolved, candidate.Layout.Stream);
    var actualQueries = resolved.HasCommentBoundaryHazard
        ? ReferenceQueries(CSharpSyntaxTree.ParseText(resolved.Render(), input.Options).GetRoot())
        : plannedQueries;
    if (resolved.HasCommentBoundaryHazard) fallbacks.Enqueue(new { input.Name, queriesDiffer = !plannedQueries.SequenceEqual(expectedQueries), plannedCount = plannedQueries.Length, parsedCount = expectedQueries.Length });
    var actualSkipped = candidate.Skipped + candidate.Layout.SkippedOccurrences + candidate.Context.TakeSkippedOccurrences();
    Interlocked.Add(ref tokenCount, resolved.Pieces.Length);
    Interlocked.Add(ref queryCount, expectedQueries.Length);
    if (resolved.Render() != expected || expectedSkipped != actualSkipped || !expectedQueries.SequenceEqual(actualQueries))
    {
        File.WriteAllText(Path.Combine(repo, ".benchmarks/results/shared-layout/failure-" + Path.GetFileName(input.Name)), expected);
        failures.Enqueue(new { input.Name, textMatches = resolved.Render() == expected, expectedSkipped, actualSkipped,
            expectedCount = expectedQueries.Length, actualCount = actualQueries.Length,
            mismatches = expectedQueries.Zip(actualQueries).Where(pair => pair.First != pair.Second).Take(8).ToArray() });
    }
});
var verification = new { files = inputs.Length, tokens = tokenCount, queries = queryCount, fallbacks = fallbacks.ToArray(), failures = failures.ToArray(), milliseconds = watch.Elapsed.TotalMilliseconds };
var outputRoot = Path.Combine(repo, ".benchmarks/results/shared-layout");
Directory.CreateDirectory(outputRoot);
File.WriteAllText(Path.Combine(outputRoot, "verification.json"), JsonSerializer.Serialize(verification, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(verification));
if (!failures.IsEmpty) return 1;

if (args.Contains("--benchmark"))
{
    var results = new List<object>();
    for (var round = -1; round < 5; round++)
    {
        var order = round % 2 == 0 ? new[] { "emitted-query", "planned-query", "planned-render" } : new[] { "planned-render", "planned-query", "emitted-query" };
        foreach (var mode in order)
        {
            var allocated = GC.GetTotalAllocatedBytes(true);
            var cpu = Process.GetCurrentProcess().TotalProcessorTime;
            watch.Restart();
            long count = 0;
            Parallel.ForEach(inputs, parallel, input =>
            {
                var prepared = Prepare(input);
                Query[] queries;
                if (mode == "emitted-query")
                {
                    var text = SinglePassEmitter.Emit(prepared.Root, emitter, prepared.Context, prepared.Text, prepared.Layout);
                    queries = ReferenceQueries(CSharpSyntaxTree.ParseText(text, input.Options).GetRoot());
                }
                else
                {
                    var resolved = PrototypeTokenLayoutResolver.Resolve(prepared.Root, emitter, prepared.Context, prepared.Text, prepared.Layout);
                    var rendered = resolved.HasCommentBoundaryHazard ? resolved.Render() : null;
                    queries = resolved.HasCommentBoundaryHazard
                        ? ReferenceQueries(CSharpSyntaxTree.ParseText(rendered!, input.Options).GetRoot())
                        : PlannedQueries(resolved, prepared.Layout.Stream);
                    if (mode == "planned-render" && rendered is null) GC.KeepAlive(resolved.Render());
                }
                Interlocked.Add(ref count, queries.Length);
            });
            var result = new { mode, round, milliseconds = watch.Elapsed.TotalMilliseconds,
                cpuMilliseconds = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, queries = count };
            results.Add(result);
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
    File.WriteAllText(Path.Combine(outputRoot, "benchmark.json"), JsonSerializer.Serialize(results));
}
return 0;

Prepared Prepare(Input input)
{
    var root = CSharpSyntaxTree.ParseText(input.Source, input.Options).GetRoot();
    var structural = FileScopedStructuralRules.Transform(root, RuleCatalog.BuiltIn, configuration);
    if (!structural.Succeeded) throw structural.Failure;
    var text = ReferenceEquals(root, structural.Root) ? input.Source : structural.Root.ToFullString();
    root = structural.Root;
    var context = new RuleContext(root, settings);
    var rewrites = SyntaxRewritePlan.For(root, members, context, member => EmbeddedStatementBraces.MinimizeMember(member, context));
    var skipped = structural.SkippedOccurrences + context.TakeSkippedOccurrences();
    return new(root, text, context, planner.Plan(root, text, rewrites, context), skipped, rewrites);
}

static Query[] PlannedQueries(ResolvedTokenLayout resolved, EffectiveTokenStream stream)
{
    var queries = new List<Query>();
    for (var index = 0; index < resolved.Pieces.Length; index++)
    {
        var piece = resolved.Pieces[index];
        if (Parts(piece.Token.Parent) is not { } parts || piece.Token != parts.Keyword) continue;
        int Index(SyntaxToken token) => stream.IndexOf(token, piece.SegmentIndex);
        bool Multi(SyntaxNode node) => resolved.IsMultiline(Index(node.GetFirstToken(includeZeroWidth: true)), Index(node.GetLastToken(includeZeroWidth: true)));
        var body = Unwrap(parts.Body);
        queries.Add(new(resolved.Extent(index).Start, piece.Token.Parent!.RawKind,
            resolved.IsMultiline(Index(parts.First), Index(parts.Last)), body is not null && Multi(body),
            parts.Body is BlockSyntax { Statements.Count: > 1 }));
    }
    return queries.ToArray();
}

static Query[] ReferenceQueries(SyntaxNode root) => root.DescendantNodes().Select(Parts).OfType<StatementParts>().Select(parts =>
{
    var body = Unwrap(parts.Body);
    return new Query(parts.Keyword.SpanStart, parts.Keyword.Parent!.RawKind,
        parts.First.GetLocation().GetLineSpan().StartLinePosition.Line != parts.Last.GetLocation().GetLineSpan().EndLinePosition.Line,
        body is not null && body.GetLocation().GetLineSpan() is var lines && lines.StartLinePosition.Line != lines.EndLinePosition.Line,
        parts.Body is BlockSyntax { Statements.Count: > 1 });
}).ToArray();

static StatementSyntax? Unwrap(StatementSyntax body) => body switch
{
    BlockSyntax { Statements.Count: 0 } => null,
    BlockSyntax { Statements: [var only] } => only,
    _ => body
};

static StatementParts? Parts(SyntaxNode? node) => node switch
{
    IfStatementSyntax n => new(n.IfKeyword, n.IfKeyword, n.CloseParenToken, n.Statement),
    ElseClauseSyntax n => new(n.ElseKeyword, n.ElseKeyword, n.ElseKeyword, n.Statement),
    WhileStatementSyntax n => new(n.WhileKeyword, n.WhileKeyword, n.CloseParenToken, n.Statement),
    DoStatementSyntax n => new(n.DoKeyword, n.WhileKeyword, n.CloseParenToken, n.Statement),
    ForStatementSyntax n => new(n.ForKeyword, n.ForKeyword, n.CloseParenToken, n.Statement),
    CommonForEachStatementSyntax n => new(n.ForEachKeyword, n.ForEachKeyword, n.CloseParenToken, n.Statement),
    UsingStatementSyntax n => new(n.UsingKeyword, n.UsingKeyword, n.CloseParenToken, n.Statement),
    LockStatementSyntax n => new(n.LockKeyword, n.LockKeyword, n.CloseParenToken, n.Statement),
    FixedStatementSyntax n => new(n.FixedKeyword, n.FixedKeyword, n.CloseParenToken, n.Statement),
    _ => null
};

sealed record Input(string Name, string Source, CSharpParseOptions Options);
sealed record Prepared(SyntaxNode Root, string Text, RuleContext Context, EmissionLayoutPlan Layout, int Skipped, SyntaxRewritePlan Rewrites);
sealed record StatementParts(SyntaxToken Keyword, SyntaxToken First, SyntaxToken Last, StatementSyntax Body);
sealed record Query(int Position, int Kind, bool HeaderMultiline, bool BodyMultiline, bool MultipleStatements);
