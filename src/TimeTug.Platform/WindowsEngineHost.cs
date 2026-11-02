using TimeTug.Engine;
using TimeTug.Platform.Auth;
using TimeTug.Platform.Broker;
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
    /// <param name="localizer">The landing page's words; English when null.</param>
    /// <param name="returnNotifier">Told when a browser sign-in redirect has come back, so the app can bring a window forward.</param>
    public static WindowsEngineHost Create(IUrlLauncher launcher, IAccountFormService forms, IDiagnostics diagnostics,
                                           ILocalizer? localizer = null, ISignInReturnNotifier? returnNotifier = null)
    {
        var store = new CredentialLockerStore(new WindowsPasswordVault(), diagnostics);
        var loopback = new LoopbackAuthorization(launcher, diagnostics, localizer, returnNotifier);
        var authorization = new CompositeAuthorization(loopback, new CredentialFormPrompter(forms));
        return new WindowsEngineHost(store, HttpClientTransport.CreateDefault(diagnostics), authorization, diagnostics,
                                     new CompositeDisposable(store, loopback));
    }

    /// <summary>
    /// As <see cref="Create(IUrlLauncher, IAccountFormService, IDiagnostics, ILocalizer, ISignInReturnNotifier)"/>, plus the Windows account broker: when the build has a Microsoft client id and
    /// <paramref name="brokerFactory"/> (default <see cref="MsalBrokerClient.TryCreate"/>) builds a client, the result is also an
    /// <see cref="IExternalTokenHost"/> and the engine offers "Microsoft account on this PC". Otherwise it is the plain host and the browser sign-in stands alone.
    /// </summary>
    public static IEngineHost Create(IUrlLauncher launcher, IAccountFormService forms, IDiagnostics diagnostics, string? microsoftClientId,
                                     IBrokerParentWindow parent, IBrokerSignInIntent intent, Func<string, IBrokerParentWindow, IBrokerClient?>? brokerFactory = null,
                                     ILocalizer? localizer = null, ISignInReturnNotifier? returnNotifier = null)
    {
        var plain = Create(launcher, forms, diagnostics, localizer, returnNotifier);
        if (string.IsNullOrWhiteSpace(microsoftClientId))
        {
            return plain;
        }

        var factory = brokerFactory ?? ((id, window) => MsalBrokerClient.TryCreate(id, window, diagnostics));
        IBrokerClient? client;
        try
        {
            client = factory(microsoftClientId, parent);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            diagnostics.Record("warning", "broker", $"create: unavailable ({ex.GetType().Name})");
            client = null;
        }

        return client is null ? plain : new BrokeredEngineHost(plain, new BrokeredTokenHost(client, intent, diagnostics), new BrokerWindowsAccountSource(client, diagnostics));
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
