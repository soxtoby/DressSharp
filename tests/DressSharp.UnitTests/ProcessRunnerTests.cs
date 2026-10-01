using System.Diagnostics;
using Xunit;
using EasyAssertions;

namespace DressSharp.UnitTests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task Output_is_returned_when_the_process_exits_even_if_a_child_still_holds_its_pipes()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // `start /b` leaves a child that inherits the pipes and outlives its parent by 20 seconds.
        var startInfo = new ProcessStartInfo("cmd.exe");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("echo first & start /b cmd.exe /c \"ping -n 20 127.0.0.1 > nul\" & echo last");
        var stopwatch = Stopwatch.StartNew();

        var result = await ProcessRunner.TryRunAsync(startInfo, TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result!.ExitCode.ShouldBe(0);
        result!.StandardOutput.ShouldBe($"first {Environment.NewLine}last{Environment.NewLine}");
        (stopwatch.Elapsed < TimeSpan.FromSeconds(10)).ShouldBe(true, $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Output_is_complete_when_the_pipes_close_with_the_process()
    {
        var startInfo = new ProcessStartInfo("dotnet");
        startInfo.ArgumentList.Add("--list-runtimes");

        var result = await ProcessRunner.TryRunAsync(startInfo, TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result!.ExitCode.ShouldBe(0);
        result!.StandardOutput.ShouldContain("Microsoft.NETCore.App");
    }
}
