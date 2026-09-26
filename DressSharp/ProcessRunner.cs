using System.ComponentModel;
using System.Diagnostics;

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
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var error = process.StandardError.ReadToEndAsync(cancellationToken);
                await Task.WhenAll(output, error, process.WaitForExitAsync(cancellationToken));
                return new(process.ExitCode, await output, await error);
            }
            catch
            {
                TryKill(process);
                throw;
            }
        }
    }

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
