using System.Collections.Concurrent;
using DressSharp.Architecture;
using EditorConfig.Core;

namespace DressSharp.Configuration;

sealed class EditorConfigResolver
{
    readonly EditorConfigParser _parser = new();

    // A run resolves every selected file against the same handful of EditorConfig files. Validating
    // each one once, rather than once per selected file, is the difference between reading a few
    // files and reading thousands.
    readonly ConcurrentDictionary<string, Lazy<bool>> _validated = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyDictionary<string, FormattingConfiguration> ResolveAll(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPaths = paths.Select(Path.GetFullPath).ToArray();
        FormattingConfiguration[] configurations;
        try
        {
            var resolvedFiles = _parser.Parse(fullPaths);
            configurations = Canonicalize(resolvedFiles);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ConfigurationException($"invalid EditorConfig: {exception.Message}");
        }

        return paths
            .Select((path, index) => (path, configuration: configurations[index]))
            .ToDictionary(pair => pair.path, pair => pair.configuration, StringComparer.OrdinalIgnoreCase);
    }

    FormattingConfiguration[] Canonicalize(IEnumerable<FileConfiguration> resolvedFiles)
    {
        var byRawProperties = new Dictionary<IReadOnlyDictionary<string, string>, FormattingConfiguration>(EffectivePropertyComparer.Instance);
        var canonical = new Dictionary<FormattingConfiguration, FormattingConfiguration>(FormattingConfigurationValueComparer.Instance);
        var configurations = new List<FormattingConfiguration>();
        foreach (var resolved in resolvedFiles)
        {
            Validate(resolved);
            if (!byRawProperties.TryGetValue(resolved.Properties, out var configuration))
            {
                configuration = ToConfiguration(resolved.FileName, resolved);
                byRawProperties.Add(resolved.Properties, configuration);
            }

            if (canonical.TryGetValue(configuration, out var existing))
                configuration = existing;
            else
                canonical.Add(configuration, configuration);
            configurations.Add(configuration);
        }

        return configurations.ToArray();
    }

    void Validate(FileConfiguration resolved)
    {
        foreach (var config in resolved.EditorConfigFiles)
        {
            var configPath = Path.Combine(config.Directory, config.FileName);
            _ = _validated.GetOrAdd(
                configPath,
                key => new Lazy<bool>(() =>
                    {
                        EditorConfigSyntaxValidator.DecodeAndValidate(key, File.ReadAllBytes(key));
                        return true;
                    })).Value;
        }
    }

    static FormattingConfiguration ToConfiguration(string fullPath, FileConfiguration resolved)
    {
        var preferences = new Dictionary<RuleKey, string>();
        foreach (var (rawKey, value) in resolved.Properties)
        {
            if (!RuleKeys.TryParse(rawKey, out var key))
                continue;
            if (!PreferenceCatalog.IsValid(key, value))
                throw new ConfigurationException($"{fullPath}: invalid effective value '{value}' for '{rawKey}'.");
            if (!value.Equals("unset", StringComparison.OrdinalIgnoreCase))
                preferences[key] = PreferenceCatalog.Normalize(key, value);
        }

        return new FormattingConfiguration(preferences);
    }

    sealed class EffectivePropertyComparer : IEqualityComparer<IReadOnlyDictionary<string, string>>
    {
        internal static EffectivePropertyComparer Instance { get; } = new();

        public bool Equals(
            IReadOnlyDictionary<string, string>? left,
            IReadOnlyDictionary<string, string>? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null || left.Count != right.Count)
                return false;
            foreach (var (key, value) in left)
            {
                if (!right.TryGetValue(key, out var other)
                    || !value.Equals(other, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(IReadOnlyDictionary<string, string> properties)
        {
            var hash = 0;
            foreach (var (key, value) in properties)
            {
                hash ^= HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(key),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(value));
            }

            return hash;
        }
    }
}
