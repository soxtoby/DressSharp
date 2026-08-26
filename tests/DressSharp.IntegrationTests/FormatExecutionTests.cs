using DressSharp.CommandLine;
using DressSharp.Configuration;
using DressSharp.Execution;
using DressSharp.TestSupport;
using EasyAssertions;
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

        exitCode.ShouldBe(1);
        ExactAssert.Text("Program.cs" + Environment.NewLine, output);
        ExactAssert.Text(string.Empty, error);
        var persisted = await File.ReadAllBytesAsync(path, Token);
        ExactAssert.Bytes(original, persisted);
    }

    [Fact]
    public async Task Format_reports_changed_file_count_and_second_run_as_a_no_op()
    {
        const string source = """class C { void M(int a,int b) { } }""";
        const string expected = """
            class C { void M(int a, int b) { } }

            """;
        var path = Source(source);

        var first = await Run(CommandKind.Format, path);
        var formatted = await File.ReadAllTextAsync(path, Token);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var second = await Run(CommandKind.Format, path);

        first.ExitCode.ShouldBe(0);
        second.ExitCode.ShouldBe(0);
        first.Output.ShouldMatch($"^Formatted 1 of 1 file in [0-9]+\\.[0-9]{{2}} s\\.{Environment.NewLine}$");
        second.Output.ShouldMatch($"^Formatted 0 of 1 file in [0-9]+\\.[0-9]{{2}} s\\.{Environment.NewLine}$");
        ExactAssert.Text(string.Empty, first.Error + second.Error);
        formatted.ShouldBe(expected);
        File.GetLastWriteTimeUtc(path).ShouldBe(timestamp);
    }

    [Fact]
    public async Task Initialized_defaults_produce_the_expected_layout()
    {
        await EditorConfigInitializer.InitializeAsync(
            Path.Combine(_directory, ".editorconfig"),
            _directory,
            Token);
        var path = Source("""
            class C
            {
                static readonly Dictionary<string, string> Values = new();

                public RuleMetadata Metadata { get; } = new(
                    ruleKey,
                    acceptedValues,
                    ownedSyntax,
                    "Only same-line whitespace changes");

                int Identity(int value)
                {
                    return value;
                }

                object? Discover(ProcessResult result)
                {
                    if (result.ExitCode != 0)
                        return result.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                            ? null
                            : throw new FileSelectionException($"Git file discovery failed: {result.StandardError.Trim()}");
                    return result;
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

        const string expected = """
            class C
            {
                static readonly Dictionary<string, string> Values = new();

                public RuleMetadata Metadata { get; } = new(
                    ruleKey,
                    acceptedValues,
                    ownedSyntax,
                    "Only same-line whitespace changes");

                int Identity(int value) => value;

                object? Discover(ProcessResult result)
                {
                    if (result.ExitCode != 0)
                    {
                        return result.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                            ? null
                            : throw new FileSelectionException($"Git file discovery failed: {result.StandardError.Trim()}");
                    }
                    return result;
                }

                object Resolve(object[] paths)
                {
                    var configurations = paths;
                    return paths
                        .Select((path, index) => (path, configuration: configurations[index]))
                        .ToDictionary(pair => pair.path, pair => pair.configuration, StringComparer.OrdinalIgnoreCase);
                }
            }
            """;

        result.ExitCode.ShouldBe(0);
        formatted.ShouldBe(expected + "\n");
        formattedAgain.ShouldBe(formatted);
        second.Output.ShouldMatch($"^Formatted 0 of 1 file in [0-9]+\\.[0-9]{{2}} s\\.{Environment.NewLine}$");
        File.GetLastWriteTimeUtc(path).ShouldBe(timestamp);
    }

    [Fact]
    public async Task Invalid_configuration_fails_before_any_write()
    {
        const string firstSource = """class A { void M(int a,int b) { } }""";
        const string secondSource = """class B { void M(int a,int b) { } }""";
        var first = Source(firstSource, "A.cs");
        var second = Source(secondSource, "B.cs");
        await File.AppendAllTextAsync(Path.Combine(_directory, ".editorconfig"), "[B.cs]\ncsharp_space_after_comma = invalid\n", Token);

        var output = new StringWriter();
        var error = new StringWriter();
        var selected = new[] { new SelectedFile(first, "A.cs"), new SelectedFile(second, "B.cs") };
        new FormatExecutor(_directory, output, error).Run(new(CommandKind.Format, [], false, null), selected, Token).ShouldFailWith<DressSharp.Configuration.ConfigurationException>();

        (await File.ReadAllTextAsync(first, Token)).ShouldBe(firstSource);
        (await File.ReadAllTextAsync(second, Token)).ShouldBe(secondSource);
    }

    [Fact]
    public async Task No_preferences_explain_how_to_initialize_editorconfig()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "root = true\n", Token);
        var path = Source("class C { }");

        var result = await Run(CommandKind.Format, path);

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldMatch($"^Formatted 0 of 1 file in [0-9]+\\.[0-9]{{2}} s\\.{Environment.NewLine}$");
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
