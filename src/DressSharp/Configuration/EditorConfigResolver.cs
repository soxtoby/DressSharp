using System.Collections.Concurrent;
using DressSharp.Architecture;
using EditorConfig.Core;

namespace DressSharp.Configuration;

sealed class EditorConfigResolver : IConfigurationResolver
{
    readonly EditorConfigParser _parser = new();

    // A run resolves every selected file against the same handful of EditorConfig files. Validating
    // each one once, rather than once per selected file, is the difference between reading a few
    // files and reading thousands.
    readonly ConcurrentDictionary<string, Lazy<bool>> _validated = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<FormattingConfiguration> ResolveAsync(string path, CancellationToken cancellationToken)
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
                _ = _validated.GetOrAdd(
                    configPath,
                    key => new Lazy<bool>(() =>
                        {
                            EditorConfigSyntaxValidator.Validate(key, File.ReadAllText(key));
                            return true;
                        })).Value;
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

        return ValueTask.FromResult(new FormattingConfiguration(preferences));
    }
}

static class ConfigurationPreflight
{
    internal static async Task<IReadOnlyDictionary<string, FormattingConfiguration>> ResolveAllAsync(
        IEnumerable<string> paths, IConfigurationResolver resolver, CancellationToken cancellationToken)
    {
        var ordered = paths as IReadOnlyList<string> ?? [.. paths];
        var resolved = new FormattingConfiguration[ordered.Count];
        await Parallel.ForAsync(
            0,
            ordered.Count,
            cancellationToken,
            async (index, token) => resolved[index] = await resolver.ResolveAsync(ordered[index], token));

        var result = new Dictionary<string, FormattingConfiguration>(ordered.Count, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < ordered.Count; index++)
            result[ordered[index]] = resolved[index];
        return result;
    }
}