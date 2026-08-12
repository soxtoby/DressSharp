using System.Text;
using Xunit;

namespace DressSharp.TestSupport;

public static class ExactAssert
{
    public static void Text(string expected, string actual) => Assert.Equal(expected, actual);

    public static void Bytes(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        Assert.True(
            expected.SequenceEqual(actual),
            $"Expected: {Convert.ToHexString(expected)}{Environment.NewLine}Actual:   {Convert.ToHexString(actual)}");
    }

    public static void Utf8(string expected, ReadOnlySpan<byte> actual) =>
        Bytes(Encoding.UTF8.GetBytes(expected), actual);
}
