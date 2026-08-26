using DressSharp.TestSupport;
using Xunit;
using EasyAssertions;

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

        exitCode.ShouldBe(0);
        output.ToString().ShouldContain("Format C# using explicit syntax-only preferences.");
        ExactAssert.Text(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Init_command_creates_the_default_preferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DressSharp.IntegrationTests", Guid.NewGuid().ToString("N"));
        try
        {
            var target = Path.Combine(directory, ".editorconfig");

            var exitCode = await Program.CreateCommand().Parse(["init", "--target", target]).InvokeAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            exitCode.ShouldBe(0);
            File.Exists(target).ShouldBe(true);
            var text = await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken);
            text.ShouldContain("dress_embedded_statement_placement = next_line");
            text.ShouldContain("dress_embedded_statement_braces = balanced");
            text.ShouldContain("dress_braces_for_multiline_statement_header = true");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Init_command_preserves_existing_preferences_and_adds_missing_ones()
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

            exitCode.ShouldBe(0);
            var text = await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken);
            text.ShouldContain("indent_size = 2");
            (text.Split("indent_size =", StringSplitOptions.None).Length - 1).ShouldBe(1);
            text.ShouldContain("dress_embedded_statement_placement = next_line");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
