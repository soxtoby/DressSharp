using System.CommandLine;
using DressSharp.Interactive;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class CommandTests
{
    [Fact]
    public void Root_command_describes_the_tool()
    {
        Program.CreateCommand().Description
            .ShouldBe("Format C# using explicit syntax-only preferences.");
    }

    [Theory]
    [InlineData("format")]
    [InlineData("check")]
    [InlineData("init")]
    [InlineData("interactive")]
    public void Root_command_exposes_documented_commands(string name)
    {
        Program.CreateCommand().Subcommands
            .ShouldContain(name, (c, n) => c.Name == n);
    }

    [Fact]
    public void Root_include_patterns_are_the_default_format_alias()
    {
        var sut = Program.CreateCommand();

        var result = sut.Parse(["--include", "one.cs", "--include", "src/**/*.cs"]);
        result.Errors.ShouldBeEmpty();
        sut.Options
            .OfType<Option<string[]>>()
            .Where(option => option.Name == "--include")
            .ShouldBeASingular<Option<string[]>>()
            .And(o => result.GetValue(o).ShouldMatch(["one.cs", "src/**/*.cs"]));
    }

    [Fact]
    public void File_command_accepts_one_pattern_per_include_occurrence()
    {
        var sut = Program.CreateCommand();

        var result = sut.Parse(["format", "--include", "one.cs", "--include", "two.cs"]);
        result.Errors.ShouldBeEmpty();
        sut.Subcommands.Where(command => command.Name == "format")
            .ShouldBeASingular<Command>()
            .And.Options.OfType<Option<string[]>>()
            .Where(option => option.Name == "--include").ShouldBeASingular<Option<string[]>>()
            .And(o => result.GetValue(o).ShouldMatch(["one.cs", "two.cs"]));
    }

    [Theory]
    [InlineData("format --staged")]
    [InlineData("check --changed")]
    [InlineData("--staged")]
    [InlineData("format --staged --include src/**/*.cs")]
    public void Scope_options_are_accepted(string commandLine)
    {
        Program.CreateCommand().Parse(commandLine).Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("format --staged --changed")]
    [InlineData("--staged format")]
    [InlineData("init --staged")]
    [InlineData("interactive --changed")]
    public void Scope_options_are_rejected_where_meaningless(string commandLine)
    {
        Program.CreateCommand().Parse(commandLine).Errors.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("source.cs")]
    [InlineData("--unknown")]
    [InlineData("format --force")]
    [InlineData("init --force")]
    [InlineData("format --include one.cs two.cs")]
    [InlineData("init --include *.cs")]
    [InlineData("--include *.cs init")]
    [InlineData("--include *.cs format")]
    public void Positional_inputs_and_invalid_options_are_rejected(string commandLine)
    {
        Program.CreateCommand().Parse(commandLine).Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Init_target_is_an_option()
    {
        var sut = Program.CreateCommand();

        var result = sut.Parse("init --target nested/.editorconfig");
        result.Errors.ShouldBeEmpty();
        sut.Subcommands
            .Where(command => command.Name == "init")
            .ShouldBeASingular<Command>()
            .And.Options.OfType<Option<string?>>()
            .Where(option => option.Name == "--target").ShouldBeASingular<Option<string?>>()
            .And(o => result.GetValue(o).ShouldBe("nested/.editorconfig"));
    }

    [Fact]
    public void Config_is_reserved_for_the_interactive_command()
    {
        var sut = Program.CreateCommand();

        sut.Parse("interactive --config nested/.editorconfig").Errors.ShouldBeEmpty();
        sut.Parse("format --config Release").Errors.ShouldNotBeEmpty();
        sut.Parse("format --configuration Release").Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("interactive --include *.cs")]
    [InlineData("interactive --verbose")]
    [InlineData("interactive --configuration Release")]
    public void Interactive_command_rejects_file_command_options(string commandLine)
    {
        Program.CreateCommand().Parse(commandLine).Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Interactive_command_forwards_config_and_cancellation()
    {
        var application = new RecordingInteractiveApplication();

        var exitCode = await Program.CreateCommand(application).Parse("interactive --config nested/.editorconfig")
            .InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(0);
        application.ConfigPath.ShouldBe("nested/.editorconfig");
        application.CancellationToken.CanBeCanceled.ShouldBe(true);
    }

    [Fact]
    public async Task Interactive_startup_failure_is_a_command_failure()
    {
        using var error = new StringWriter();
        var application = new RecordingInteractiveApplication(new InvalidOperationException("listener unavailable"));

        var exitCode = await Program.CreateCommand(application, error).Parse("interactive").InvokeAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        exitCode.ShouldBe(2);
        error.ToString().ShouldContain("listener unavailable");
    }

    sealed class RecordingInteractiveApplication(Exception? failure = null) : IInteractiveApplication
    {
        internal string? ConfigPath { get; private set; }
        internal CancellationToken CancellationToken { get; private set; }

        public Task Run(string? configPath, string invocationDirectory, CancellationToken cancellationToken)
        {
            if (failure is not null)
                throw failure;
            ConfigPath = configPath;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
