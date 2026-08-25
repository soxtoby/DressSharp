---
status: accepted
---

# Derive embedded-statement braces from planned layout

DressSharp applies brace policy to every brace-optional embedded statement. `compact` braces a body whose planned brace-free layout spans multiple lines and otherwise removes braces where syntax and trivia permit, even when that produces a mixed conditional chain. Existing braces and the body-placement newline do not make a body multiline, but line breaks inside a nested embedded statement do. `balanced` uses the same test but keeps a complete `if`/`else if`/`else` chain uniform; outside a conditional chain it behaves like `compact`. `always` braces every body.

Malformed syntax, directives, attached comments, and dangling-`else` association may require existing braces to remain. Multiline statement-header handling is a separate formatting rule that contributes another constraint to the shared brace plan, allowing it to affect both `compact` and `balanced` without multiplying their modes. The trailing `while` condition of a `do` statement participates despite following the body. Under `compact`, a multiline statement header constrains only its own body; under `balanced`, any multiline statement header constrains the complete conditional chain. The plan uses final formatted layout so one formatter pass remains idempotent. Changing `max_line_length` or another wrapping preference may therefore change the resulting braces.

The preferences are `dress_embedded_statement_braces = compact | balanced | always` and `dress_braces_for_multiline_statement_header = true | false`. Default uses `balanced` and `true`. These replace the unreleased `dress_conditional_braces` preference without a compatibility alias.

Empty bodies are canonical rather than preserved: `compact` and `balanced` use an empty statement (`;`), while `always` uses an empty block (`{ }`), subject to the same trivia safety rules.
