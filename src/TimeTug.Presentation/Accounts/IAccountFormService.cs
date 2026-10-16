namespace TimeTug.Presentation.Accounts;

/// <summary>One field of a sign-in form.</summary>
public sealed record AccountFormField(string Key, string Label, bool IsSecret);

/// <summary>Help text shown with a sign-in form, with an optional link.</summary>
public sealed record AccountFormHelp(string Text, string? LinkTitle = null, string? Url = null);

/// <summary>What to show: the form title and help, the fields, prefilled values (non-secret fields only) and the previous attempt's error.</summary>
public sealed record AccountFormRequest(
    string Title,
    AccountFormHelp? Help,
    IReadOnlyList<AccountFormField> Fields,
    IReadOnlyDictionary<string, string> Values,
    string? Error);

/// <summary>
/// The in-app sign-in form (a dialog, implemented by the App). <see cref="ShowAsync"/> returns what the person entered,
/// by field key, or null when they cancelled. When <c>cancellationToken</c> is cancelled the form must close and the
/// returned task must end with a cancellation.
/// </summary>
public interface IAccountFormService
{
    ValueTask<IReadOnlyDictionary<string, string>?> ShowAsync(AccountFormRequest request, CancellationToken cancellationToken);
}
