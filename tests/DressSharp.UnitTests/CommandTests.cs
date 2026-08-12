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
}
