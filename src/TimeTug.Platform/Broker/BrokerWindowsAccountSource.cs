using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Broker;

/// <summary>
/// Says which Microsoft accounts Windows has, from the broker's account listing, which asks for no token and shows nothing. The mapping is the spike's S2 verdict
/// (<c>docs/spikes/2026-11-wam-broker.md</c>): zero accounts is <see cref="WindowsListingKind.None"/>; exactly one account whose user name contains an <c>@</c>
/// is <see cref="WindowsListingKind.OneNamed"/>; any other non-empty listing is <see cref="WindowsListingKind.Unnamed"/>; a throw, or no answer within
/// <see cref="Limit"/>, is <see cref="WindowsListingKind.Unavailable"/> and leaves a diagnostics line with the step name only.
/// </summary>
public sealed class BrokerWindowsAccountSource(IBrokerClient broker, IDiagnostics? diagnostics = null, TimeProvider? clock = null) : IWindowsAccountSource
{
    /// <summary>The first listing after the app has been idle took about 1.6 s in the spike and later ones about 0.1 s.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    public async ValueTask<WindowsAccountListing> GetListingAsync(CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var call = Start(attempt.Token);
        try
        {
            var accounts = await call.WaitAsync(Limit, clock ?? TimeProvider.System, cancellationToken).ConfigureAwait(false);
            return Map(accounts);
        }
        catch (TimeoutException)
        {
            // The broker may ignore the token, so the wait is abandoned rather than awaited; nobody must see its late failure.
            _ = call.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await attempt.CancelAsync().ConfigureAwait(false);
            diagnostics?.Record("warning", "broker", "listing: no answer in time");
            return new WindowsAccountListing(WindowsListingKind.Unavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = call.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            diagnostics?.Record("warning", "broker", $"listing: failed ({ex.GetType().Name})");
            return new WindowsAccountListing(WindowsListingKind.Unavailable);
        }
    }

    private Task<IReadOnlyList<BrokerAccount>> Start(CancellationToken token)
    {
        try
        {
            return broker.ListAccountsAsync(token).AsTask();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Task.FromException<IReadOnlyList<BrokerAccount>>(ex);
        }
    }

    private static WindowsAccountListing Map(IReadOnlyList<BrokerAccount> accounts) => accounts.Count switch
    {
        0 => new WindowsAccountListing(WindowsListingKind.None),
        1 when accounts[0].Email?.Trim() is { } email && email.Contains('@', StringComparison.Ordinal) => new WindowsAccountListing(WindowsListingKind.OneNamed, email),
        _ => new WindowsAccountListing(WindowsListingKind.Unnamed),
    };
}
