using DressSharp.TestSupport;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class CommandTests
{
    [Fact]
    public void Root_command_describes_the_tool()
    {
        ExactAssert.Text(
            "Format C# using explicit syntax-only preferences.",
            Program.CreateCommand().Description ?? string.Empty);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("check")]
    [InlineData("init")]
    public void Root_command_exposes_documented_commands(string name)
    {
        Assert.Contains(Program.CreateCommand().Subcommands, command => command.Name == name);
    }

    [Fact]
    public void Root_include_patterns_are_the_default_format_alias()
    {
        var command = Program.CreateCommand();
        var includes = Assert.Single(command.Options.OfType<System.CommandLine.Option<string[]>>(), option => option.Name == "--include");
        var result = command.Parse(["--include", "one.cs", "--include", "src/**/*.cs"]);

        Assert.Empty(result.Errors);
        Assert.Equal(["one.cs", "src/**/*.cs"], result.GetValue(includes) ?? []);
    }

    [Fact]
    public void File_command_accepts_one_pattern_per_include_occurrence()
    {
        var root = Program.CreateCommand();
        var command = Assert.Single(root.Subcommands, command => command.Name == "format");
        var includes = Assert.Single(command.Options.OfType<System.CommandLine.Option<string[]>>(), option => option.Name == "--include");
        var result = root.Parse(["format", "--include", "one.cs", "--include", "two.cs"]);

        Assert.Empty(result.Errors);
        Assert.Equal(["one.cs", "two.cs"], result.GetValue(includes) ?? []);
    }

    [Theory]
    [InlineData("source.cs")]
    [InlineData("--unknown")]
    [InlineData("format --force")]
    [InlineData("format --include one.cs two.cs")]
    [InlineData("init --include *.cs")]
    [InlineData("--include *.cs init")]
    [InlineData("--include *.cs format")]
    public void Positional_inputs_and_invalid_options_are_rejected(string commandLine)
    {
        var result = Program.CreateCommand().Parse(commandLine);

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Init_target_is_an_option()
    {
        var root = Program.CreateCommand();
        var command = Assert.Single(root.Subcommands, command => command.Name == "init");
        var target = Assert.Single(command.Options.OfType<System.CommandLine.Option<string?>>(), option => option.Name == "--target");
        var result = root.Parse("init --target nested/.editorconfig --force");

        Assert.Empty(result.Errors);
        Assert.Equal("nested/.editorconfig", result.GetValue(target));
    }
}
