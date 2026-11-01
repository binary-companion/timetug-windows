using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Broker;

/// <summary>
/// Names the account Windows is signed in with for the welcome card ("Use name@example.com"), from the broker's account listing, which asks for no token and
/// shows nothing. The rule is the spike's S2 verdict: exactly one listed account whose user name contains an <c>@</c>; anything else leaves the card unlabelled.
/// </summary>
public sealed class BrokerWindowsAccountSource(IBrokerClient broker) : IWindowsAccountSource
{
    public async ValueTask<string?> GetEmailAsync(CancellationToken cancellationToken)
    {
        var accounts = await broker.ListAccountsAsync(cancellationToken).ConfigureAwait(false);
        return accounts.Count == 1 && accounts[0].Email is { } email && email.Contains('@', StringComparison.Ordinal) ? email : null;
    }
}
