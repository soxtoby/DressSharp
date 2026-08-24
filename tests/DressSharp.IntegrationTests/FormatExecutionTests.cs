using DressSharp.CommandLine;
using DressSharp.Execution;
using DressSharp.TestSupport;
using Xunit;

namespace DressSharp.IntegrationTests;

public sealed class FormatExecutionTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), "DressSharp.IntegrationTests", Guid.NewGuid().ToString("N"));

    public FormatExecutionTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(_directory, ".editorconfig"), "root = true\n[*.cs]\ncsharp_space_after_comma = true\nend_of_line = lf\ninsert_final_newline = true\n");
    }

    [Fact]
    public async Task Check_reports_representation_changes_without_writing()
    {
        var path = Source("class C { void M(int a,int b) { } }\r\n");
        var original = await File.ReadAllBytesAsync(path, Token);
        var (exitCode, output, error) = await Run(CommandKind.Check, path);

        Assert.Equal(1, exitCode);
        ExactAssert.Text("Program.cs" + Environment.NewLine, output);
        ExactAssert.Text(string.Empty, error);
        var persisted = await File.ReadAllBytesAsync(path, Token);
        ExactAssert.Bytes(original, persisted);
    }

    [Fact]
    public async Task Format_writes_silently_and_second_run_is_a_no_op()
    {
        var path = Source("class C { void M(int a,int b) { } }");

        var first = await Run(CommandKind.Format, path);
        var formatted = await File.ReadAllTextAsync(path, Token);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var second = await Run(CommandKind.Format, path);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        ExactAssert.Text(string.Empty, first.Output + first.Error + second.Output + second.Error);
        Assert.Contains("int a, int b", formatted);
        Assert.EndsWith("\n", formatted);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task Invalid_configuration_fails_before_any_write()
    {
        var first = Source("class A { void M(int a,int b) { } }", "A.cs");
        var second = Source("class B { void M(int a,int b) { } }", "B.cs");
        await File.AppendAllTextAsync(Path.Combine(_directory, ".editorconfig"), "[B.cs]\ncsharp_space_after_comma = invalid\n", Token);

        var output = new StringWriter();
        var error = new StringWriter();
        var selected = new[] { new SelectedFile(first, "A.cs"), new SelectedFile(second, "B.cs") };
        await Assert.ThrowsAsync<DressSharp.Configuration.ConfigurationException>(() =>
            new FormatExecutor(_directory, output, error).Run(new(CommandKind.Format, [], false, null), selected, Token));

        Assert.DoesNotContain("a, int", await File.ReadAllTextAsync(first, Token));
    }

    [Fact]
    public async Task Verbose_check_reports_emitter_skipped_occurrences_exactly()
    {
        var path = Source("class C\n{\nvoid M()\n{\n      retry:\nreturn +;\n}\n}");
        await File.AppendAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            "csharp_indent_labels = flush_left\n",
            Token);

        var (_, _, error) = await Run(CommandKind.Check, path, verbose: true);

        ExactAssert.Text("Program.cs: skipped 1 malformed occurrence(s)." + Environment.NewLine, error);
    }

    async Task<(int ExitCode, string Output, string Error)> Run(CommandKind kind, string path, bool verbose = false)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await new FormatExecutor(_directory, output, error).Run(
            new(kind, [], verbose, null), [new SelectedFile(path, Path.GetFileName(path))], Token);
        return (exitCode, output.ToString(), error.ToString());
    }

    string Source(string text, string name = "Program.cs")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_directory, true);
}
