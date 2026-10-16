using TimeTug.Engine;

namespace TimeTug.Platform.Auth;

/// <summary>The engine's <see cref="IAuthorizationInteraction"/>: the browser flow from <see cref="LoopbackAuthorization"/>, the forms from <see cref="CredentialFormPrompter"/>.</summary>
public sealed class CompositeAuthorization : IAuthorizationInteraction
{
    private readonly LoopbackAuthorization _browser;
    private readonly CredentialFormPrompter _forms;

    public CompositeAuthorization(LoopbackAuthorization browser, CredentialFormPrompter forms)
    {
        _browser = browser;
        _forms = forms;
    }

    public ValueTask<RedirectSession> BeginRedirectAsync(CancellationToken cancellationToken) => _browser.BeginRedirectAsync(cancellationToken);

    public ValueTask<string> AuthorizeAsync(string sessionID, string url, CancellationToken cancellationToken) =>
        _browser.AuthorizeAsync(sessionID, url, cancellationToken);

    public ValueTask CloseRedirectAsync(string sessionID, CancellationToken cancellationToken) => _browser.CloseRedirectAsync(sessionID, cancellationToken);

    public ValueTask<IReadOnlyDictionary<string, string>> PromptCredentialsAsync(IReadOnlyList<CredentialField> fields, CancellationToken cancellationToken) =>
        _forms.PromptCredentialsAsync(fields, cancellationToken);

    public ValueTask PrepareSignInAsync(SignInForm form, CancellationToken cancellationToken) => _forms.PrepareSignInAsync(form, cancellationToken);

    public ValueTask<IReadOnlyDictionary<string, string>?> LastNonSecretValuesAsync(CancellationToken cancellationToken) =>
        _forms.LastNonSecretValuesAsync(cancellationToken);
}
