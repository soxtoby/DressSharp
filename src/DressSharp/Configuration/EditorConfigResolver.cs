using DressSharp.Architecture;
using EditorConfig.Core;

namespace DressSharp.Configuration;

sealed class EditorConfigResolver : IConfigurationResolver
{
    readonly EditorConfigParser _parser = new();

    public async ValueTask<FormattingConfiguration> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        FileConfiguration resolved;
        try
        {
            resolved = _parser.Parse(fullPath);
            foreach (var config in resolved.EditorConfigFiles)
            {
                var configPath = Path.Combine(config.Directory, config.FileName);
                EditorConfigSyntaxValidator.Validate(
                    configPath,
                    await File.ReadAllTextAsync(configPath, cancellationToken));
            }
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
        return new FormattingConfiguration(preferences);
    }
}

static class ConfigurationPreflight
{
    internal static async Task<IReadOnlyDictionary<string, FormattingConfiguration>> ResolveAllAsync(
        IEnumerable<string> paths, IConfigurationResolver resolver, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, FormattingConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            result[path] = await resolver.ResolveAsync(path, cancellationToken);
        return result;
    }
}
