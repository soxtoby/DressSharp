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

    [Fact]
    public async Task Init_command_creates_the_familiar_configuration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DressSharp.IntegrationTests", Guid.NewGuid().ToString("N"));
        try
        {
            var target = Path.Combine(directory, ".editorconfig");

            var exitCode = await Program.CreateCommand().Parse(["init", "--target", target]).InvokeAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(target));
            Assert.Contains("dress_conditional_braces = balanced", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Init_command_fails_without_changing_conflicting_configuration()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "DressSharp.IntegrationTests", Guid.NewGuid().ToString("N"))).FullName;
        var target = Path.Combine(directory, ".editorconfig");
        const string original = "[*.cs]\nindent_size = 2\n";
        try
        {
            await File.WriteAllTextAsync(target, original, TestContext.Current.CancellationToken);

            var exitCode = await Program.CreateCommand().Parse(["init", "--target", target]).InvokeAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, exitCode);
            Assert.Equal(original, await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
