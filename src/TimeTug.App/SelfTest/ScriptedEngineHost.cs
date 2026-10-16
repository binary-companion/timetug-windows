#if SELFTEST
using TimeTug.Engine;

namespace TimeTug.App.SelfTest;

/// <summary>
/// The engine's host services for a scenario that runs the real engine but must touch nothing real: credentials in memory, no
/// network, no browser. Anything that would need the outside world is refused as the engine's own error kinds describe.
/// </summary>
internal sealed class ScriptedEngineHost : IEngineHost
{
    public List<EngineDiagnostic> Diagnostics { get; } = [];

    public ICredentialStore Credentials { get; } = new MemoryCredentials();

    public IHttpTransport Http { get; } = new OfflineTransport();

    public IAuthorizationInteraction Authorization { get; } = new RefusedAuthorization();

    public ITextGenerator? TextGenerator => null;

    public void Diagnostic(EngineDiagnostic diagnostic)
    {
        lock (Diagnostics)
        {
            Diagnostics.Add(diagnostic);
        }
    }

    private sealed class MemoryCredentials : ICredentialStore
    {
        private readonly Dictionary<string, (IReadOnlyDictionary<string, string> Secrets, int Revision)> _items = [];

        public ValueTask<IReadOnlyDictionary<string, string>?> GetAsync(string connectionID, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_items.TryGetValue(connectionID, out var item) ? item.Secrets : null);

        public ValueTask SetAsync(string connectionID, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken)
        {
            _items[connectionID] = (secrets, _items.TryGetValue(connectionID, out var old) ? old.Revision + 1 : 1);
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string connectionID, CancellationToken cancellationToken)
        {
            _items.Remove(connectionID);
            return ValueTask.CompletedTask;
        }

        public ValueTask<CredentialSnapshot?> SnapshotAsync(string connectionID, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_items.TryGetValue(connectionID, out var item) ? new CredentialSnapshot(item.Secrets, item.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null);

        public ValueTask<bool> UpdateRefreshTokenAsync(string connectionID, string token, string expectedRevision, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }

    private sealed class OfflineTransport : IHttpTransport
    {
        public ValueTask<EngineHttpResponse> SendAsync(EngineHttpRequest request, CancellationToken cancellationToken) =>
            throw new EngineServiceException(ServiceErrorKind.Network, "the self-test has no network");
    }

    private sealed class RefusedAuthorization : IAuthorizationInteraction
    {
        public ValueTask<RedirectSession> BeginRedirectAsync(CancellationToken cancellationToken) =>
            throw new EngineServiceException(ServiceErrorKind.Cancelled, "the self-test opens no browser");

        public ValueTask<string> AuthorizeAsync(string sessionID, string url, CancellationToken cancellationToken) =>
            throw new EngineServiceException(ServiceErrorKind.Cancelled, "the self-test opens no browser");

        public ValueTask CloseRedirectAsync(string sessionID, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyDictionary<string, string>> PromptCredentialsAsync(IReadOnlyList<CredentialField> fields, CancellationToken cancellationToken) =>
            throw new EngineServiceException(ServiceErrorKind.Cancelled, "the self-test shows no form");

        public ValueTask PrepareSignInAsync(SignInForm form, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyDictionary<string, string>?> LastNonSecretValuesAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(null);
    }
}
#endif
