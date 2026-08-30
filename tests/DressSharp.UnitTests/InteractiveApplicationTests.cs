using DressSharp.Interactive;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class InteractiveApplicationTests
{
    [Fact]
    public async Task Application_reports_address_opens_browser_and_disposes_server()
    {
        var server = new StubServer();
        server.RequestShutdown();
        var browser = new StubBrowser();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var sut = new InteractiveApplication(new StubServerFactory(server), browser, output, error);

        await sut.Run(null, TestContext.Current.CancellationToken);

        browser.Address.ShouldBe(server.Address);
        output.ToString().ShouldContain(server.Address.AbsoluteUri);
        error.ToString().ShouldBeEmpty();
        server.Disposed.ShouldBe(true);
    }

    [Fact]
    public async Task Browser_failure_reports_manual_url_and_keeps_application_running()
    {
        var server = new StubServer();
        var browser = new StubBrowser(new InvalidOperationException("no association"));
        using var output = new StringWriter();
        using var error = new StringWriter();
        var sut = new InteractiveApplication(new StubServerFactory(server), browser, output, error);
        var run = sut.Run("chosen/.editorconfig", TestContext.Current.CancellationToken);

        await browser.Called.Task;
        run.IsCompleted.ShouldBe(false);
        server.RequestShutdown();
        await run;

        error.ToString().ShouldContain("no association");
        error.ToString().ShouldContain(server.Address.AbsoluteUri);
        server.Disposed.ShouldBe(true);
    }

    [Fact]
    public async Task Cancellation_stops_and_disposes_the_server()
    {
        var server = new StubServer();
        using var cancellation = new CancellationTokenSource();
        var sut = new InteractiveApplication(
            new StubServerFactory(server),
            new StubBrowser(),
            TextWriter.Null,
            TextWriter.Null);
        var run = sut.Run(null, cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        server.Disposed.ShouldBe(true);
    }

    sealed class StubServerFactory(StubServer server) : IInteractiveServerFactory
    {
        public Task<IInteractiveServer> Start(string? configPath, CancellationToken cancellationToken)
        {
            server.ConfigPath = configPath;
            return Task.FromResult<IInteractiveServer>(server);
        }
    }

    sealed class StubServer : IInteractiveServer
    {
        readonly TaskCompletionSource shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Uri Address { get; } = new("http://127.0.0.1:43210/");
        internal string? ConfigPath { get; set; }
        internal bool Disposed { get; private set; }

        public Task WaitForShutdown(CancellationToken cancellationToken) => shutdown.Task.WaitAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        internal void RequestShutdown() => shutdown.TrySetResult();
    }

    sealed class StubBrowser(Exception? failure = null) : IInteractiveBrowser
    {
        internal TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Uri? Address { get; private set; }

        public void Open(Uri address)
        {
            Address = address;
            Called.TrySetResult();
            if (failure is not null)
                throw failure;
        }
    }
}
