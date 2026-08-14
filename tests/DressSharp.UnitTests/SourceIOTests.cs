using System.Text;
using DressSharp.IO;
using DressSharp.TestSupport;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class SourceIOTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), $"DressSharp-{Guid.NewGuid():N}");

    public SourceIOTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task NoOpPreservesExactBytesAndDoesNotWrite()
    {
        var path = File([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a\r\nb\nc")]);
        var document = await SourceDocument.ReadAsync(path, Token);
        var before = System.IO.File.GetLastWriteTimeUtc(path);

        var bytes = document.Encode(document.Text, new());
        var changed = await new AtomicFilePersistence().WriteIfChangedAsync(document, bytes, Token);

        Assert.False(changed);
        Assert.Equal(before, System.IO.File.GetLastWriteTimeUtc(path));
        var persisted = await System.IO.File.ReadAllBytesAsync(path, Token);
        ExactAssert.Bytes(document.OriginalBytes.Span, persisted);
        Assert.Equal("\r\n", document.PreferredLineEnding);
    }

    [Theory]
    [InlineData(0, "hello")]
    [InlineData(4, "café")]
    [InlineData(2, "hello")]
    [InlineData(3, "hello")]
    public async Task DetectsAndPreservesEncoding(int encodingValue, string text)
    {
        var encoding = (SourceEncoding)encodingValue;
        var bytes = Encode(encoding, text);
        var document = await SourceDocument.ReadAsync(File(bytes), Token);

        Assert.Equal(encoding, document.SourceEncoding);
        Assert.Equal(text, document.Text);
        ExactAssert.Bytes(bytes, document.Encode(text, new()));
    }

    [Fact]
    public async Task BomlessUtf16IsTreatedAsValidUtf8RatherThanGuessed()
    {
        var document = await SourceDocument.ReadAsync(File([0x61, 0x00, 0x62, 0x00]), Token);
        Assert.Equal(SourceEncoding.Utf8, document.SourceEncoding);
        Assert.Equal("a\0b\0", document.Text);
    }

    [Fact]
    public async Task UnsupportedBomFailsWithoutChangingFile()
    {
        var path = File([0xFF, 0xFE, 0x00, 0x00, 0x61]);
        await Assert.ThrowsAsync<SourceIOException>(async () => await SourceDocument.ReadAsync(path, Token));
        var persisted = await System.IO.File.ReadAllBytesAsync(path, Token);
        ExactAssert.Bytes([0xFF, 0xFE, 0x00, 0x00, 0x61], persisted);
    }

    [Fact]
    public async Task InvalidBomEncodedTextFailsWithoutChangingFile()
    {
        var bytes = new byte[] { 0xFF, 0xFE, 0x00 };
        var path = File(bytes);

        await Assert.ThrowsAsync<SourceIOException>(async () => await SourceDocument.ReadAsync(path, Token));

        var persisted = await System.IO.File.ReadAllBytesAsync(path, Token);
        ExactAssert.Bytes(bytes, persisted);
    }

    [Fact]
    public async Task ExplicitRepresentationPreferencesApplyToWholeDecodedText()
    {
        var document = await SourceDocument.ReadAsync(File(Encoding.UTF8.GetBytes("a  \r\n#if false\r\nb  \r\n#endif")), Token);
        var bytes = document.Encode(document.Text, new(SourceEncoding.Utf8Bom, "\n", true, true));

        ExactAssert.Bytes([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a\n#if false\nb\n#endif\n")], bytes);
    }

    [Fact]
    public async Task DetectsConcurrentChangeBeforeReplacement()
    {
        var path = File(Encoding.UTF8.GetBytes("before"));
        var document = await SourceDocument.ReadAsync(path, Token);
        await System.IO.File.WriteAllTextAsync(path, "concurrent", Token);

        await Assert.ThrowsAsync<SourceIOException>(async () =>
            await new AtomicFilePersistence().WriteIfChangedAsync(document, Encoding.UTF8.GetBytes("after"), Token));
        Assert.Equal("concurrent", await System.IO.File.ReadAllTextAsync(path, Token));
    }

    [Fact]
    public async Task ReplacementChangesContent()
    {
        var path = File(Encoding.UTF8.GetBytes("before"));
        var document = await SourceDocument.ReadAsync(path, Token);

        Assert.True(await new AtomicFilePersistence().WriteIfChangedAsync(document, Encoding.UTF8.GetBytes("after"), Token));
        Assert.Equal("after", await System.IO.File.ReadAllTextAsync(path, Token));
        Assert.Empty(Directory.GetFiles(directory, "*.dresssharp.tmp"));
    }

    [Fact]
    public async Task ReplacementThroughSymbolicLinkPreservesTheLink()
    {
        var target = File(Encoding.UTF8.GetBytes("before"));
        var link = Path.Combine(directory, "link.cs");
        try
        {
            System.IO.File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Symbolic links are unavailable in this environment.");
        }
        var document = await SourceDocument.ReadAsync(link, Token);

        await new AtomicFilePersistence().WriteIfChangedAsync(document, Encoding.UTF8.GetBytes("after"), Token);

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("after", await System.IO.File.ReadAllTextAsync(target, Token));
    }

    string File(byte[] bytes)
    {
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.cs");
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    static byte[] Encode(SourceEncoding encoding, string text)
    {
        Encoding encoder = encoding switch
        {
            SourceEncoding.Utf8 => new UTF8Encoding(false),
            SourceEncoding.Latin1 => Encoding.Latin1,
            SourceEncoding.Utf16LittleEndian => new UnicodeEncoding(false, true),
            SourceEncoding.Utf16BigEndian => new UnicodeEncoding(true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
        return [.. encoder.GetPreamble(), .. encoder.GetBytes(text)];
    }

    static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(directory, true);
}
