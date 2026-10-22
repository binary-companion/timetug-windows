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
}
