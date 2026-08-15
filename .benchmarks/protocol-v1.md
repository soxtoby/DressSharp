# Benchmark protocol v1

Restore repository tools with `dotnet tool restore`, then materialize the pinned 1,000-file corpus with `dotnet do materialize-corpus`. A source or lock-manifest change starts a new benchmark series.

Run `dotnet do benchmark`. The runner materializes the corpus when absent; otherwise it verifies the existing corpus against the lock manifest. It compares complete fresh-process `check` commands against the .NET 10.0.302 SDK's pinned `dotnet format`. It performs three untimed warmups and fifteen measured pairs, alternating tool order. Default production concurrency is primary; one worker and all logical processors are diagnostic.

For `format`, the immutable materialized corpus is copied into a fresh directory outside timing. Inputs are verified against the manifest. Every measured run is retained. Never delete outliers; mark interference and repeat the entire set.

Results are timestamped JSON conforming to `result-schema-v1.json`, with a Markdown summary, under gitignored `.benchmarks/results/`. Results and regressions are advisory. A median or p95 regression over 10% in three consecutive same-corpus runs flags investigation but never blocks release.

Public performance claims must state the machine, corpus version/hash, DressSharp and comparison versions, statistic, ratio, and date. “Faster” requires a lower same-machine median on every real-world source group. The 500 ms warm median target is owner-machine-only.

DressSharp benchmark diagnostics are enabled only through undocumented `DRESSSHARP_BENCHMARK_WORKERS` and `DRESSSHARP_BENCHMARK_TIMING` environment variables. They are not public CLI contracts.
