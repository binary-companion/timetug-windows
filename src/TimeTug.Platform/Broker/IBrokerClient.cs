using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Broker;

/// <summary>
/// The Windows account broker, as TimeTug uses it. The only type family that knows MSAL (<see cref="MsalBrokerClient"/> implements it); the host
/// and every rule above it are tested with a fake. Methods return an outcome for every expected failure and throw only for cancellation of the call itself.
/// </summary>
public interface IBrokerClient
{
    /// <summary>The accounts the broker lists without showing anything (no token is requested).</summary>
    ValueTask<IReadOnlyList<BrokerAccount>> ListAccountsAsync(CancellationToken cancellationToken);

    /// <summary>A token with no user interface. <paramref name="email"/> finds the account again when its id no longer matches.</summary>
    ValueTask<BrokerOutcome> AcquireSilentAsync(string accountId, string? email, IReadOnlyList<string> scopes, bool forceRefresh, CancellationToken cancellationToken);

    /// <summary>
    /// A token through the Windows dialog. With <paramref name="accountId"/> (a reconnect) the dialog is for that account; otherwise <paramref name="intent"/>
    /// says whether it is the Windows account or the account picker. Cancelling the token closes the dialog.
    /// </summary>
    ValueTask<BrokerOutcome> AcquireInteractiveAsync(BrokerIntent intent, string? accountId, string? email, IReadOnlyList<string> scopes, CancellationToken cancellationToken);
}
