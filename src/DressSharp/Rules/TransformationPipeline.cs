using DressSharp.Architecture;
using Microsoft.CodeAnalysis;

namespace DressSharp.Rules;

sealed class TransformationPipeline(RuleCatalog catalog) : ITransformationPipeline
{
    public TransformationResult Transform(SyntaxNode root, FormattingConfiguration configuration)
    {
        var current = root;
        try
        {
            foreach (var rule in catalog.Rules)
            {
                if (!configuration.Preferences.TryGetValue(rule.Metadata.PreferenceKey, out var preference) 
                    || preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!rule.Metadata.AcceptedValues.Contains(preference, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Invalid value '{preference}' for '{rule.Metadata.PreferenceKey}'.");

                var maximum = configuration.Preferences.TryGetValue("max_line_length", out var configuredMaximum)
                    && int.TryParse(configuredMaximum, out var parsedMaximum)
                        ? parsedMaximum
                        : int.MaxValue;
                var tabWidth = configuration.Preferences.TryGetValue("tab_width", out var configuredTabWidth)
                    && int.TryParse(configuredTabWidth, out var parsedTabWidth)
                        ? parsedTabWidth
                        : 4;
                var usesTabs = configuration.Preferences.TryGetValue("indent_style", out var indentStyle)
                    && indentStyle.Equals("tab", StringComparison.OrdinalIgnoreCase);
                var indentSize = configuration.Preferences.TryGetValue("indent_size", out var configuredIndentSize)
                    && int.TryParse(configuredIndentSize, out var parsedIndentSize)
                        ? parsedIndentSize
                        : 4;
                var indentUnit = usesTabs ? "\t" : new string(' ', indentSize);
                current = rule.Transform(current, preference, new RuleContext(current, maximum, tabWidth, indentUnit));
            }
            return new TransformationResult(current);
        }
        catch (Exception exception)
        {
            return new TransformationResult(root, exception);
        }
    }

    internal async Task<IReadOnlyList<TransformationResult>> TransformAsync(
        IReadOnlyList<(SyntaxNode Root, FormattingConfiguration Configuration)> files,
        CancellationToken cancellationToken = default)
    {
        var results = new TransformationResult[files.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Count), cancellationToken, (index, _) =>
            {
                var file = files[index];
                results[index] = Transform(file.Root, file.Configuration);
                return ValueTask.CompletedTask;
            });
        return results;
    }
}
