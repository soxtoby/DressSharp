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
The smallest independently selectable formatting decision. One formatting rule is controlled by exactly one formatting preference; compatible rules may contribute constraints to the same planned transformation.

**Formatting preference**:
An EditorConfig key and value that selects a formatting rule's outcome. Missing or `unset` preferences select no outcome, so the rule makes no change.

**Default preferences**:
The complete, explicit set of formatting preferences written by `dresssharp init`. It is not an implicit formatter fallback.
_Avoid_: Familiar preferences, Familiar preset

**Default preference initialization**:
Adding each missing supported preference to an EditorConfig file. Existing assignments in any section remain authoritative; missing preferences go into an existing `[*.cs]` section or a new one at the end.
_Avoid_: Managed configuration block

**Interactive configuration**:
A local browser workflow that edits supported preferences in one target EditorConfig and previews the pending preferences against transient C# source.
_Avoid_: Interactive configuration session, session

**CSRF token**:
An unguessable, per-launch value that the interactive browser sends in a request header when changing local state. It prevents unrelated browser content from invoking write operations on the local server.
_Avoid_: Session token, authentication token, loopback capability

**Target EditorConfig**:
The EditorConfig selected explicitly or discovered nearest to the invocation directory for an interactive configuration. The interactive configuration reads and writes only its `[*.cs]` sections.

**Exact C# section**:
An EditorConfig section whose parsed glob is exactly the ordinal, case-sensitive text `*.cs`. Surrounding whitespace is permitted; paths, brace expansions, lists, case variants, and trailing text are different or invalid sections.
_Avoid_: C# section

**Interactive root**:
The directory containing the target EditorConfig. It anchors inherited preference resolution, preview context, and the optional post-save file selection for an interactive configuration.

**Preview source**:
Transient C# supplied within an interactive configuration to demonstrate the pending preferences. DressSharp never saves it.
_Avoid_: Sample file

**Preview parse context**:
The fixed C# context for preview source: the latest stable language version supported by DressSharp, with no predefined preprocessor symbols. It is independent of any project's parse context.

**Preview diff**:
The difference between preview source and its formatted output under the pending preferences. It may reveal whitespace explicitly without changing either text.

**Rule example**:
A read-only, ephemeral illustration of one formatting rule and its outcomes. It explains the rule but does not replace or modify the preview source.

**Pending preferences**:
The effective formatting preferences represented by an interactive configuration's controls, including unsaved edits. DressSharp applies all of them to the preview source.

**Inherited preference**:
The authoritative upstream assignment state for one supported preference outside the target EditorConfig. It is absent, `unset`, or explicit; the interactive configuration displays it independently even when a local assignment masks it.

**Interactive effective preference**:
The outcome produced by exact `[*.cs]` sections from the target EditorConfig and its parent chain at the interactive root, including values derived by EditorConfig rules; `unset` produces no outcome. More-specific sections may override it for individual source files.

**Interactive configuration edit**:
A user-directed preference change tracked independently of the loaded EditorConfig text. On save, it overrides the corresponding target assignment in the latest file while preserving unrelated external edits.

**Local assignment**:
The target EditorConfig's authoritative assignment for one supported preference across exact `[*.cs]` sections. The last occurrence wins; it is absent only when no occurrence exists, otherwise it is `unset` or an explicit value, and may differ from the interactive effective preference.

**Rule catalog**:
The versioned reference of every supported formatting preference, including its values, Default, documentation, examples, and any formatting rule that implements it.

**Transformation pipeline**:
The ordered application of enabled formatting rules to a file. Compatible layout rules may share preparation, a syntax walk, or emission, but conflicting claims resolve in catalog order. The completed pipeline must be idempotent.

**Catalog order**:
The fixed order in which formatting rules run within a file. Files may be processed concurrently, but rules within one file run sequentially.

**Layout mode**:
An explicit wrapping outcome for one supported syntax shape: `always_single`, `auto`, or `always_multi`. `auto` uses the shared maximum line length; an unspecified layout preference makes no change.

**Statement header**:
The controlling syntax of a statement that owns an embedded statement, excluding the embedded statement itself. For a `do` statement, it includes the trailing `while` clause.
_Avoid_: Header, control header

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
