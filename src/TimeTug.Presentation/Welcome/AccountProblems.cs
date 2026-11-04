using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Welcome;

/// <summary>Why a connected account needs the person, in words ("Sign in again"), for the places that show a short line instead of the account's full status.</summary>
public static class AccountProblems
{
    /// <summary>The words for an account that cannot be read right now, or null when it is fine (or its status has no words).</summary>
    public static string? For(AccountInfo account, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(text);
        if (account.BuildFailure)
        {
            return text.GetString("Status_buildFailure");
        }

        return account.Status == "ok" ? null : text.TryGetString("Status_" + account.Status);
    }
}
