using System.Text;
using DressSharp.IO;
using DressSharp.TestSupport;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class SourceIOTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), $"DressSharp-{Guid.NewGuid():N}");

    public SourceIOTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task NoOpPreservesExactBytesAndDoesNotWrite()
    {
        var path = File([0xEF, 0xBB, 0xBF, .. "a\r\nb\nc"u8.ToArray()]);
        var document = await SourceDocument.ReadAsync(path, Token);
        var before = System.IO.File.GetLastWriteTimeUtc(path);

        var bytes = document.Encode(document.Text, new());
        var changed = await new AtomicFilePersistence().WriteIfChangedAsync(document, bytes, Token);

        changed.ShouldBe(false);
        System.IO.File.GetLastWriteTimeUtc(path).ShouldBe(before);
        var persisted = await System.IO.File.ReadAllBytesAsync(path, Token);
        ExactAssert.Bytes(document.OriginalBytes.Span, persisted);
        document.PreferredLineEnding.ShouldBe("\r\n");
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

        document.SourceEncoding.ShouldBe(encoding);
        document.Text.ShouldBe(text);
        ExactAssert.Bytes(bytes, document.Encode(text, new()));
    }

    [Fact]
    public async Task BomlessUtf16IsTreatedAsValidUtf8RatherThanGuessed()
    {
        var document = await SourceDocument.ReadAsync(File([.. "a\0b\0"u8]), Token);
        document.SourceEncoding.ShouldBe(SourceEncoding.Utf8);
        document.Text.ShouldBe("a\0b\0");
    }

    [Fact]
    public async Task UnsupportedBomFailsWithoutChangingFile()
    {
        var path = File([0xFF, 0xFE, 0x00, 0x00, 0x61]);
        SourceDocument.ReadAsync(path, Token).AsTask().ShouldFailWith<SourceIOException>();
        var persisted = await System.IO.File.ReadAllBytesAsync(path, Token);
        ExactAssert.Bytes([0xFF, 0xFE, 0x00, 0x00, 0x61], persisted);
    }

    [Fact]
    public async Task InvalidBomEncodedTextFailsWithoutChangingFile()
    {
        var bytes = new byte[] { 0xFF, 0xFE, 0x00 };
        var path = File(bytes);

        SourceDocument.ReadAsync(path, Token).AsTask().ShouldFailWith<SourceIOException>();

        var persisted = await System.IO.File.ReadAllBytesAsync(path, Token);
        ExactAssert.Bytes(bytes, persisted);
    }

    [Fact]
    public async Task ExplicitRepresentationPreferencesApplyToWholeDecodedText()
    {
        var document = await SourceDocument.ReadAsync(File([.. "a  \r\n#if false\r\nb  \r\n#endif"u8]), Token);
        var bytes = document.Encode(document.Text, new(SourceEncoding.Utf8Bom, "\n", true, true));

        ExactAssert.Bytes([0xEF, 0xBB, 0xBF, .. "a\n#if false\nb\n#endif\n"u8], bytes);
    }

    [Fact]
    public async Task DetectsConcurrentChangeBeforeReplacement()
    {
        var path = File([.. "before"u8]);
        var document = await SourceDocument.ReadAsync(path, Token);
        await System.IO.File.WriteAllTextAsync(path, "concurrent", Token);

        new AtomicFilePersistence().WriteIfChangedAsync(document, "after"u8.ToArray(), Token).AsTask().ShouldFailWith<SourceIOException>();
        (await System.IO.File.ReadAllTextAsync(path, Token)).ShouldBe("concurrent");
    }

    [Fact]
    public async Task ReplacementChangesContent()
    {
        var path = File([.. "before"u8]);
        var document = await SourceDocument.ReadAsync(path, Token);

        (await new AtomicFilePersistence().WriteIfChangedAsync(document, "after"u8.ToArray(), Token)).ShouldBe(true);
        (await System.IO.File.ReadAllTextAsync(path, Token)).ShouldBe("after");
        Directory.GetFiles(_directory, "*.dresssharp.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task ReplacementThroughSymbolicLinkPreservesTheLink()
    {
        var target = File([.. "before"u8]);
        var link = Path.Combine(_directory, "link.cs");
        try
        {
            System.IO.File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Symbolic links are unavailable in this environment.");
        }
        var document = await SourceDocument.ReadAsync(link, Token);

        await new AtomicFilePersistence().WriteIfChangedAsync(document, "after"u8.ToArray(), Token);

        new FileInfo(link).LinkTarget.ShouldNotBeNull();
        (await System.IO.File.ReadAllTextAsync(target, Token)).ShouldBe("after");
    }

    string File(byte[] bytes)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.cs");
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

    public void Dispose() => Directory.Delete(_directory, true);
}
