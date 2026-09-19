# Working on DressSharp

Terms are defined in `CONTEXT.md`; use them as written there.

## One look

A file is read once, planned, and written once. Output that a second run would move is a **fixpoint** failure. Repair it by finding the layout question that was put to the **source**, the file as it arrived, and putting it to the **plan**, the decisions already made for the file. The two differ exactly where this run moves a line.

- A break one rule is about to claim counts for every other rule as much as a break the source already holds, and every rule reads it from the same place.
- Measure a line from the column it will be written at; the source column stands in only where no preference will move the line.
- Record a decision when it is made. A record kept only while something measures vanishes exactly when nothing measures.

A layout change is done when the pinned corpus formats to a fixpoint under the default preferences, its output is byte-identical to before except where the change intends otherwise, and the commit message says how many files changed and why.

## Per-file cost

Cost is work per token and per member across a thousand files. Shapes that have paid for themselves:

- Visit in order and remember the neighbour instead of walking back to it.
- Scan for the kinds a walk cares about before walking; most members have none of them.
- Index by stream position, not by token.
- Answer the common case, a well-formed file with no comments in the way, before touching the data the rare case needs.
- Loop over trivia directly; structured trivia never changes the answer.

## Process cost

The runtime settings in the project file and the worker count are measured choices, each commented with its measurement. Re-measure before changing one.

## Measuring

Before claiming a speedup, run `.benchmarks/protocol-v1.md`. Report CPU time beside wall time, since a change can win one while losing the other, and report a change that removes work by construction as that, with the numbers as evidence rather than the claim.
