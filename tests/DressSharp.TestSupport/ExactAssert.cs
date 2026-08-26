using System.Text;
using EasyAssertions;

namespace DressSharp.TestSupport;

public static class ExactAssert
{
    public static void Text(string expected, string actual) => actual.ShouldBe(expected);

    public static void Bytes(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) =>
        actual.ToArray().ShouldMatch(expected.ToArray());

    public static void Utf8(string expected, ReadOnlySpan<byte> actual) =>
        Bytes(Encoding.UTF8.GetBytes(expected), actual);
}
