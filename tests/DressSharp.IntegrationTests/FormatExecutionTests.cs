using DressSharp.CommandLine;
using DressSharp.Configuration;
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
    public async Task Format_reports_changed_file_count_and_second_run_as_a_no_op()
    {
        var path = Source("class C { void M(int a,int b) { } }");

        var first = await Run(CommandKind.Format, path);
        var formatted = await File.ReadAllTextAsync(path, Token);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var second = await Run(CommandKind.Format, path);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Matches($"^Formatted 1 of 1 file in [0-9]+\\.[0-9]{{2}} s\\.{Environment.NewLine}$", first.Output);
        Assert.Matches($"^Formatted 0 of 1 file in [0-9]+\\.[0-9]{{2}} s\\.{Environment.NewLine}$", second.Output);
        ExactAssert.Text(string.Empty, first.Error + second.Error);
        Assert.Contains("int a, int b", formatted);
        Assert.EndsWith("\n", formatted);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task Initialized_defaults_keep_required_spacing_and_layout_generated_braces()
    {
        await EditorConfigInitializer.InitializeAsync(
            Path.Combine(_directory, ".editorconfig"),
            _directory,
            Token);
        var path = Source("""
            class C
            {
                static readonly Dictionary<string, string> Values = new();

                bool Matches(string value)
                {
                    if (value == "first")
                        return value.Equals("first", StringComparison.OrdinalIgnoreCase)
                            || value.Equals("second", StringComparison.OrdinalIgnoreCase)
                            || value.Equals("third", StringComparison.OrdinalIgnoreCase);
                    return false;
                }

                object Resolve(object[] paths)
                {
                    var configurations = paths;
                    return paths
                        .Select((path, index) => (path, configuration: configurations[index]))
                        .ToDictionary(pair => pair.path, pair => pair.configuration, StringComparer.OrdinalIgnoreCase);
                }
            }
            """);

        var result = await Run(CommandKind.Format, path);
        var formatted = await File.ReadAllTextAsync(path, Token);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var second = await Run(CommandKind.Format, path);
        var formattedAgain = await File.ReadAllTextAsync(path, Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(formatted, formattedAgain);
        Assert.Matches($"^Formatted 0 of 1 file in [0-9]+\\.[0-9]{{2}} s\\.{Environment.NewLine}$", second.Output);
        Assert.Contains("Values = new();", formatted);
        Assert.Contains("\n        {\n            return value.Equals", formatted);
        Assert.DoesNotContain("{return", formatted);
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
    public async Task No_preferences_explain_how_to_initialize_editorconfig()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "root = true\n", Token);
        var path = Source("class C { }");

        var result = await Run(CommandKind.Format, path);

        Assert.Equal(0, result.ExitCode);
        Assert.Matches($"^Formatted 0 of 1 file in [0-9]+\\.[0-9]{{2}} s\\.{Environment.NewLine}$", result.Output);
        ExactAssert.Text(
            "warning: no EditorConfig preferences to apply; run 'dotnet dress init' to initialize .editorconfig."
                + Environment.NewLine,
            result.Error);
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
