# Shared layout prototype — throwaway

Question: can brace body/header multiline queries use a resolved layout directly, without first writing and reparsing a complete candidate document?

**Yes, for the measured normal path.** The prototype separates decision-making from output, answers multiline queries from token extents, and renders retained source slices only when requested. A second experiment now applies brace decisions and replans dependent layout through a syntax-projection bridge. That bridge validates behavior but costs more CPU and memory; it is not a proposed production fast path. Wrapping stabilization is not replaced. Production code and runtime defaults are unchanged.

## Run

Requires .NET 10, PowerShell, and the repository's pinned benchmark corpus.

```powershell
pwsh -NoProfile -File prototypes/SharedLayout/Run.ps1 -Benchmark
```

Omit `-Benchmark` for the parity check alone. In an environment where the existing performance harness is already restored, avoid another restore with `-AssetsFile artifacts/perf/obj/project.assets.json`. This was the validation path used here. The script verifies corpus hashes and uses disabled tiering for the local comparison; it restores the caller's environment afterward.

Add `-Braces` to run the extended candidate-plus-brace experiment, optionally with `-Benchmark`.

## Shape

- `Generate.ps1` derives a temporary resolver from the current `SinglePassEmitter`. It changes the output sink and token capture only, failing if expected source anchors disappear. This avoids maintaining a handwritten second set of formatting rules.
- `PlannedText.cs` records source slices and token start/end lines and positions. Adjacent source slices coalesce; changed token text remains explicit. CRLF spanning fragments and Unicode line separators are counted.
- `ResolvedTokenLayout.IsMultiline` answers from those extents. `Render` only copies fragments into the final string; it makes no syntax decisions.
- `BraceProbe.cs` takes its reference brace phase from the production flow: braces are settled against the layout of the candidate without them, read from that plan, and layout is planned again only when a member changed. The generated resolver's output sink can be cut back within a comment gap, as the emitter now does when it moves a comment.
- `Program.cs` runs the same file/member rewrites and layout preparation for both paths. It compares candidate text, skipped-occurrence counts, and body/header multiline queries against a freshly parsed reference candidate. These are candidate-stage checks, not full final brace-policy or complete formatter checks.

Generated code and evidence go under `.benchmarks/results/shared-layout/`. The prototype is deliberately excluded from the production solution.

## Result, 2026-09-13

At baseline `044ba99`, 1,000 corpus files plus 36 focused LF/CRLF and preprocessor variants passed parity: 563,994 effective tokens and 5,161 reference statement query records. Focused cases include nested/dangling conditionals, balanced-chain inputs, multiline `do` headers, empty bodies, comments, directives/disabled text, raw/verbatim strings, Unicode line separators and malformed syntax. Each record compares both body and header multiline status and the multiple-statement condition.

Seven corpus files and four focused variants triggered the conservative comment-boundary fallback. Queries actually diverged before fallback in one corpus file and its four minimized variants. This fallback is explicit; those inputs still render and parse to obtain the old answer.

Five measured alternating rounds after verification and one warmup round, 11 workers, tiering disabled, warm in-process execution:

| Candidate-stage workload | Median wall | Median allocated bytes |
| --- | ---: | ---: |
| Current emission + parse + queries | 607 ms | 650,966,696 |
| Plan + direct queries, including fallbacks | 535 ms | 593,352,120 |
| Plan + queries + final render | 527 ms | 618,086,296 |

Direct queries used 8.9% fewer allocated bytes and took 11.9% less wall time in this local run. Wall timing is noisy: the render variant being faster than queries alone is not evidence that rendering is free. These numbers include initial parsing and candidate layout preparation, but exclude CLI startup/discovery, MSBuild/EditorConfig resolution, final brace application, stabilization, encoding and persistence. They do **not** establish a one-second full CLI result.

Raw evidence: [verification](../../.benchmarks/results/shared-layout/verification.json), [benchmark](../../.benchmarks/results/shared-layout/benchmark.json). An earlier, less compact fragment representation is retained in `benchmark-fragments.json`; use the final benchmark above for current results.

## Existing blocker exposed by the comparison

In `Polly/samples/Retries/Program.cs`, current candidate emission can join initializer code onto a preceding `//` comment. Reparsing then loses an embedded statement that still exists in the effective token stream. A minimized input is included in the focused cases:

```csharp
class C { object x = new Widget {
// attached
Callback = () => { if (a) A(); }
}; }
```

The prototype detects a planned nonempty token after an unterminated line comment and falls back. The guard is conservative and covers this observed hazard, not every possible lexical change. The production bug is deliberately not copied into direct layout semantics or silently fixed by this experiment. It needs a separate exact-output regression and correction before removing the fallback.

## Decision and next implementation slice

Keep the separation: resolved layout supplies multiline facts and the final writer has no decisions. Replace the generated resolver with a refactoring of the real implementation only when integrating it; do not ship the generated copy.

The next section records the subsequent brace experiment. Incremental dependency propagation and convergence remain unimplemented. Eliminating the remaining full passes is where the larger architectural gain must come from.

## Extended brace experiment

`BracePlan.cs` computes balanced-chain brace decisions from planned token extents, records insertions, and applies them to affected members. `BraceProbe.cs` compares the resulting brace phase against the existing emit/parse/apply/layout path. The prototype supports the default balanced/next-line brace configuration; it does not generalize all settings yet.

Three alternatives were tested:

1. Insert delimiters into resolved text without replanning: **60 mismatches**.
2. Apply brace edits to the effective syntax and rerun layout using original source facts: **63 mismatches**.
3. Project planned trivia onto the existing syntax, apply planned brace edits, then replan: **zero mismatches on eligible inputs**.

The third path uses `PlannedSyntax.cs`: syntax structure is retained, token trivia is reconstructed, and changed literal tokens are retokenized individually. There is no full-document candidate parse in this path. `ParseTrailingTrivia` retains Roslyn's trivia ownership at the first newline. Repeated gaps share parsed trivia within one projection. Generated-block annotations are removed to match the old candidate parse.

The projection is an explicit compatibility bridge. Existing rules still read line spans, source indentation and parent syntax from Roslyn nodes. Supplying the right multiline booleans alone does not update these other dependencies. The bridge materializes candidate text and rebuilds syntax trivia; it is not the final parse-once/emit-once architecture.

Validation: **789 eligible inputs**, 507 brace pairs added across 129 inputs, exact brace-phase text and skipped counts matched, and projected syntax was equivalent to the reference parse. The first body token's indentation never changed in these cases, yet continuation anchors and wrapping did: a first-token indent check is insufficient. Nineteen mismatches in the delimiter-only control occurred even without brace edits, demonstrating extra normalization in the current second pass.

**247 inputs are excluded from this extended comparison**, not counted as passing through a fallback: 226 have directives, 10 malformed syntax, and 11 comment-boundary hazards. The earlier query-only check still covers all 1,036 inputs with its documented fallbacks. Full formatter output, all preference combinations, stabilization and idempotence are not established by this brace-phase experiment.

Five measured alternating pairs after a warmup pair, same 789 inputs, 11 workers, tiering disabled; initial parse, candidate preparation and the brace phase included in both arms:

| Candidate + brace workload | Median wall | Median CPU | Median allocated bytes |
| --- | ---: | ---: | ---: |
| Existing emit/reparse path | 983 ms | 5,969 ms | 698,501,440 |
| Planned decisions + syntax projection | 960 ms | 7,219 ms | 1,044,180,552 |

Wall time is effectively tied amid machine variability. Projection consumed **21% more CPU and 49% more allocations**. Reusing gap trivia improved the initial projection prototype's allocation cost, but did not make it competitive. Do not promote this bridge as an optimization.

Evidence: [extended verification](../../.benchmarks/results/shared-layout/braces/verification.json), [extended benchmark](../../.benchmarks/results/shared-layout/braces/benchmark.json). Counterexample source/output files in that directory are retained from the earlier failed alternatives; the verification JSON describes the final path.

**Architectural conclusion:** planned source positions and indentation anchors must become first-class inputs to the actual wrapping/indentation consumers. Projecting them back into Roslyn merely replaces reparsing with expensive rebuilding. The next implementation should introduce those plan-backed queries at the consumers, use this projection as a temporary differential oracle, and remove the bridge once parity holds. Then recompute only groups affected by brace changes and resolve convergence; adding more text-level caches does not address this dependency.

## First consumer migration: indentation facts

`LayoutFacts` now supplies the indentation model's physical start lines, leading indentation,
and raw-string token text. Production uses syntax-backed facts by default. The prototype
injects resolved facts for continuation rebasing, preserved argument continuation, arrow
placement, and raw-string margins. This is a preparatory production refactor, not an enabled
production optimization. Wrapping's visual-column scans and trivia consumers still need migration.

The differential verifier checks **1,060,989 facts** using original effective tokens against
the positioned syntax projection. The brace phase then uses the same planned facts through
the real indentation model: **789 eligible inputs, zero output/skipped-count mismatches**.
During that phase, 14,020 reads use planned facts; 5,238 reads on newly rewritten member tokens
still use projected syntax. Those fallbacks are counted explicitly. The 247 exclusions above
remain unchanged. Production validation: **400 relevant unit tests passed**, including
indentation, wrapping, embedded statements, document formatting and rule examples.

An initial all-token lookup dictionary added roughly 50 MB to the phase workload. The measured
adapter now reuses the effective stream's binary-search index instead; only the independent
original-token verifier builds a dictionary, outside benchmark timing. Planned leading indents
are cached per physical line and recognize all Roslyn newline characters. No lookup is made
for ordinary token text in the emitter hot path.

The new three-arm comparison is recorded in
[`benchmark-facts.json`](../../.benchmarks/results/shared-layout/braces/benchmark-facts.json):
existing emission/reparse, projection with syntax facts, and projection with planned facts.
The earlier dictionary arm is retained in `benchmark-facts-dictionary.json`. Wall time varied
substantially during this run; no end-to-end speedup is established. Projection remains in both
prototype arms and still costs much more allocation than the existing path.

Next: retain token correspondence through member rewrites, migrate wrapping's source-position
and trivia reads, then remove projection. The separate initializer comment/newline defect
remains unresolved; its guard cannot yet be removed.

### Rewrite correspondence and wrapping ancestry

The subsequent slice maps rewritten member tokens back to their resolved token indices,
skipping only inserted, annotated brace tokens. Token kind/text/order and complete consumption
are checked. A forward cursor visits original tokens once across ordered replacements;
unchanged members reuse the existing stream index. Only rewritten tokens get an additional map.

Wrapping's same-line ancestor decisions now use `LayoutFacts` too. Exact brace-phase output
and skipped counts still match on all **789 eligible inputs**. Migrated consumers make
**29,546 planned reads and zero syntax fallbacks**; the verifier now fails on any such fallback.
The independent **1,060,989 fact comparisons** and **400 relevant unit tests** still pass.
This establishes parity for these consumers on the eligible inputs, not equivalence of every
rewritten physical position: inserted braces can themselves change physical lines.

The latest `benchmark-facts.json` includes this mapping; the previous run is retained as
`benchmark-facts-before-rewrite-mapping.json`. The projection remains required by visual-column
scans, trivia layout and emission. No full CLI speedup or removal of candidate projection is
claimed. The next substantial slice is supplying effective gaps and line-prefix measurements
directly to those consumers; mapping indentation alone cannot eliminate the projection.

### Visual-column scan correction

Inspection found two independent backward searches in `VisualStartColumn`'s line-start helper:
one for CR, one for LF. On LF-only input, the CR search traverses the entire preceding source
for every lookup, even when LF is nearby. The helper now searches for either character in one
span operation. This preserves CR/LF behavior, including positions between CR and LF, without
adding an index or cache. It does not yet migrate visual-column reads to the resolved plan.

`FormatProbe.cs` adds `--format-benchmark <output.json>`: initial parse and complete
`FormatSyntax` (including stabilization), plus output hashing, on all 1,036 inputs with 11
workers. It excludes CLI startup/discovery, file reading, encoding and writes. A frozen assembly
from immediately before this one-line correction and the rebuilt assembly ran in before/after,
then after/before order, each with one warmup and three measured rounds, tiering disabled.
**All 1,036 final output hashes matched across all four runs.** The 400 relevant unit tests and
789-input brace comparison also passed.

Median wall was 1,938 ms before / 1,710 ms after, but median CPU was 14,000 / 15,180 ms and wall
times varied substantially. These results **do not establish a speedup**. The change removes
an unnecessary scan by construction; a quieter full CLI measurement is still needed.
Evidence: `format-prefix-before-1.json`, `format-prefix-after-1.json`,
`format-prefix-after-2.json`, `format-prefix-before-2.json` under the shared-layout results directory.
