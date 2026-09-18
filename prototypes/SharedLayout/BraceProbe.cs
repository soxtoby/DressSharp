using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DressSharp.Rules;
using Microsoft.CodeAnalysis.CSharp;

static class BraceProbe
{
    // A rule set nothing is enabled in: the candidate has had its member rules applied already.
    static readonly MemberRuleSet NoRules = MemberRuleSet.From(RuleCatalog.BuiltIn, new DressSharp.Architecture.FormattingConfiguration([]));

    // The production brace flow: settle braces against the layout of the candidate without them,
    // reading lines from that plan rather than from a written file, and plan again only if a
    // member changed. Returns the plan the file is written from and the rewrite skips it counted.
    static (SyntaxRewritePlan Rewrites, EmissionLayoutPlan Layout, int RewriteSkipped) Braced(
        Microsoft.CodeAnalysis.SyntaxNode root, string text, RuleContext context,
        EmitterPlan emitter, EmissionLayoutPlanner planner, EmbeddedStatementSettings braces)
    {
        var minimized = SyntaxRewritePlan.For(root, NoRules, context);
        var braceFree = planner.Plan(root, text, minimized, context);
        context.TakeSkippedOccurrences();
        var lines = SinglePassEmitter.ForReading(root, emitter, context, braceFree);
        var rewrites = minimized.Then(root, (member, segment) => EmbeddedStatementBraces.ApplyMember(member, segment, braces, context, lines));
        var rewriteSkipped = context.TakeSkippedOccurrences();
        var layout = ReferenceEquals(rewrites, minimized) ? braceFree : planner.Plan(root, text, rewrites, context);
        return (rewrites, layout, rewriteSkipped);
    }

    internal static int Run(Input[] inputs, Func<Input, Prepared> prepare, EmitterPlan emitter,
        EmissionLayoutPlanner planner, RuleSettings settings, EmbeddedStatementSettings braces, string repo, bool benchmark)
    {
        var failures = new ConcurrentQueue<object>();
        var fallbacks = new ConcurrentQueue<string>();
        var exclusionReasons = new ConcurrentDictionary<string, int>();
        var output = Path.Combine(repo, ".benchmarks/results/shared-layout/braces");
        Directory.CreateDirectory(output);
        var inserted = 0;
        var changedFiles = 0;
        var bodyStartIndentChanges = 0;
        var overlayFailures = 0;
        long plannedReads = 0, syntaxReads = 0, verifiedFacts = 0;
        Parallel.ForEach(inputs, new ParallelOptions { MaxDegreeOfParallelism = 11 }, input =>
        {
            var prepared = prepare(input);
            var resolved = PrototypeTokenLayoutResolver.Resolve(prepared.Root, emitter, prepared.Context, prepared.Text, prepared.Layout);
            if (resolved.HasCommentBoundaryHazard || prepared.Root.ContainsDiagnostics || prepared.Root.ContainsDirectives)
            {
                fallbacks.Enqueue(input.Name);
                var reason = resolved.HasCommentBoundaryHazard ? "comment boundary" : prepared.Root.ContainsDiagnostics ? "malformed syntax" : "directives";
                exclusionReasons.AddOrUpdate(reason, 1, (_, count) => count + 1);
                return;
            }
            var projectedRoot = PlannedSyntax.Project(prepared.Root, prepared.Rewrites, resolved, input.Options);
            var candidateText = resolved.Render();
            if (projectedRoot.ToFullString() != candidateText) throw new InvalidOperationException("Projected text changed: " + input.Name);
            var updatedContext = new RuleContext(projectedRoot, settings);
            var emptyRewrites = SyntaxRewritePlan.For(projectedRoot, NoRules, updatedContext);
            var projectedStream = EffectiveTokenStream.For(projectedRoot, candidateText, emptyRewrites);
            // Validate values against the independently positioned projection while querying
            // original effective tokens, whose source positions differ from planned positions.
            var originalFacts = new PlannedLayoutFacts(resolved, resolved.Pieces.Select(piece => piece.Token));
            for (var index = 0; index < projectedStream.Pieces.Length; index++)
            {
                var original = resolved.Pieces[index].Token;
                var projected = projectedStream.Pieces[index].Token;
                if (originalFacts.StartLine(original) != LayoutFacts.Syntax.StartLine(projected)
                    || originalFacts.LeadingIndent(original) != LayoutFacts.Syntax.LeadingIndent(projected)
                    || originalFacts.TokenText(original) != projected.Text)
                    throw new InvalidOperationException("Planned layout fact mismatch: " + input.Name + " token " + index);
            }
            Interlocked.Add(ref verifiedFacts, projectedStream.Pieces.Length * 3L);
            var facts = new PlannedLayoutFacts(resolved, projectedStream);
            var edits = new BracePlan(resolved.Rebind(projectedStream), projectedStream, updatedContext);
            edits.Solve();
            Interlocked.Add(ref inserted, edits.Count);
            Interlocked.Add(ref bodyStartIndentChanges, edits.BodyStartIndentChanges);
            if (edits.Count > 0) Interlocked.Increment(ref changedFiles);
            var overlay = edits.RenderOverlay();
            var updatedRewrites = SyntaxRewritePlan.For(projectedRoot, NoRules, updatedContext, edits.RewriteMember);
            var actualRewriteSkipped = updatedContext.TakeSkippedOccurrences();
            facts.RegisterRewrites(updatedRewrites);
            var updatedLayout = planner.Plan(projectedRoot, candidateText, updatedRewrites, updatedContext, facts);
            var actual = PrototypeTokenLayoutResolver.Resolve(projectedRoot, emitter, updatedContext, candidateText, updatedLayout).Render();
            var actualSkipped = actualRewriteSkipped + updatedLayout.SkippedOccurrences + updatedContext.TakeSkippedOccurrences();
            Interlocked.Add(ref plannedReads, facts.PlannedReads);
            Interlocked.Add(ref syntaxReads, facts.SyntaxReads);
            if (facts.SyntaxReads != 0) throw new InvalidOperationException("Unmapped layout fact: " + input.Name);

            // Oracle only: the proposed path above never parses its candidate text.
            var candidateRoot = CSharpSyntaxTree.ParseText(candidateText, input.Options).GetRoot();
            var context = new RuleContext(candidateRoot, settings);
            var (_, layout, expectedRewriteSkipped) = Braced(candidateRoot, candidateText, context, emitter, planner, braces);
            var expected = SinglePassEmitter.Emit(candidateRoot, emitter, context, candidateText, layout);
            var expectedSkipped = expectedRewriteSkipped + layout.SkippedOccurrences + context.TakeSkippedOccurrences();
            if (overlay != expected) Interlocked.Increment(ref overlayFailures);
            if (actual == expected && actualSkipped == expectedSkipped && projectedRoot.IsEquivalentTo(candidateRoot)) return;
            var first = 0;
            while (first < Math.Min(actual.Length, expected.Length) && actual[first] == expected[first]) first++;
            var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input.Name)))[..12];
            File.WriteAllText(Path.Combine(output, name + "-expected.cs"), expected);
            File.WriteAllText(Path.Combine(output, name + "-actual.cs"), actual);
            File.WriteAllText(Path.Combine(output, name + "-input.cs"), input.Source);
            failures.Enqueue(new { input.Name, artifact = name, edits = edits.Count, edits.BodyStartIndentChanges, first,
                actualSkipped, expectedSkipped, equivalentSyntax = projectedRoot.IsEquivalentTo(candidateRoot),
                expected = expected.Substring(Math.Max(0, first - 30), Math.Min(180, expected.Length - Math.Max(0, first - 30))),
                actual = actual.Substring(Math.Max(0, first - 30), Math.Min(180, actual.Length - Math.Max(0, first - 30))) });
        });
        var summary = new { files = inputs.Length, eligible = inputs.Length - fallbacks.Count, changedFiles, inserted, bodyStartIndentChanges, overlayFailures, verifiedFacts, plannedReads, syntaxReads, exclusionReasons, excluded = fallbacks.Order().ToArray(), failures = failures.ToArray() };
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { summary.files, summary.eligible, changedFiles, inserted, bodyStartIndentChanges, overlayFailures, verifiedFacts, plannedReads, syntaxReads, exclusionReasons, excluded = fallbacks.Count, failures = failures.Count }));
        if (!failures.IsEmpty) return 1;
        if (!benchmark) return 0;

        // Compare the complete candidate + brace phase, not just the decision loop. Unsupported
        // inputs are omitted from both arms. No oracle comparison is inside the measured loop.
        var excluded = fallbacks.ToHashSet();
        var measured = inputs.Where(input => !excluded.Contains(input.Name)).ToArray();
        var results = new List<object>();
        for (var round = -1; round < 5; round++)
        {
            foreach (var mode in round % 2 == 0 ? new[] { "existing", "projected", "planned-facts" } : new[] { "planned-facts", "projected", "existing" })
            {
                var allocated = GC.GetTotalAllocatedBytes(true);
                var cpu = Process.GetCurrentProcess().TotalProcessorTime;
                var watch = Stopwatch.StartNew();
                Parallel.ForEach(measured, new ParallelOptions { MaxDegreeOfParallelism = 11 }, input =>
                {
                    var prepared = prepare(input);
                    Microsoft.CodeAnalysis.SyntaxNode root;
                    SyntaxRewritePlan rewrites;
                    RuleContext context;
                    string source;
                    LayoutFacts? facts = null;
                    EmissionLayoutPlan? layout = null;
                    if (mode == "existing")
                    {
                        source = SinglePassEmitter.Emit(prepared.Root, emitter, prepared.Context, prepared.Text, prepared.Layout);
                        root = CSharpSyntaxTree.ParseText(source, input.Options).GetRoot();
                        context = new(root, settings);
                        (rewrites, layout, _) = Braced(root, source, context, emitter, planner, braces);
                    }
                    else
                    {
                        var resolved = PrototypeTokenLayoutResolver.Resolve(prepared.Root, emitter, prepared.Context, prepared.Text, prepared.Layout);
                        root = PlannedSyntax.Project(prepared.Root, prepared.Rewrites, resolved, input.Options);
                        source = resolved.Render();
                        context = new(root, settings);
                        var empty = SyntaxRewritePlan.For(root, NoRules, context);
                        var stream = EffectiveTokenStream.For(root, source, empty);
                        if (mode == "planned-facts")
                            facts = new PlannedLayoutFacts(resolved, stream);
                        var edits = new BracePlan(resolved.Rebind(stream), stream, context);
                        edits.Solve();
                        rewrites = SyntaxRewritePlan.For(root, NoRules, context, edits.RewriteMember);
                        if (facts is PlannedLayoutFacts plannedFacts) plannedFacts.RegisterRewrites(rewrites);
                    }
                    layout ??= planner.Plan(root, source, rewrites, context, facts);
                    var result = mode == "existing"
                        ? SinglePassEmitter.Emit(root, emitter, context, source, layout)
                        : PrototypeTokenLayoutResolver.Resolve(root, emitter, context, source, layout).Render();
                    GC.KeepAlive(result);
                });
                var row = new { mode, round, files = measured.Length, milliseconds = watch.Elapsed.TotalMilliseconds,
                    cpuMilliseconds = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
                    allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated };
                results.Add(row);
                Console.WriteLine(JsonSerializer.Serialize(row));
            }
        }
        File.WriteAllText(Path.Combine(output, "benchmark-facts.json"), JsonSerializer.Serialize(results));
        return 0;
    }
}
