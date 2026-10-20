# Changelog

## Unreleased

### Added
- `dress_declaration_open_brace_position`, `dress_statement_open_brace_position`, and `dress_lambda_open_brace_position` place opening braces by what owns them, `same_line` or `next_line`, in place of the category list of `csharp_new_line_before_open_brace`. They also place the braces of block namespaces and `switch` statements, which the standard key never covered.
- `dress_single_line_blocks = preserve | expand | expand_unless_trivial` says what happens to a block written on one line, in place of `csharp_preserve_single_line_blocks` and `dress_preserve_trivial_single_line_blocks` together.

### Changed
- `dotnet dress init` no longer writes `csharp_new_line_before_open_brace` or `csharp_preserve_single_line_blocks`, so Visual Studio and Rider stop acting on DressSharp's brace and single-line block settings. Both keys still work where set, but only where no `dress_` rule decided. `init` adds the new rules at their defaults, which match the old defaults, so remove the two standard keys once it has run.
- `dress_preserve_trivial_single_line_blocks` is no longer read. `dress_single_line_blocks = expand_unless_trivial`, which `init` adds, gives the same result.

### Fixed
- A multi-line collection initializer of `{ key, value }` elements, such as a dictionary, puts one element per line like any other wrapped initializer, instead of leaving them all on one line.
- With `dress_base_type_lists_layout = auto`, a base type list that fits on the line after a wrapped colon, such as `: IFoo, IBar`, stays on one line instead of taking a line per type, and one already broken after the colon no longer joins into a line over the limit.
- Comments inside a wrapped object initializer or argument list keep their own lines and indent with the item that follows. They could be merged onto one line, pulled onto the item's line, or left one level out with `dress_comment_align = false`.
- A comment just before a closing bracket or brace keeps the space between them.

## v0.4.0

### Changed
- `dress_namespace_style = file_scoped` converts only files with a single namespace, since a file-scoped namespace cannot share a file with a block namespace.
- `dress_lambda_body = expression` converts only a lambda block holding a single `return` statement, since that is the form whose expression body binds to the same overload.

### Fixed
- `csharp_preferred_modifier_order` keeps a member's comments and indentation where they were when it reorders modifiers, instead of carrying them along with the moved keyword and leaving the member uncompilable.

## v0.3.0

### Changed
- Using the SDK's MSBuild loaded into the formatter for faster formatting across lots of projects.
- A member access chain that wraps keeps its first call on the receiver's line when the receiver is no wider than the continuation indent, such as `_sut` or `this` at the start of a statement, since breaking before that call would gain no room.

### Fixed
- Wrapping and optional braces use planned line breaks and indentation, so formatting reaches a fixpoint when arguments, switch arms, or lambda bodies move.
- Wrapped operators in a lambda passed to a chained call, such as `.Where(c => a` followed by `|| b`, no longer lose their indent when the chain's line breaks were already in place. Arguments of calls in such lambdas, and of calls in a chain inside an argument, indent from the chained call's line rather than the statement's.
- With `dress_binary_expression_indentation = precedence`, operators inside parentheses, a lambda, or a call indent from the line their group opens on, as with `flat`, rather than lining up with the operators outside.
- Item wrapping keeps trailing line comments with their original collection items, arguments, and parameters.
- Empty blocks used as brace-optional statement bodies remain blocks instead of becoming empty statements.
- Expanding a single-line accessor whose body is a block, such as `get { return 1; } set { Store(value); }`, left the next accessor on the closing brace's line as `} set`. Each accessor now starts its own line once a body in the list expands; accessors without a block body still share a line.
- Attributes on preceding lines no longer cause braces in otherwise single-line declarations to move to the next line.

## v0.2.1

### Fixed
- Formatting could stall for 30 seconds per project, or hang, in a repository whose NuGet feed uses a credential provider.

## v0.2.0

### Changed
- A project that can't be read is reported once, and its files are skipped, instead of failing the run with one error per file.
- A file compiled under several frameworks or projects is formatted under each, so every `#if` region is formatted. Such files used to fail, or be formatted under the lowest framework only.

### Fixed
- Projects that target several frameworks failed to evaluate with `error MSB4057: The target "AddImplicitDefineConstants" does not exist in the project`. Each framework is now read on its own, and a project that names no framework is read as it is declared.

## v0.1.0

### Added
- `dotnet dress format` and `dotnet dress check` format C# files according to explicit EditorConfig preferences, using syntax only.
- `--staged` and `--changed` select only the files Git reports as changed, so a pre-commit or editor hook formats what was touched.
- `dotnet dress init` adds every missing preference, set to its default, to an EditorConfig file.
- `dotnet dress interactive` opens a browser rule editor that previews pending preferences before writing them.
- Precompiled packages for Windows x64, Linux x64, macOS x64, and macOS Arm64.
