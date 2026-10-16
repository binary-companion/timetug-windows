using TimeTug.Engine;
using TimeTug.Platform.Auth;
using TimeTug.Platform.Credentials;
using TimeTug.Platform.Http;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform;

/// <summary>The Windows services the shared engine asks for: Credential Locker, HTTP, browser and form sign-in, and diagnostics. There is no on-device text generation.</summary>
public sealed class WindowsEngineHost : IEngineHost, IDisposable
{
    private readonly IDiagnostics _diagnostics;
    private readonly IDisposable? _owned;

    public WindowsEngineHost(ICredentialStore credentials, IHttpTransport http, IAuthorizationInteraction authorization,
                             IDiagnostics diagnostics, IDisposable? owned = null)
    {
        Credentials = credentials;
        Http = http;
        Authorization = authorization;
        _diagnostics = diagnostics;
        _owned = owned;
    }

    public ICredentialStore Credentials { get; }

    public IHttpTransport Http { get; }

    public IAuthorizationInteraction Authorization { get; }

    public ITextGenerator? TextGenerator => null;

    /// <summary>Builds the real services: Credential Locker, the shared HTTP client, loopback browser sign-in and the given form dialog.</summary>
    public static WindowsEngineHost Create(IUrlLauncher launcher, IAccountFormService forms, IDiagnostics diagnostics)
    {
        var store = new CredentialLockerStore(new WindowsPasswordVault(), diagnostics);
        var loopback = new LoopbackAuthorization(launcher, diagnostics);
        var authorization = new CompositeAuthorization(loopback, new CredentialFormPrompter(forms));
        return new WindowsEngineHost(store, HttpClientTransport.CreateDefault(diagnostics), authorization, diagnostics,
                                     new CompositeDisposable(store, loopback));
    }

    public void Diagnostic(EngineDiagnostic diagnostic) => _diagnostics.Record(diagnostic.Level, diagnostic.Category, diagnostic.Message);

    public void Dispose() => _owned?.Dispose();

    private sealed class CompositeDisposable(params IDisposable[] items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }
}
