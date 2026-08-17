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
    ValueTask<IReadOnlyDictionary<string, ParseContextResolution>> ResolveAsync(
        IReadOnlyList<string> paths,
        string? configuration,
        CancellationToken cancellationToken);
}

sealed record ParseContextResolution(CSharpParseOptions? Options, IReadOnlyList<string> Diagnostics)
{
    internal bool CanFormat => Options is not null;
}

sealed record FormattingConfiguration
{
    internal FormattingConfiguration(IEnumerable<KeyValuePair<string, string>> preferences)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in preferences)
        {
            builder.Add(key, value);
            ValueHashCode ^= HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value));
        }

        Preferences = builder.ToImmutable();
    }

    internal ImmutableDictionary<string, string> Preferences { get; }
    internal int ValueHashCode { get; }
}

sealed class FormattingConfigurationValueComparer : IEqualityComparer<FormattingConfiguration>
{
    internal static FormattingConfigurationValueComparer Instance { get; } = new();

    public bool Equals(FormattingConfiguration? left, FormattingConfiguration? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Preferences.Count != right.Preferences.Count)
            return false;

        foreach (var (key, value) in left.Preferences)
        {
            if (!right.Preferences.TryGetValue(key, out var other)
                || !value.Equals(other, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public int GetHashCode(FormattingConfiguration configuration) => configuration.ValueHashCode;
}

sealed record TransformationResult(SyntaxNode Root, Exception? Failure = null, int SkippedOccurrences = 0)
{
    internal bool Succeeded => Failure is null;
}
