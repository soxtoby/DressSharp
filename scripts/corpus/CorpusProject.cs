/// <summary>
/// The project written beside the materialized corpus, so that dotnet format has a project to run on
/// and DressSharp resolves a parse context. It lives here rather than as a file in the repository,
/// where its default compile items would take in the whole corpus.
/// </summary>
static class CorpusProject
{
    public const string Name = "Corpus.csproj";

    public const string Content = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
          </PropertyGroup>
        </Project>

        """;
}
