using System.Net;
using System.Text.Json;
using DressSharp.Interactive;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class InteractiveHttpServerTests
{
    [Fact]
    public async Task Server_uses_random_loopback_addresses()
    {
        await using var first = await InteractiveHttpServer.Start(null, TestContext.Current.CancellationToken);
        await using var second = await InteractiveHttpServer.Start(null, TestContext.Current.CancellationToken);

        first.Address.Host.ShouldBe("127.0.0.1");
        second.Address.Host.ShouldBe("127.0.0.1");
        first.Address.Port.ShouldNotBe(second.Address.Port);
        first.Address.Query.ShouldBeEmpty();
    }

    [Fact]
    public async Task Embedded_application_and_catalog_are_public_and_offline()
    {
        await using var server = await InteractiveHttpServer.Start("nested/.editorconfig", TestContext.Current.CancellationToken);
        using var client = CreateClient(server.Address);

        using var index = await client.GetAsync("/", TestContext.Current.CancellationToken);
        using var script = await client.GetAsync("/app.js", TestContext.Current.CancellationToken);
        using var styles = await client.GetAsync("/app.css", TestContext.Current.CancellationToken);
        using var bootstrap = await client.GetAsync("/api/bootstrap", TestContext.Current.CancellationToken);

        index.StatusCode.ShouldBe(HttpStatusCode.OK);
        script.StatusCode.ShouldBe(HttpStatusCode.OK);
        styles.StatusCode.ShouldBe(HttpStatusCode.OK);
        bootstrap.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await index.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("DressSharp interactive");
        (await script.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("http://");
        (await styles.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("url(");

        using var document = JsonDocument.Parse(await bootstrap.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("requestedConfig").GetString().ShouldBe("nested/.editorconfig");
        document.RootElement.GetProperty("catalog").GetProperty("version").GetInt32().ShouldBe(1);
        document.RootElement.GetProperty("catalog").GetProperty("rules").GetArrayLength().ShouldBeGreaterThan(0);
        var token = document.RootElement.GetProperty("csrfToken").GetString()!;
        token.Length.ShouldBe(64);
        server.Address.AbsoluteUri.ShouldNotContain(token);
        index.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'self'");
        index.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
    }

    [Fact]
    public async Task Write_endpoint_requires_the_per_launch_csrf_header()
    {
        await using var server = await InteractiveHttpServer.Start(null, TestContext.Current.CancellationToken);
        using var client = CreateClient(server.Address);
        var token = await GetToken(client);

        using var missing = await client.PostAsync("/api/shutdown", null, TestContext.Current.CancellationToken);
        using var wrongRequest = new HttpRequestMessage(HttpMethod.Post, "/api/shutdown");
        wrongRequest.Headers.Add("X-DressSharp-CSRF", new string('0', 64));
        using var wrong = await client.SendAsync(wrongRequest, TestContext.Current.CancellationToken);

        missing.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var acceptedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/shutdown");
        acceptedRequest.Headers.Add("X-DressSharp-CSRF", token);
        using var accepted = await client.SendAsync(acceptedRequest, TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await server.WaitForShutdown(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unknown_routes_and_unsupported_methods_are_rejected_without_cors()
    {
        await using var server = await InteractiveHttpServer.Start(null, TestContext.Current.CancellationToken);
        using var client = CreateClient(server.Address);

        using var missing = await client.GetAsync("/missing", TestContext.Current.CancellationToken);
        using var options = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/shutdown"), TestContext.Current.CancellationToken);

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        options.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        options.Headers.Contains("Access-Control-Allow-Origin").ShouldBe(false);
    }

    static HttpClient CreateClient(Uri address) => new(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = address };

    static async Task<string> GetToken(HttpClient client)
    {
        using var response = await client.GetAsync("/api/bootstrap", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("csrfToken").GetString()!;
    }
}
