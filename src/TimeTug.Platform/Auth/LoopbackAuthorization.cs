using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TimeTug.Engine;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Auth;

/// <summary>
/// The browser half of sign-in: an OAuth redirect caught on <c>http://127.0.0.1:port/</c>. <see cref="HttpListener"/>
/// cannot bind port 0, so a free port is found with a <see cref="TcpListener"/> first and the listener then bound to that
/// explicit prefix, retrying when another process takes the port in between.
/// </summary>
public sealed class LoopbackAuthorization : IDisposable
{
    private const int Attempts = 5;
    private const string ClosePage =
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>TimeTug</title>" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"></head>" +
        "<body style=\"font-family:Segoe UI,sans-serif;text-align:center;margin-top:20vh\">" +
        "<h1>You can close this tab</h1><p>Return to TimeTug to finish signing in.</p></body></html>";

    private readonly IUrlLauncher _launcher;
    private readonly IDiagnostics? _diagnostics;
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public LoopbackAuthorization(IUrlLauncher launcher, IDiagnostics? diagnostics = null)
    {
        _launcher = launcher;
        _diagnostics = diagnostics;
    }

    public ValueTask<RedirectSession> BeginRedirectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Exception? last = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var port = FindFreePort();
            var prefix = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                listener.Close();
                last = ex;
                if (ex.ErrorCode == 5)
                {
                    break;   // access denied: another port will not help
                }

                continue;   // taken in between
            }

            var id = Guid.NewGuid().ToString("N");
            _sessions[id] = new Session(listener, prefix);
            return ValueTask.FromResult(new RedirectSession(id, prefix));
        }

        var kind = last is HttpListenerException { ErrorCode: 5 } ? ServiceErrorKind.Denied : ServiceErrorKind.Failed;
        _diagnostics?.Record("error", "signin", $"Could not start the loopback listener on 127.0.0.1 ({last?.GetType().Name}, code {(last as HttpListenerException)?.ErrorCode}).");
        throw new EngineServiceException(kind, "TimeTug could not listen on the loopback address to finish browser sign-in. A security policy may block it.", last);
    }

    public async ValueTask<string> AuthorizeAsync(string sessionID, string url, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionID, out var session))
        {
            throw new EngineServiceException(ServiceErrorKind.NotFound, "The sign-in session is closed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await _launcher.LaunchAsync(url).ConfigureAwait(false))
        {
            _diagnostics?.Record("error", "signin", "Windows did not open the default browser for sign-in.");
            throw new EngineServiceException(ServiceErrorKind.Failed, "TimeTug could not open the default browser for sign-in.");
        }

        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
        while (true)
        {
            Task<HttpListenerContext> pending;
            try
            {
                pending = session.Listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or HttpListenerException)
            {
                throw new EngineServiceException(ServiceErrorKind.NotFound, "The sign-in session is closed.", ex);
            }

            var first = await Task.WhenAny(pending, cancelled.Task).ConfigureAwait(false);
            if (first == cancelled.Task)
            {
                _ = pending.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);   // observe the abandoned accept
                throw new EngineServiceException(ServiceErrorKind.Cancelled, "Sign-in was cancelled.");
            }

            HttpListenerContext context;
            try
            {
                context = await pending.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException or InvalidOperationException)
            {
                throw new EngineServiceException(ServiceErrorKind.NotFound, "The sign-in session was closed while waiting.", ex);
            }

            var rawUrl = context.Request.RawUrl ?? "/";
            if (rawUrl.StartsWith("/favicon", StringComparison.Ordinal))
            {
                Respond(context, 404, "");
                continue;
            }

            Respond(context, 200, ClosePage);
            return session.Prefix.TrimEnd('/') + rawUrl;
        }
    }

    public ValueTask CloseRedirectAsync(string sessionID, CancellationToken cancellationToken)
    {
        if (_sessions.TryRemove(sessionID, out var session))
        {
            session.Close();
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var id in _sessions.Keys)
        {
            if (_sessions.TryRemove(id, out var session))
            {
                session.Close();
            }
        }
    }

    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static void Respond(HttpListenerContext context, int status, string html)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = status;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // The browser went away; the redirect URL has been read already.
        }
    }

    private sealed class Session(HttpListener listener, string prefix)
    {
        public HttpListener Listener { get; } = listener;

        public string Prefix { get; } = prefix;

        public void Close()
        {
            try
            {
                Listener.Close();
            }
            catch (ObjectDisposedException)
            {
                // Already closed.
            }
        }
    }
}
