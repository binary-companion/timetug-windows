using TimeTug.Engine;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Broker;

/// <summary>
/// The Windows engine host plus the capability to supply Microsoft tokens from the Windows account broker. A decorator because the engine decides the
/// capability by <c>host is IExternalTokenHost</c> and a C# class either implements the interface or not: <see cref="WindowsEngineHost"/>.Create returns this
/// only when the build has a Microsoft client id and the broker client could be built; otherwise the plain host is returned and the engine never offers the kind.
/// </summary>
public sealed class BrokeredEngineHost(IEngineHost inner, IExternalTokenHost tokens, IWindowsAccountSource? windowsAccounts = null) : IEngineHost, IExternalTokenHost, IDisposable
{
    /// <summary>Names the Windows account for the welcome card; null when the host was built without one.</summary>
    public IWindowsAccountSource? WindowsAccounts => windowsAccounts;

    public ICredentialStore Credentials => inner.Credentials;

    public IHttpTransport Http => inner.Http;

    public IAuthorizationInteraction Authorization => inner.Authorization;

    public ITextGenerator? TextGenerator => inner.TextGenerator;

    public void Diagnostic(EngineDiagnostic diagnostic) => inner.Diagnostic(diagnostic);

    public ValueTask<ExternalTokenResult> GetTokenAsync(ExternalTokenRequest request, CancellationToken cancellationToken) => tokens.GetTokenAsync(request, cancellationToken);

    public ValueTask<ExternalSignInResult> SignInAsync(ExternalSignInRequest request, CancellationToken cancellationToken) => tokens.SignInAsync(request, cancellationToken);

    public void CancelSignIn(string signInID) => tokens.CancelSignIn(signInID);

    public void Dispose()
    {
        (tokens as IDisposable)?.Dispose();
        (inner as IDisposable)?.Dispose();
    }
}
