---
status: accepted
---

# Place embedded statements independently

DressSharp uses `dress_embedded_statement_placement = same_line | next_line` to place the embedded statements owned by `if`/`else`, `while`, `do`, `for`, `foreach`, `using`, `lock`, and `fixed`. It applies whether the embedded statement is braced or unbraced, controls only where the statement starts, and preserves `else if` as a chain continuation.

This specific preference wins over `csharp_new_line_before_open_brace` for a block used as one of these embedded statements. The standard preference continues to control other braces. Brace presence has its own preference across the same brace-optional embedded statements. This keeps statement placement independent from brace presence while making the overlap explicit.

`same_line` replaces boundary whitespace with one space. `next_line` replaces it with one configured line ending and the computed body indentation. Both remove excess spaces and blank lines. An occurrence with a comment or directive in that boundary remains unchanged.

The Familiar default is `next_line`.
