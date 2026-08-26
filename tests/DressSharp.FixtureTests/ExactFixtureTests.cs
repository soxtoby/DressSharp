using DressSharp.TestSupport;
using Xunit;

namespace DressSharp.FixtureTests;

public sealed class ExactFixtureTests
{
    [Fact]
    public void Fixture_bytes_are_asserted_without_normalization()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "smoke.cs");
        ExactAssert.Utf8(
            """
            class Smoke
            {
            }
            """ + "\n",
            File.ReadAllBytes(path));
    }
}
