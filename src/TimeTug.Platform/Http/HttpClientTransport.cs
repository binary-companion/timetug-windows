using System.Net;
using System.Net.Http.Headers;
using TimeTug.Engine;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Http;

/// <summary>
/// The engine's HTTP transport over one shared <see cref="HttpClient"/> (system proxy and certificate store, no cookies,
/// no cache). 4xx and 5xx are results; a transport failure becomes <see cref="ServiceErrorKind.Network"/>. Failures are
/// logged with the host name only: never a path or query string, which can carry authorization codes.
/// </summary>
public sealed class HttpClientTransport : IHttpTransport
{
    private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient(CreateHandler(), disposeHandler: true)
    {
        Timeout = TimeSpan.FromSeconds(120),   // the engine times a request out after 60 seconds on its own
    });

    private readonly HttpClient _client;
    private readonly IDiagnostics? _diagnostics;

    public HttpClientTransport(HttpClient client, IDiagnostics? diagnostics = null)
    {
        _client = client;
        _diagnostics = diagnostics;
    }

    /// <summary>The process-wide client every transport made by <see cref="CreateDefault"/> uses.</summary>
    public static HttpClient SharedClient => Shared.Value;

    public static HttpClientTransport CreateDefault(IDiagnostics? diagnostics = null) => new(SharedClient, diagnostics);

    /// <summary>The handler of the shared client: system proxy and certificate store, no cookies.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        UseCookies = false,
        UseProxy = true,
        DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    public async ValueTask<EngineHttpResponse> SendAsync(EngineHttpRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new EngineServiceException(ServiceErrorKind.Failed, "The request address is not a valid http(s) URL.");
        }

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
        if (request.Body is not null)
        {
            message.Content = new ByteArrayContent(request.Body);
        }

        foreach (var (name, value) in request.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(name, value))
            {
                message.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        try
        {
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return new EngineHttpResponse((int)response.StatusCode, Flatten(response.Headers, response.Content.Headers), body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            // Host only: no path, no query, and not the exception text (it can repeat the full URL).
            _diagnostics?.Record("warning", "http", $"Request to {uri.Host} failed: {ex.GetType().Name}");
            throw new EngineServiceException(ServiceErrorKind.Network, $"The request to {uri.Host} did not complete ({ex.GetType().Name}).", ex);
        }
    }

    private static Dictionary<string, string> Flatten(HttpResponseHeaders headers, HttpContentHeaders contentHeaders)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers.Concat(contentHeaders))
        {
            result[header.Key] = string.Join(", ", header.Value);
        }

        return result;
    }
}
