using System.Diagnostics;
using System.Runtime.InteropServices;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;

namespace DressSharp.Rules;

sealed class TransformationPipeline(RuleCatalog catalog, Action<string, TimeSpan>? recordRule = null) : ITransformationPipeline
{
    public TransformationResult Transform(SyntaxNode root, FormattingConfiguration configuration)
    {
        var current = root;
        var skippedOccurrences = 0;
        try
        {
            var settings = RuleSettings.From(configuration);
            var enabled = Enabled(configuration);
            var knownWellFormed = false;
            var context = new RuleContext(current, settings, knownWellFormed);
            knownWellFormed |= !context.HasMalformedRegions;
            var spacingBatch = new List<(TokenSpacingRule Rule, string Preference)>();
            var commentBatch = new List<(CommentRule Rule, string Preference)>();

            for (var index = 0; index < enabled.Count;)
            {
                // Consecutive token-pair spacing rules share one walk and one rewrite. Every rule in
                // the run then judges safety against one consistent snapshot of the tree, rather than
                // against a tree the preceding rule has already shifted.
                spacingBatch.Clear();
                while (index < enabled.Count
                    && spacingBatch.Count < TokenSpacingBatch.MaximumRules
                    && enabled[index].Rule is TokenSpacingRule { ParticipatesInBatch: true } spacing)
                {
                    spacingBatch.Add((spacing, enabled[index].Preference));
                    index++;
                }

                // Likewise for consecutive comment rules, which each rewrite trivia lists in place.
                commentBatch.Clear();
                while (spacingBatch.Count == 0
                    && index < enabled.Count
                    && enabled[index].Rule is CommentRule comment)
                {
                    commentBatch.Add((comment, enabled[index].Preference));
                    index++;
                }

                SyntaxNode input;
                if (spacingBatch.Count > 0)
                {
                    input = current;
                    var started = recordRule is null ? 0 : Stopwatch.GetTimestamp();
                    current = TokenSpacingBatch.Apply(input, CollectionsMarshal.AsSpan(spacingBatch), context);
                    recordRule?.Invoke("token_spacing", Stopwatch.GetElapsedTime(started));
                }
                else if (commentBatch.Count > 0)
                {
                    input = current;
                    var started = recordRule is null ? 0 : Stopwatch.GetTimestamp();
                    current = CommentRuleBatch.Apply(input, CollectionsMarshal.AsSpan(commentBatch));
                    recordRule?.Invoke("comment_rules", Stopwatch.GetElapsedTime(started));
                }
                else
                {
                    var (rule, preference) = enabled[index];
                    index++;
                    input = current;
                    var started = recordRule is null ? 0 : Stopwatch.GetTimestamp();
                    current = rule.Transform(input, preference, context);
                    recordRule?.Invoke(rule.Metadata.PreferenceKey, Stopwatch.GetElapsedTime(started));
                }

                skippedOccurrences += context.TakeSkippedOccurrences();

                // The line ending and the malformed regions are read off the tree a rule sees, so the
                // context only needs rebuilding when a rule actually replaced the tree.
                if (!ReferenceEquals(input, current))
                {
                    context = new RuleContext(current, settings, knownWellFormed);
                    knownWellFormed |= !context.HasMalformedRegions;
                }
            }

            return new TransformationResult(current, SkippedOccurrences: skippedOccurrences);
        }
        catch (Exception exception)
        {
            return new TransformationResult(root, exception);
        }
    }

    IReadOnlyList<(IFormattingRule Rule, string Preference)> Enabled(FormattingConfiguration configuration)
    {
        var enabled = new List<(IFormattingRule, string)>(catalog.Rules.Length);
        foreach (var rule in catalog.Rules)
        {
            if (configuration.Preferences.TryGetValue(rule.Metadata.PreferenceKey, out var preference)
                && !preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
            {
                if (!rule.Metadata.AcceptedValues.Contains(preference, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Invalid value '{preference}' for '{rule.Metadata.PreferenceKey}'.");
                enabled.Add((rule, preference));
            }
        }

        return enabled;
    }

    internal async Task<IReadOnlyList<TransformationResult>> TransformAsync(
        IReadOnlyList<(SyntaxNode Root, FormattingConfiguration Configuration)> files,
        CancellationToken cancellationToken = default)
    {
        var results = new TransformationResult[files.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Count),
            cancellationToken,
            (index, _) =>
                {
                    var file = files[index];
                    results[index] = Transform(file.Root, file.Configuration);
                    return ValueTask.CompletedTask;
                });
        return results;
    }
}