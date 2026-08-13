using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Architecture;

interface IFileSelector
{
    ValueTask<IReadOnlyList<string>> SelectAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken);
}

interface IConfigurationResolver
{
    ValueTask<FormattingConfiguration> ResolveAsync(
        string path,
        CancellationToken cancellationToken);
}

interface IParseContextResolver
{
    ValueTask<CSharpParseOptions> ResolveAsync(
        string path,
        CancellationToken cancellationToken);
}

interface ITransformationPipeline
{
    SyntaxNode Transform(SyntaxNode root, FormattingConfiguration configuration);
}

interface IFilePersistence
{
    ValueTask WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);
}

sealed record FormattingConfiguration
{
    internal FormattingConfiguration(IEnumerable<KeyValuePair<string, string>> preferences)
    {
        Preferences = preferences.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    internal ImmutableDictionary<string, string> Preferences { get; }
}
