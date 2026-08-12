using DressSharp.TestSupport;
using Xunit;

namespace DressSharp.IntegrationTests;

public sealed class SmokeTests
{
    [Fact]
    public async Task Help_invocation_succeeds()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var configuration = new System.CommandLine.InvocationConfiguration
        {
            Output = output,
            Error = error,
        };

        var exitCode = await Program.CreateCommand().Parse("--help").InvokeAsync(
            configuration,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains("Format C# using explicit syntax-only preferences.", output.ToString());
        ExactAssert.Text(string.Empty, error.ToString());
    }
}
