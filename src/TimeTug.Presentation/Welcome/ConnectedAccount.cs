using TimeTug.Presentation.Accounts;

namespace TimeTug.Presentation.Welcome;

/// <summary>
/// A connected account as the Settings pane shows it (the Mac's row): the provider, the name, the status in words, whether it needs the person
/// (orange on the Mac) and so offers "Sign in again", and the notice about repeating events the engine cannot read. All text is decided.
/// </summary>
public sealed record ConnectedAccount(
    string ConnectionId,
    string KindId,
    string KindName,
    string Name,
    string StatusText,
    bool NeedsAttention,
    bool CanSignInAgain,
    string? NoticeText,
    string AutomationName,
    string SignInAutomationName,
    string ActionsAutomationName,
    string RemoveAutomationName)
{
    public bool HasNotice => NoticeText is not null;

    /// <summary>The account is a Microsoft account, however it signs in: Microsoft holds the consent, so the removal question says where to withdraw it.</summary>
    public bool IsMicrosoft { get; init; }

    /// <summary>The account's email address when its name is one (used to tell whether the Windows account is connected already); null otherwise.</summary>
    public string? Email { get; init; }

    /// <summary>The vendor logo the row shows; <see cref="ProviderMark.None"/> when the kind has none.</summary>
    public ProviderMark Mark { get; init; }

    /// <summary>"Microsoft logo" or "Google logo": the logo image's name for a screen reader; empty when there is no vendor logo.</summary>
    public string LogoAutomationName { get; init; } = "";

    /// <summary>The words of the "Sign in again" button in the row.</summary>
    public string SignInText { get; init; } = "";
}
