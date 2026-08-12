using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Architecture;

internal interface IFileSelector
{
    ValueTask<IReadOnlyList<string>> SelectAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken);
}

internal interface IConfigurationResolver
{
    ValueTask<FormattingConfiguration> ResolveAsync(
        string path,
        CancellationToken cancellationToken);
}

internal interface IParseContextResolver
{
    ValueTask<CSharpParseOptions> ResolveAsync(
        string path,
        CancellationToken cancellationToken);
}

internal interface ITransformationPipeline
{
    SyntaxNode Transform(SyntaxNode root, FormattingConfiguration configuration);
}

internal interface IFilePersistence
{
    ValueTask WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

internal sealed record FormattingConfiguration(IReadOnlyDictionary<string, string> Preferences);
