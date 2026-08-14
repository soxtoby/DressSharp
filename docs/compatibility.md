# Compatibility and versioning

DressSharp targets .NET 10 (`net10.0`). It supports operating systems and architectures supported by that runtime, subject to successful MSBuild project evaluation. CI covers Windows, Linux, and macOS x64 and Arm64 where hosted runners are available.

DressSharp bundles and pins Roslyn rather than loading the invoking SDK's or project's compiler assemblies. Stable DressSharp releases use stable Roslyn packages. Released and preview C# versions understood by that bundled Roslyn are accepted when selected by the resolved parse context.

The `DressSharp` package follows SemVer. Breaking CLI or configuration changes require a major release. Formatting output may change in a minor release and must be called out in release notes. Pinning the package version pins the formatter and its output contract.
