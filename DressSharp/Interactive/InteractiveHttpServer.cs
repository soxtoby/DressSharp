using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DressSharp.Architecture;
using DressSharp.Configuration;
using DressSharp.Rules;

namespace DressSharp.Interactive;

interface IInteractiveServer : IAsyncDisposable
{
    Uri Address { get; }

    Task WaitForShutdown(CancellationToken cancellationToken);
}

interface IInteractiveServerFactory
{
    Task<IInteractiveServer> Start(string? configPath, string invocationDirectory, CancellationToken cancellationToken);
}

sealed class InteractiveHttpServerFactory : IInteractiveServerFactory
{
    public async Task<IInteractiveServer> Start(string? configPath, string invocationDirectory, CancellationToken cancellationToken) =>
        await InteractiveHttpServer.Start(configPath, invocationDirectory, cancellationToken);
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
    readonly string _targetPath;
    readonly string _invocationDirectory;
    bool _disposed;

    InteractiveHttpServer(HttpListener listener, Uri address, string targetPath, string invocationDirectory)
    {
        _listener = listener;
        _targetPath = targetPath;
        _invocationDirectory = invocationDirectory;
        Address = address;
        _csrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _requestLoop = Listen();
    }

    public Uri Address { get; }

    internal static async Task<InteractiveHttpServer> Start(
        string? configPath,
        string invocationDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InteractiveAssets.EnsureBuilt();
        var configuration = await InteractiveEditorConfig.LoadAsync(configPath, invocationDirectory, cancellationToken);
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var address = new Uri($"http://127.0.0.1:{ReservePort()}/");
            var listener = new HttpListener { IgnoreWriteExceptions = true };
            listener.Prefixes.Add(address.AbsoluteUri);
            try
            {
                listener.Start();
                return new InteractiveHttpServer(listener, address, configuration.TargetPath, invocationDirectory);
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
        var requests = new List<Task>();
        try
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

                requests.RemoveAll(request => request.IsCompleted);
                requests.Add(Task.Run(() => Handle(context)));
            }
        }
        finally
        {
            await Task.WhenAll(requests);
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
                var asset = InteractiveAssets.JavaScript.ForRequest(context.Request.Headers["Accept-Encoding"]);
                if (asset.ContentEncoding is not null)
                    context.Response.Headers["Content-Encoding"] = asset.ContentEncoding;
                await Write(context.Response, 200, "text/javascript; charset=utf-8", asset.Content);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/app.css")
            {
                await Write(context.Response, 200, "text/css; charset=utf-8", InteractiveAssets.Css);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path is "/dresssharp.svg" or "/favicon.svg" or "/favicon.ico")
            {
                var (contentType, content) = path switch
                    {
                        "/dresssharp.svg" => ("image/svg+xml", InteractiveAssets.Icon),
                        "/favicon.svg" => ("image/svg+xml", InteractiveAssets.FaviconSvg),
                        _ => ("image/x-icon", InteractiveAssets.FaviconIco)
                    };
                await Write(context.Response, 200, contentType, content);
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/bootstrap")
            {
                await Write(context.Response, 200, "application/json; charset=utf-8", CreateBootstrap());
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/configuration")
            {
                await WriteConfiguration(context.Response, await LoadConfiguration());
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/configuration")
            {
                if (!TokenMatches(context.Request.Headers[CsrfHeader]))
                {
                    await WriteError(context.Response, 403, "forbidden", "Forbidden");
                    return;
                }
                var request = await JsonSerializer.DeserializeAsync<SaveRequest>(
                    context.Request.InputStream,
                    JsonOptions,
                    _stopping.Token) ?? throw new JsonException("A request body is required.");
                if (request.Edits is null)
                    throw new JsonException("Edits are required.");
                var edits = request.Edits.Select(ToEdit).ToArray();
                var current = await LoadConfiguration();
                await WriteConfiguration(context.Response, await InteractiveEditorConfig.MergeAsync(current, edits, _stopping.Token));
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/preview")
            {
                var request = await JsonSerializer.DeserializeAsync<PreviewRequest>(
                    context.Request.InputStream,
                    JsonOptions,
                    _stopping.Token)
                    ?? throw new JsonException("A request body is required.");
                if (request.Source is null || request.Preferences is null)
                    throw new JsonException("Source and preferences are required.");
                var preferences = request.Preferences.Select(preference =>
                    {
                        if (preference.Local is null || preference.Inherited is null)
                            throw new JsonException("Local and inherited assignments are required.");
                        var local = ToEdit(new SaveEdit(preference.Key, preference.Local.Kind, preference.Local.Value));
                        var inherited = ToEdit(new SaveEdit(preference.Key, preference.Inherited.Kind, preference.Inherited.Value));
                        return new InteractivePreference(local.RuleKey, local.DesiredLocal, inherited.DesiredLocal, null, null, null);
                    }).ToArray();
                await WriteJson(context.Response, 200, await InteractivePreview.Format(request.Source, preferences, _stopping.Token));
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

            if (path is "/" or "/app.js" or "/app.css" or "/dresssharp.svg" or "/favicon.svg" or "/favicon.ico" or "/api/bootstrap" or "/api/configuration" or "/api/shutdown" or "/api/preview")
            {
                context.Response.Headers[HttpResponseHeader.Allow] = path is "/api/shutdown" or "/api/preview" ? "POST"
                    : path is "/api/configuration" ? "GET, POST"
                    : "GET";
                await Write(context.Response, 405, "text/plain; charset=utf-8", "Method not allowed");
                return;
            }

            await Write(context.Response, 404, "text/plain; charset=utf-8", "Not found");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            await WriteError(context.Response, 400, "invalid_request", exception.Message);
        }
        catch (ConfigurationException exception)
        {
            await WriteError(context.Response, 409, "configuration_unavailable", exception.Message);
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException)
        {
            context.Response.Abort();
        }
        catch (Exception)
        {
            await WriteError(context.Response, 500, "internal_error", "DressSharp could not complete the request.");
        }
    }

    byte[] CreateBootstrap()
    {
        var rules = RuleCatalog.BuiltIn.Rules.Select(rule => new
            {
                key = rule.Metadata.RuleKey.ToName(),
                expandedCaption = rule.Metadata.ExpandedCaption,
                group = rule.Metadata.GroupName,
                subgroup = rule.Metadata.SubgroupName,
                caption = rule.Metadata.Caption,
                description = rule.Metadata.Description,
                example = rule.Metadata.Example,
                examplePreferences = rule.Metadata.ExamplePreferences.ToDictionary(pair => pair.Key.ToName(), pair => pair.Value),
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
        return JsonSerializer.SerializeToUtf8Bytes(
            new
                {
                    csrfToken = _csrfToken,
                    requestedConfig = _targetPath,
                    catalog = new { version = RuleCatalog.BuiltIn.Version, rules },
                },
            JsonOptions);
    }

    async Task<InteractiveEditorConfigData> LoadConfiguration() =>
        await InteractiveEditorConfig.LoadAsync(_targetPath, _invocationDirectory, _stopping.Token);

    static InteractivePreferenceEdit ToEdit(SaveEdit edit)
    {
        if (!RuleKeys.TryParse(edit.Key, out var key))
            throw new ArgumentException($"Unknown preference '{edit.Key}'.");
        var desired = edit.Kind switch
            {
                "absent" when edit.Value is null => PreferenceAssignment.Absent,
                "unset" when edit.Value is null => PreferenceAssignment.Unset,
                "explicit" when edit.Value is not null => PreferenceAssignment.Explicit(edit.Value),
                _ => throw new ArgumentException($"Invalid assignment for '{edit.Key}'.")
            };
        return new InteractivePreferenceEdit(key, desired);
    }

    static async Task WriteConfiguration(HttpListenerResponse response, InteractiveEditorConfigData data) =>
        await WriteJson(
        response,
        200,
        new
            {
                targetPath = data.TargetPath,
                interactiveRoot = data.InteractiveRoot,
                revision = data.Revision,
                preferences = data.Preferences.Select(preference => new
                    {
                        key = preference.RuleKey.ToName(),
                        local = Assignment(preference.Local),
                        inherited = Assignment(preference.Inherited),
                        inheritedSourcePath = preference.InheritedSourcePath,
                        effectiveValue = preference.EffectiveValue,
                        effectiveSourcePath = preference.EffectiveSourcePath,
                    })
            });

    static object Assignment(PreferenceAssignment assignment) => new
        {
            kind = assignment.Kind.ToString().ToLowerInvariant(),
            assignment.Value,
        };

    static async Task WriteError(HttpListenerResponse response, int status, string code, string message) =>
        await WriteJson(response, status, new { code, message });

    static async Task WriteJson(HttpListenerResponse response, int status, object value) =>
        await Write(response, status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

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
        // Pierre generates shadow-DOM styles; Shiki colors tokens with style attributes.
        response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
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

    sealed record SaveRequest(IReadOnlyList<SaveEdit> Edits);

    sealed record SaveEdit(string Key, string Kind, string? Value);

    sealed record PreviewRequest(string Source, IReadOnlyList<PreviewPreference> Preferences);

    sealed record PreviewPreference(string Key, PreviewAssignment Local, PreviewAssignment Inherited);

    sealed record PreviewAssignment(string Kind, string? Value);
}
