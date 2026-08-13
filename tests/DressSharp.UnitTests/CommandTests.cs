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
    public void Root_paths_are_the_default_format_alias()
    {
        var result = Program.CreateCommand().Parse(["one.cs", "two.cs"]);

        Assert.Empty(result.Errors);
        Assert.Equal(["one.cs", "two.cs"], result.GetValue<string[]>("paths") ?? []);
    }

    [Fact]
    public void Double_dash_allows_dash_prefixed_literal_path()
    {
        var result = Program.CreateCommand().Parse(["format", "--", "-file.cs"]);

        Assert.Empty(result.Errors);
        Assert.Equal(["-file.cs"], result.GetValue<string[]>("paths") ?? []);
    }
}
