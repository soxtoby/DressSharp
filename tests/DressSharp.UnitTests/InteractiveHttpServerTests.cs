using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DressSharp.Interactive;
using DressSharp.Architecture;
using DressSharp.Rules;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class InteractiveHttpServerTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), "DressSharp.Tests", Guid.NewGuid().ToString("N"));

    public InteractiveHttpServerTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, ".editorconfig"), "root = true\n[*.cs]\nindent_style = space\n");
    }

    [Fact]
    public async Task Preview_uses_browser_assignments_without_reading_or_writing_files()
    {
        await using var server = await Start();
        using var client = CreateClient(server.Address);
        var path = Path.Combine(_directory, ".editorconfig");
        const string external = "root = true\n[*.cs]\ncsharp_space_after_comma = false\n";
        await File.WriteAllTextAsync(path, external, TestContext.Current.CancellationToken);
        using var response = await client.PostAsJsonAsync(
            "/api/preview",
            new
        {
            source = "class C { void M(int a,int b) {} }",
            preferences = new[]
                {
                    new
                { key = "csharp_space_after_comma",
                    local = new
                    { kind = "explicit",
                        value = "true" },
                    inherited = new
                    { kind = "absent",
                        value = (string?)null } }
                }
        },
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        result.RootElement.GetProperty("text").GetString().ShouldContain("int a, int b");
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe(external);
        Directory.GetFiles(_directory).Length.ShouldBe(1);
    }

    [Fact]
    public async Task Server_uses_random_loopback_addresses()
    {
        await using var first = await Start();
        await using var second = await Start();

        first.Address.Host.ShouldBe("127.0.0.1");
        second.Address.Host.ShouldBe("127.0.0.1");
        first.Address.Port.ShouldNotBe(second.Address.Port);
        first.Address.Query.ShouldBeEmpty();
    }

    [Fact]
    public async Task Embedded_application_and_catalog_are_public_and_offline()
    {
        await using var server = await Start();
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
        (script.Content.Headers.ContentLength > 0).ShouldBe(true);
        (await styles.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("url(");

        using var document = JsonDocument.Parse(await bootstrap.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("requestedConfig").GetString().ShouldBe(Path.Combine(_directory, ".editorconfig"));
        document.RootElement.GetProperty("catalog").GetProperty("version").GetInt32().ShouldBe(1);
        document.RootElement.GetProperty("catalog").GetProperty("rules").GetArrayLength().ShouldBeGreaterThan(0);
        var catalog = RuleCatalog.BuiltIn.Rules.ToDictionary(rule => rule.Metadata.RuleKey.ToName(), rule => rule.Metadata);
        foreach (var rule in document.RootElement.GetProperty("catalog").GetProperty("rules").EnumerateArray())
        {
            var metadata = catalog[rule.GetProperty("key").GetString()!];
            rule.GetProperty("caption").GetString().ShouldBe(metadata.Caption);
            rule.GetProperty("expandedCaption").GetString().ShouldBe(metadata.ExpandedCaption);
            rule.GetProperty("subgroup").GetString().ShouldBe(metadata.SubgroupName);
        }
        var token = document.RootElement.GetProperty("csrfToken").GetString()!;
        token.Length.ShouldBe(64);
        server.Address.AbsoluteUri.ShouldNotContain(token);
        index.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'self'");
        index.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
    }

    [Fact]
    public async Task Write_endpoint_requires_the_per_launch_csrf_header()
    {
        await using var server = await Start();
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
        await using var server = await Start();
        using var client = CreateClient(server.Address);

        using var missing = await client.GetAsync("/missing", TestContext.Current.CancellationToken);
        using var options = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/api/shutdown"), TestContext.Current.CancellationToken);

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        options.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        options.Headers.Contains("Access-Control-Allow-Origin").ShouldBe(false);
    }

    [Fact]
    public async Task Configuration_snapshot_and_save_use_the_latest_file()
    {
        await using var server = await Start();
        using var client = CreateClient(server.Address);
        var token = await GetToken(client);

        using var first = await client.GetAsync("/api/configuration", TestContext.Current.CancellationToken);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var revision = firstJson.RootElement.GetProperty("revision").GetString();
        var preference = firstJson.RootElement.GetProperty("preferences").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == "indent_style");
        preference.GetProperty("local").GetProperty("value").GetString().ShouldBe("space");

        await File.AppendAllTextAsync(Path.Combine(_directory, ".editorconfig"), "# external\n", TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/configuration")
            {
                Content = JsonContent.Create(new
                    {
                        edits = new[] { new { key = "indent_style", kind = "explicit", value = "tab" } }
                    })
            };
        request.Headers.Add("X-DressSharp-CSRF", token);
        using var saved = await client.SendAsync(request, TestContext.Current.CancellationToken);
        using var savedJson = JsonDocument.Parse(await saved.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        savedJson.RootElement.GetProperty("revision").GetString().ShouldNotBe(revision);
        (await File.ReadAllTextAsync(Path.Combine(_directory, ".editorconfig"), TestContext.Current.CancellationToken))
            .ShouldContain("# external\n")
            .And.ShouldContain("indent_style = tab");
    }

    [Fact]
    public async Task Configuration_write_is_atomic_and_requires_csrf()
    {
        await using var server = await Start();
        using var client = CreateClient(server.Address);
        var original = await File.ReadAllTextAsync(Path.Combine(_directory, ".editorconfig"), TestContext.Current.CancellationToken);
        using var missing = await client.PostAsJsonAsync(
            "/api/configuration",
            new { edits = Array.Empty<object>() },
            TestContext.Current.CancellationToken);
        var token = await GetToken(client);
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/api/configuration")
            {
                Content = JsonContent.Create(new
                    {
                        edits = new[]
                        {
                            new { key = "indent_style", kind = "explicit", value = "tab" },
                            new { key = "indent_size", kind = "explicit", value = "invalid" },
                        }
                    })
            };
        invalidRequest.Headers.Add("X-DressSharp-CSRF", token);
        using var invalid = await client.SendAsync(invalidRequest, TestContext.Current.CancellationToken);

        missing.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("invalid_request");
        (await File.ReadAllTextAsync(Path.Combine(_directory, ".editorconfig"), TestContext.Current.CancellationToken)).ShouldBe(original);
    }

    static HttpClient CreateClient(Uri address) => new(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = address };

    static async Task<string> GetToken(HttpClient client)
    {
        using var response = await client.GetAsync("/api/bootstrap", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("csrfToken").GetString()!;
    }

    Task<InteractiveHttpServer> Start() => InteractiveHttpServer.Start(null, _directory, TestContext.Current.CancellationToken);

    public void Dispose() => Directory.Delete(_directory, true);
}
