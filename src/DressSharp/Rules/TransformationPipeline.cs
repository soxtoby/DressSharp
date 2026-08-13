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

                current = rule.Transform(current, preference, new RuleContext(current));
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
