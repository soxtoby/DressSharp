using DressSharp.Architecture;
using EditorConfig.Core;

namespace DressSharp.Configuration;

internal sealed class EditorConfigResolver : IConfigurationResolver
{
    private readonly EditorConfigParser _parser = new();

    public ValueTask<FormattingConfiguration> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        FileConfiguration resolved;
        try
        {
            resolved = _parser.Parse(fullPath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ConfigurationException($"{fullPath}: invalid EditorConfig: {exception.Message}");
        }

        var preferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in resolved.Properties)
        {
            if (!PreferenceCatalog.IsKnown(key))
                continue;
            if (!PreferenceCatalog.IsValid(key, value))
                throw new ConfigurationException($"{fullPath}: invalid effective value '{value}' for '{key}'.");
            if (!value.Equals("unset", StringComparison.OrdinalIgnoreCase))
                preferences[key] = PreferenceCatalog.Normalize(key, value);
        }
        return ValueTask.FromResult(new FormattingConfiguration(preferences));
    }
}

internal static class ConfigurationPreflight
{
    internal static async Task<IReadOnlyDictionary<string, FormattingConfiguration>> ResolveAllAsync(
        IEnumerable<string> paths, IConfigurationResolver resolver, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, FormattingConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            result[path] = await resolver.ResolveAsync(path, cancellationToken);
        }
        return result;
    }
}
