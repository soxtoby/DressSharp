namespace DressSharp.Rules;

/// <summary>
/// The file-specific token stream and whitespace decisions consumed by the emitter.
/// </summary>
sealed class EmissionLayoutPlan
{
    internal EmissionLayoutPlan(
        EffectiveTokenStream stream,
        TriviaLayoutPlan trivia,
        SyntaxWrappingPlan wrapping)
    {
        Stream = stream;
        Trivia = trivia;
        Wrapping = wrapping;
    }

    internal EffectiveTokenStream Stream { get; }
    internal TriviaLayoutPlan Trivia { get; }
    internal SyntaxWrappingPlan Wrapping { get; }
    internal int SkippedOccurrences => Trivia.SkippedOccurrences + Wrapping.SkippedOccurrences;
}
