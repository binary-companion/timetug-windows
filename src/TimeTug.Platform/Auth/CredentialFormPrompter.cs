using TimeTug.Engine;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Auth;

/// <summary>
/// The form half of sign-in. <c>PrepareSignInAsync</c> remembers the title, help, prefilled values and last error;
/// <c>PromptCredentialsAsync</c> shows them with the requested fields through <see cref="IAccountFormService"/>.
/// Only non-secret values are ever kept after a submit; a cancel is <see cref="ServiceErrorKind.Cancelled"/>.
/// </summary>
public sealed class CredentialFormPrompter
{
    private readonly IAccountFormService _forms;
    private readonly object _gate = new();
    private SignInForm _prepared = new("", null, new Dictionary<string, string>(), null);
    private IReadOnlyDictionary<string, string>? _lastNonSecret;

    public CredentialFormPrompter(IAccountFormService forms) => _forms = forms;

    public ValueTask PrepareSignInAsync(SignInForm form, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _prepared = form;
            _lastNonSecret = null;
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> PromptCredentialsAsync(
        IReadOnlyList<CredentialField> fields, CancellationToken cancellationToken)
    {
        SignInForm prepared;
        lock (_gate)
        {
            prepared = _prepared;
        }

        var nonSecretKeys = fields.Where(f => !f.IsSecret).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        var request = new AccountFormRequest(
            prepared.Title,
            prepared.Help is null ? null : new AccountFormHelp(prepared.Help.Text, prepared.Help.LinkTitle, prepared.Help.Url),
            [.. fields.Select(f => new AccountFormField(f.Key, f.Label, f.IsSecret))],
            prepared.Values.Where(v => nonSecretKeys.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value),
            prepared.Error);

        IReadOnlyDictionary<string, string>? entered;
        try
        {
            entered = await _forms.ShowAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new EngineServiceException(ServiceErrorKind.Cancelled, "Sign-in was cancelled.");
        }

        if (entered is null)
        {
            throw new EngineServiceException(ServiceErrorKind.Cancelled, "Sign-in was cancelled.");
        }

        lock (_gate)
        {
            _lastNonSecret = entered.Where(v => nonSecretKeys.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value);
        }

        return entered;
    }

    public ValueTask<IReadOnlyDictionary<string, string>?> LastNonSecretValuesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_lastNonSecret);
        }
    }
}
