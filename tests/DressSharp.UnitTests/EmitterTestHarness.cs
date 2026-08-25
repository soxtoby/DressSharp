using DressSharp.Architecture;
using DressSharp.Execution;
using DressSharp.Rules;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DressSharp.UnitTests;

static class EmitterTestHarness
{
    internal static string Format(string source, params (string Key, string Value)[] preferences)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var configuration = new FormattingConfiguration(
            preferences.Select(pair => new KeyValuePair<RuleKey, string>(RuleKeys.Parse(pair.Key), pair.Value)));
        return new DocumentFormatter(configuration, new BenchmarkTiming()).FormatSyntax(
            root,
            source,
            CSharpParseOptions.Default,
            TestContext.Current.CancellationToken);
    }
}
