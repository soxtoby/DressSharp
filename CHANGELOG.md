# Changelog

## Unreleased

## v0.1.0

### Added
- `dotnet dress format` and `dotnet dress check` format C# files according to explicit EditorConfig preferences, using syntax only.
- `--staged` and `--changed` select only the files Git reports as changed, so a pre-commit or editor hook formats what was touched.
- `dotnet dress init` adds every missing preference, set to its default, to an EditorConfig file.
- `dotnet dress interactive` opens a browser rule editor that previews pending preferences before writing them.
- Precompiled packages for Windows x64, Linux x64, macOS x64, and macOS Arm64.
