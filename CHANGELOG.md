# Changelog

## Unreleased

### Changed
- A member access chain that wraps keeps its first call on the receiver's line when the receiver is no wider than the continuation indent, such as `_sut` or `this` at the start of a statement, since breaking before that call would gain no room.

### Fixed
- Expanding a single-line accessor whose body is a block, such as `get { return 1; } set { Store(value); }`, left the next accessor on the closing brace's line as `} set`. Each accessor now starts its own line once a body in the list expands; accessors without a block body still share a line.

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
