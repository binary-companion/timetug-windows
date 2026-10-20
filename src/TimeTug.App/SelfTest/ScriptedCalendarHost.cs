#if SELFTEST
using System.Reflection;
using TimeTug.Engine;

namespace TimeTug.App.SelfTest;

/// <summary>
/// The engine's host services for a scenario that runs the real engine but must touch nothing real: credentials in memory, no
/// browser, and HTTP answered from recorded responses in <c>tests/data</c> (embedded in development and CI builds). A request with
/// no scripted answer fails as a network error, so a scenario that does not expect the network never gets one.
/// </summary>
internal sealed class ScriptedCalendarHost : IEngineHost
{
    public List<EngineDiagnostic> Diagnostics { get; } = [];

    /// <summary>The credential store; a scenario seeds the secrets an account would have after signing in.</summary>
    public MemoryCredentials Secrets { get; } = new();

    /// <summary>The recorded HTTP answers.</summary>
    public ScriptedTransport Answers { get; } = new();

    public ICredentialStore Credentials => Secrets;

    public IHttpTransport Http => Answers;

    public IAuthorizationInteraction Authorization { get; } = new RefusedAuthorization();

    public ITextGenerator? TextGenerator => null;

    public void Diagnostic(EngineDiagnostic diagnostic)
    {
        lock (Diagnostics)
        {
            Diagnostics.Add(diagnostic);
        }
    }

    internal sealed class MemoryCredentials : ICredentialStore
    {
        // The engine requires the revision to be a UUID.
        private readonly Dictionary<string, (IReadOnlyDictionary<string, string> Secrets, string Revision)> _items = [];

        public void Seed(string connectionID, string key, string value) => _items[connectionID] = (new Dictionary<string, string> { [key] = value }, Guid.NewGuid().ToString());

        /// <summary>The store's operations the engine made, oldest first (names and connection ids only, never a secret).</summary>
        public List<string> Calls { get; } = [];

        public ValueTask<IReadOnlyDictionary<string, string>?> GetAsync(string connectionID, CancellationToken cancellationToken)
        {
            Calls.Add("get " + connectionID);
            return ValueTask.FromResult(_items.TryGetValue(connectionID, out var item) ? item.Secrets : null);
        }

        public ValueTask SetAsync(string connectionID, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken)
        {
            _items[connectionID] = (secrets, Guid.NewGuid().ToString());
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string connectionID, CancellationToken cancellationToken)
        {
            _items.Remove(connectionID);
            return ValueTask.CompletedTask;
        }

        public ValueTask<CredentialSnapshot?> SnapshotAsync(string connectionID, CancellationToken cancellationToken)
        {
            Calls.Add("snapshot " + connectionID);
            return ValueTask.FromResult(_items.TryGetValue(connectionID, out var item) ? new CredentialSnapshot(item.Secrets, item.Revision) : null);
        }

        public ValueTask<bool> UpdateRefreshTokenAsync(string connectionID, string token, string expectedRevision, CancellationToken cancellationToken)
        {
            Calls.Add("update-refresh-token " + connectionID);
            if (!_items.TryGetValue(connectionID, out var item) || item.Revision != expectedRevision)
            {
                return ValueTask.FromResult(false);
            }

            // Rotating the token keeps the revision (only a new sign-in changes it); the engine binds its cached access token to it.
            var secrets = new Dictionary<string, string>(item.Secrets) { ["refresh_token"] = token };
            _items[connectionID] = (secrets, item.Revision);
            return ValueTask.FromResult(true);
        }
    }

    /// <summary>Answers requests from recorded response files: the newest answer whose URL prefix (and method, when given) matches wins.</summary>
    internal sealed class ScriptedTransport : IHttpTransport
    {
        private readonly List<(string Prefix, string? Method, string File)> _answers = [];
        private readonly List<string> _requests = [];

        /// <summary>The method and URL (without its query) of every request the engine made, oldest first.</summary>
        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        /// <summary>From now on, a request whose URL starts with <paramref name="urlPrefix"/> is answered with the recorded file (HTTP 200).</summary>
        public void Answer(string urlPrefix, string file, string? method = null)
        {
            lock (_answers)
            {
                _answers.Add((urlPrefix, method, file));
            }
        }

        public ValueTask<EngineHttpResponse> SendAsync(EngineHttpRequest request, CancellationToken cancellationToken)
        {
            string? file = null;
            lock (_answers)
            {
                for (var i = _answers.Count - 1; i >= 0 && file is null; i--)
                {
                    var answer = _answers[i];
                    if (request.Url.StartsWith(answer.Prefix, StringComparison.Ordinal) && (answer.Method is null || string.Equals(answer.Method, request.Method, StringComparison.OrdinalIgnoreCase)))
                    {
                        file = answer.File;
                    }
                }
            }

            lock (_requests)
            {
                _requests.Add(request.Method + " " + request.Url.Split('?')[0] + " -> " + (file ?? "no recorded answer") + " at " + DateTime.UtcNow.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (file is null)
            {
                throw new EngineServiceException(ServiceErrorKind.Network, "the self-test has no recorded answer for " + request.Method + " " + request.Url.Split('?')[0]);
            }

            return ValueTask.FromResult(new EngineHttpResponse(200, new Dictionary<string, string> { ["content-type"] = "application/json" }, SelfTestData.ReadBytes(file)));
        }
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

/// <summary>The recorded responses and engine files under <c>tests/data</c>, which the development and CI builds embed in the app.</summary>
internal static class SelfTestData
{
    private const string Prefix = "TimeTug.SelfTest.Data.";

    public static byte[] ReadBytes(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + file)
            ?? throw new FileNotFoundException("the self-test data file is not embedded: " + file);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Writes the embedded file into <paramref name="folder"/> under <paramref name="name"/>.</summary>
    public static void CopyTo(string file, string folder, string name) =>
        File.WriteAllBytes(Path.Combine(folder, name), ReadBytes(file));
}
#endif
