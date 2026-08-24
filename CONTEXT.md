# DressSharp

DressSharp formats C# according to explicit, independently selectable preferences without semantic information.

## Language

**Formatter**:
A tool that transforms C# using only syntax information. It never requests type or semantic information.

**Tool command**:
The `dotnet dress` command that invokes DressSharp. The product name remains **DressSharp** and is never rendered as a lowercase command name.

**Invocation directory**:
The process working directory from which `dotnet dress` is invoked. It is the default discovery root and the preferred base for displayed relative paths.

**Formatting rule**:
The smallest independently selectable transformation. One formatting rule owns one syntax surface and is controlled by exactly one formatting preference.

**Formatting preference**:
An EditorConfig key and value that selects a formatting rule's outcome. Missing or `unset` preferences select no outcome, so the rule makes no change.

**Default configuration**:
The complete, explicit set of formatting preferences written by `dresssharp init`. It is not an implicit formatter fallback.

**Managed configuration block**:
The marked EditorConfig section owned by `dresssharp init`. A configuration file may contain at most one valid managed configuration block.

**Safety class**:
A rule-catalog classification stating whether a rule changes only layout or intentionally transforms syntax, together with the test invariant used to validate its output.

**Rule catalog**:
The versioned reference of every built-in formatting rule, including its preference key, accepted values, owned syntax, safety class, and validation invariant.

**Transformation pipeline**:
The ordered application of enabled formatting rules to a file. Compatible layout rules may share preparation, a syntax walk, or emission, but conflicting claims resolve in catalog order. The completed pipeline must be idempotent.

**Catalog order**:
The fixed order in which formatting rules run within a file. Files may be processed concurrently, but rules within one file run sequentially.

**Layout mode**:
An explicit wrapping outcome for one construct: `always_single`, `auto`, or `always_multi`. `auto` uses the shared maximum line length; an unspecified layout preference makes no change.

**Parse context**:
The effective language version, preprocessor symbols, source kind, and documentation mode used to parse one file. It comes from the file's selected MSBuild project context or, for an unowned file, its implicit file-app project.

**Malformed region**:
A source span containing parser diagnostics, missing or skipped tokens, or disabled text. A formatting-rule occurrence that intersects a malformed region is unsafe and is skipped without preventing safe occurrences elsewhere in the file.

**Disabled text**:
Source excluded by the file's effective preprocessor symbols. DressSharp does not format it because it is not active syntax in that parse context. It remains byte-identical unless an explicit file-representation preference applies, in which case its decoded text remains identical.

**Attached comment**:
A comment and the syntax it accompanies. DressSharp may move them together, but does not implicitly rewrite or reflow the comment's content.

**File-representation preference**:
An explicit `charset`, `end_of_line`, or `insert_final_newline` setting that may change a file's byte representation without changing its C# syntax.

**Performance target**:
A tracked, non-blocking expectation measured on the product owner's documented machine. It guides optimization but is neither portable nor a release criterion.
