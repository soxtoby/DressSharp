using System.CommandLine;
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
}
