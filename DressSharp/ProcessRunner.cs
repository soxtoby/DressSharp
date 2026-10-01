using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DressSharp;

static class ProcessRunner
{
    internal static async ValueTask<ProcessOutput?> TryRunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.UseShellExecute = false;

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            return null;
        }

        if (process is null)
            return null;

        using (process)
        {
            // On Windows a process the child starts inherits the child's end of each pipe and may
            // outlive it, so neither pipe is sure to reach end-of-file when the child exits. The
            // child's output is complete once it has exited, so from then on the reads are stopped
            // as soon as the pipes hold nothing unread, rather than waiting for end-of-file.
            using var readers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var output = ReadAsync(process.StandardOutput, readers.Token, cancellationToken);
            var error = ReadAsync(process.StandardError, readers.Token, cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                var reads = Task.WhenAll(output, error);
                if (OperatingSystem.IsWindows())
                {
                    while (!reads.IsCompleted)
                    {
                        if (!HasUnreadBytes(process.StandardOutput) && !HasUnreadBytes(process.StandardError))
                        {
                            readers.Cancel();
                            break;
                        }
                        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
                    }
                }
                return new(process.ExitCode, await output, await error);
            }
            catch
            {
                TryKill(process);
                throw;
            }
        }
    }

    static async Task<string> ReadAsync(StreamReader reader, CancellationToken readToken, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer, readToken)) > 0)
                text.Append(buffer, 0, count);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The pipe is held by something that outlived the process; what the process wrote is in hand.
        }
        return text.ToString();
    }

    /// <summary>
    /// Whether the pipe behind a redirected stream holds bytes no read has taken yet. A pipe that
    /// cannot be asked, because it is broken or is not a pipe, has nothing left to read.
    /// </summary>
    static bool HasUnreadBytes(StreamReader reader) =>
        reader.BaseStream is FileStream { SafeFileHandle: var handle }
        && PeekNamedPipe(handle.DangerousGetHandle(), 0, 0, 0, out var available, 0) != 0
        && available > 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern int PeekNamedPipe(nint pipe, nint buffer, uint bufferSize, nint bytesRead, out uint bytesAvailable, nint bytesLeftThisMessage);

    static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }
}

sealed record ProcessOutput(int ExitCode, string StandardOutput, string StandardError);
