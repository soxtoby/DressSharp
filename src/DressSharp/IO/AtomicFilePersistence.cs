namespace DressSharp.IO;

sealed class AtomicFilePersistence
{
    internal async ValueTask<bool> WriteIfChangedAsync(SourceDocument document, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        if (content.Span.SequenceEqual(document.OriginalBytes.Span))
            return false;

        var destination = ResolveDestination(document.Path);
        byte[] current;
        try
        {
            current = await File.ReadAllBytesAsync(destination, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SourceIOException($"Could not verify '{document.Path}' before writing.", exception);
        }
        
        if (!current.AsSpan().SequenceEqual(document.OriginalBytes.Span))
            throw new SourceIOException($"'{document.Path}' changed after it was read.");

        var directory = Path.GetDirectoryName(destination)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.dresssharp.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, destination, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SourceIOException($"Could not atomically replace '{document.Path}'.", exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    static string ResolveDestination(string path)
    {
        var information = new FileInfo(path);
        if (information.LinkTarget is null)
            return information.FullName;
        return information.ResolveLinkTarget(true)?.FullName
            ?? throw new SourceIOException($"The symbolic link '{path}' has no target.");
    }
}
