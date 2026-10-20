---
status: accepted
---

# Place braces with dress rules instead of the standard key

DressSharp reads the standard `csharp_*` and `dotnet_*` preferences as Visual Studio does, so a file that already sets them works unchanged. Two of them, as set by the Default preferences, carried a meaning Visual Studio and Rider do not share. `csharp_new_line_before_open_brace` omitted `object_collection_array_initializers`, because the `dress_*_initializer_layout` preferences decide where an initializer brace goes, but other tools read the omission as "same line". `csharp_preserve_single_line_blocks = false` was softened by `dress_preserve_trivial_single_line_blocks = true`, but other tools read it as written and split `{ get; set; }`. Rider acted on both, moved what DressSharp had placed, and DressSharp's `auto` layouts then kept a moved brace as the author's choice.

Most of what the standard key's twelve categories cover already has a more specific DressSharp rule: initializer layouts, embedded statement placement, the brace after a wrapped parameter list, and trivial single-line blocks. What remains is one question, same line or next line, for three families of syntax. `dress_declaration_open_brace_position` places the brace of a namespace, type, member, accessor, or local function; `dress_statement_open_brace_position` the brace of a statement block no embedded-statement placement decided; `dress_lambda_open_brace_position` the brace of a lambda or anonymous method. Each is `same_line | next_line`, Default `next_line`. They also cover block namespaces and `switch` statements, which the standard key never did. `dress_single_line_blocks = preserve | expand | expand_unless_trivial` replaces `dress_preserve_trivial_single_line_blocks`, saying directly what the pair of standard key and softener said together.

The standard keys stay supported. `csharp_new_line_before_open_brace` places only the braces no dress rule decided, and `csharp_preserve_single_line_blocks` is read only while `dress_single_line_blocks` is unset. Default preferences set the dress rules and leave both standard keys out, so a tool that acts on them finds nothing. `dotnet dress init` adds the dress rules at their defaults even to a file that sets a standard key, as it does for any new rule. The old Default preferences and the new ones mean the same thing, so a file written by an earlier `init` keeps formatting as before once the dress rules are added, and the standard keys can then be removed.

Rejected: a `dress_` copy of `csharp_new_line_before_open_brace` with the same category list. It would ask for twelve syntax kinds to answer a two-way question, and several of the kinds are already decided by better rules.

Rejected: adding `object_collection_array_initializers` to the Default open-brace list. It hands the placement of initializer braces to other tools, and a brace they move to its own line is kept there by `auto` layout, which does not rejoin a list that fits. It also makes the open-brace rule claim the inner braces of element initializers, which have no layout preference of their own.

Rejected: making `auto` layouts rejoin a list that fits on one line. `auto` keeps the author's choice of a multi-line list, and rejoining would discard it.
