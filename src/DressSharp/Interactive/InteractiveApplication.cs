using System.Diagnostics;

namespace DressSharp.Interactive;

interface IInteractiveApplication
{
    Task Run(string? configPath, CancellationToken cancellationToken);
}

sealed class InteractiveApplication(
    IInteractiveServerFactory serverFactory,
    IInteractiveBrowser browser,
    TextWriter output,
    TextWriter error
) : IInteractiveApplication
{
    public async Task Run(string? configPath, CancellationToken cancellationToken)
    {
        await using var server = await serverFactory.Start(configPath, cancellationToken);
        await output.WriteLineAsync($"DressSharp interactive is running at {server.Address}");
        await output.WriteLineAsync("Press Ctrl+C to stop.");

        try
        {
            browser.Open(server.Address);
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"Could not open the browser: {exception.Message}");
            await error.WriteLineAsync($"Open {server.Address} manually.");
        }

        await server.WaitForShutdown(cancellationToken);
    }

    internal static InteractiveApplication CreateDefault() => new(
        new InteractiveHttpServerFactory(),
        new SystemInteractiveBrowser(),
        Console.Out,
        Console.Error);
}

interface IInteractiveBrowser
{
    void Open(Uri address);
}

sealed class SystemInteractiveBrowser : IInteractiveBrowser
{
    public void Open(Uri address)
    {
        Process.Start(new ProcessStartInfo(address.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }
}
