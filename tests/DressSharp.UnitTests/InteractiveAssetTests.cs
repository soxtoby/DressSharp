using System.IO.Compression;
using System.Text;
using DressSharp.Interactive;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class InteractiveAssetTests
{
    static readonly byte[] Plain = Encoding.UTF8.GetBytes("const answer = 42;");
    static readonly byte[] Compressed = Compress(Plain);

    [Theory]
    [InlineData("gzip", "gzip")]
    [InlineData("br, gzip;q=0.5", "gzip")]
    [InlineData(null, null)]
    [InlineData("gzip;q=0", null)]
    public void Compressed_asset_negotiates_gzip(string? acceptEncoding, string? expectedEncoding)
    {
        var response = new InteractiveAsset(Compressed, true).ForRequest(acceptEncoding);

        response.ContentEncoding.ShouldBe(expectedEncoding);
        (response.ContentEncoding is null ? response.Content : Decompress(response.Content)).SequenceEqual(Plain).ShouldBe(true);
    }

    [Fact]
    public void Development_asset_remains_plain()
    {
        var response = new InteractiveAsset(Plain, false).ForRequest("gzip");

        response.ContentEncoding.ShouldBeNull();
        response.Content.SequenceEqual(Plain).ShouldBe(true);
    }

    static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, true))
            gzip.Write(content);
        return output.ToArray();
    }

    static byte[] Decompress(byte[] content)
    {
        using var input = new MemoryStream(content);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
