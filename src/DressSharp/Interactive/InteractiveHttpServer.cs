using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DressSharp.Architecture;
using DressSharp.Rules;

namespace DressSharp.Interactive;

interface IInteractiveServer : IAsyncDisposable
{
    Uri Address { get; }

    Task WaitForShutdown(CancellationToken cancellationToken);
}

interface IInteractiveServerFactory
{
    Task<IInteractiveServer> Start(string? configPath, CancellationToken cancellationToken);
}

sealed class InteractiveHttpServerFactory : IInteractiveServerFactory
{
    public async Task<IInteractiveServer> Start(string? configPath, CancellationToken cancellationToken) =>
        await InteractiveHttpServer.Start(configPath, cancellationToken);
}

sealed class InteractiveHttpServer : IInteractiveServer
{
    const string CsrfHeader = "X-DressSharp-CSRF";
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    readonly HttpListener _listener;
    readonly CancellationTokenSource _stopping = new();
    readonly TaskCompletionSource _shutdownRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Task _requestLoop;
    readonly string _csrfToken;
    readonly string? _configPath;
    bool _disposed;

    InteractiveHttpServer(HttpListener listener, Uri address, string? configPath)
    {
        _listener = listener;
        _configPath = configPath;
        Address = address;
        _csrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _requestLoop = Listen();
    }

    public Uri Address { get; }

    internal static Task<InteractiveHttpServer> Start(string? configPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var address = new Uri($"http://127.0.0.1:{ReservePort()}/");
            var listener = new HttpListener { IgnoreWriteExceptions = true };
            listener.Prefixes.Add(address.AbsoluteUri);
            try
            {
                listener.Start();
                return Task.FromResult(new InteractiveHttpServer(listener, address, configPath));
            }
            catch (HttpListenerException exception)
            {
                lastFailure = exception;
                listener.Close();
            }
        }

        throw new InvalidOperationException("Could not start the DressSharp interactive server on loopback.", lastFailure);
    }

    public async Task WaitForShutdown(CancellationToken cancellationToken) =>
        await _shutdownRequested.Task.WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _stopping.CancelAsync();
        _listener.Close();
        try
        {
            await _requestLoop;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    async Task Listen()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(_stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            await Handle(context);
        }
    }

    async Task Handle(HttpListenerContext context)
    {
        AddSecurityHeaders(context.Response);
        try
        {
            var path = context.Request.Url?.AbsolutePath;
            if (context.Request.HttpMethod == "GET" && path == "/")
            {
                await Write(context.Response, 200, "text/html; charset=utf-8", InteractiveAssets.Index);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/app.js")
            {
                await Write(context.Response, 200, "text/javascript; charset=utf-8", InteractiveAssets.JavaScript);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/app.css")
            {
                await Write(context.Response, 200, "text/css; charset=utf-8", InteractiveAssets.Css);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/bootstrap")
            {
                await Write(context.Response, 200, "application/json; charset=utf-8", CreateBootstrap());
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/shutdown")
            {
                if (!TokenMatches(context.Request.Headers[CsrfHeader]))
                {
                    await Write(context.Response, 403, "text/plain; charset=utf-8", "Forbidden");
                    return;
                }

                context.Response.StatusCode = 204;
                context.Response.Close();
                _shutdownRequested.TrySetResult();
                return;
            }

            if (path is "/" or "/app.js" or "/app.css" or "/api/bootstrap" or "/api/shutdown")
            {
                context.Response.Headers[HttpResponseHeader.Allow] = path == "/api/shutdown" ? "POST" : "GET";
                await Write(context.Response, 405, "text/plain; charset=utf-8", "Method not allowed");
                return;
            }

            await Write(context.Response, 404, "text/plain; charset=utf-8", "Not found");
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException)
        {
            context.Response.Abort();
        }
    }

    byte[] CreateBootstrap()
    {
        var rules = RuleCatalog.BuiltIn.Rules.Select(rule => new
            {
                key = rule.Metadata.RuleKey.ToName(),
                name = rule.Metadata.Name,
                group = rule.Metadata.GroupName,
                description = rule.Metadata.Description,
                defaultValue = rule.Metadata.DefaultValue,
                valueKind = rule.Metadata.Values.Kind.ToString().ToLowerInvariant(),
                values = rule.Metadata.Values.Options.Select(option => new
                    {
                        option.Value,
                        option.Label,
                        option.Description,
                    }),
                minimum = rule.Metadata.Values.Minimum,
                specialValues = rule.Metadata.Values.SpecialValues.IsDefault ? [] : rule.Metadata.Values.SpecialValues,
            });
        return JsonSerializer.SerializeToUtf8Bytes(new
            {
                csrfToken = _csrfToken,
                requestedConfig = _configPath,
                catalog = new { version = RuleCatalog.BuiltIn.Version, rules },
            }, JsonOptions);
    }

    bool TokenMatches(string? candidate)
    {
        if (candidate is null || candidate.Length != _csrfToken.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(candidate),
            Encoding.ASCII.GetBytes(_csrfToken));
    }

    static void AddSecurityHeaders(HttpListenerResponse response)
    {
        response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cache-Control"] = "no-store";
    }

    static async Task Write(HttpListenerResponse response, int statusCode, string contentType, byte[] content)
    {
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = content.Length;
        await response.OutputStream.WriteAsync(content);
        response.Close();
    }

    static async Task Write(HttpListenerResponse response, int statusCode, string contentType, string content) =>
        await Write(response, statusCode, contentType, Encoding.UTF8.GetBytes(content));

    static int ReservePort()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        try
        {
            return ((IPEndPoint)reservation.LocalEndpoint).Port;
        }
        finally
        {
            reservation.Stop();
        }
    }
}
