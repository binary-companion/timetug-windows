using System.Collections.Concurrent;
using TimeTug.Engine;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Broker;

/// <summary>
/// Supplies the engine's access tokens from the Windows account broker. Silent requests never show UI; the Windows dialog opens only from
/// <see cref="SignInAsync"/>, which takes the intent the view model armed. Exception messages are codes, never Windows text; the log carries the step
/// and the outcome only.
/// </summary>
public sealed class BrokeredTokenHost : IExternalTokenHost, IDisposable
{
    private readonly IBrokerClient _broker;
    private readonly IBrokerSignInIntent _intents;
    private readonly IDiagnostics? _diagnostics;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _signIns = new(StringComparer.Ordinal);

    public BrokeredTokenHost(IBrokerClient broker, IBrokerSignInIntent intents, IDiagnostics? diagnostics = null)
    {
        _broker = broker;
        _intents = intents;
        _diagnostics = diagnostics;
    }

    public async ValueTask<ExternalTokenResult> GetTokenAsync(ExternalTokenRequest request, CancellationToken cancellationToken)
    {
        var outcome = await RunAsync("silent", () => _broker.AcquireSilentAsync(request.AccountID, request.Email, request.Scopes, request.ForceRefresh, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        switch (outcome)
        {
            case BrokerOutcome.Ok ok:
                return ExternalTokenResult.Ok(ok.AccessToken, ok.ExpiresAt);
            case BrokerOutcome.UiRequired ui:
                return ExternalTokenResult.InteractionRequired(BrokerReason.Normalize(ui.Reason));
            case BrokerOutcome.Blocked blocked:
                // A silent request has no "blocked" answer; the person signs in again and that dialog says why.
                return ExternalTokenResult.InteractionRequired(BrokerReason.Normalize(blocked.Reason));
            case BrokerOutcome.Unavailable unavailable:
                return ExternalTokenResult.Unavailable(BrokerReason.Normalize(unavailable.Reason));
            default:
                throw Fail(outcome);
        }
    }

    public async ValueTask<ExternalSignInResult> SignInAsync(ExternalSignInRequest request, CancellationToken cancellationToken)
    {
        var intent = _intents.Take();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _signIns[request.SignInID] = linked;
        try
        {
            var outcome = await RunAsync("interactive", () => _broker.AcquireInteractiveAsync(intent, request.AccountID, request.Email, request.Scopes, linked.Token), linked.Token)
                .ConfigureAwait(false);

            // A person who cancelled in TimeTug must never get an account from a dialog that finished in the same instant.
            linked.Token.ThrowIfCancellationRequested();
            switch (outcome)
            {
                case BrokerOutcome.Ok ok:
                    return ExternalSignInResult.Ok(ok.AccountId, ok.AccessToken, ok.ExpiresAt);
                case BrokerOutcome.Blocked blocked:
                    return ExternalSignInResult.Blocked(BrokerReason.Normalize(blocked.Reason));
                case BrokerOutcome.Unavailable unavailable:
                    return ExternalSignInResult.Unavailable(BrokerReason.Normalize(unavailable.Reason));
                default:
                    throw Fail(outcome);
            }
        }
        finally
        {
            _signIns.TryRemove(new KeyValuePair<string, CancellationTokenSource>(request.SignInID, linked));
        }
    }

    public void CancelSignIn(string signInID)
    {
        try
        {
            if (_signIns.TryGetValue(signInID, out var source))
            {
                source.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
            // The sign-in ended between the lookup and the cancel: nothing to close.
        }
    }

    public void Dispose()
    {
        foreach (var source in _signIns.Values)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    // Runs one broker request. A broker that throws is a failed request, not a crash of the host; the exception message (which could carry account
    // data) is dropped.
    private async ValueTask<BrokerOutcome> RunAsync(string step, Func<ValueTask<BrokerOutcome>> request, CancellationToken cancellationToken)
    {
        BrokerOutcome outcome;
        try
        {
            outcome = await request().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _diagnostics?.Record("info", "broker", $"{step}: cancelled");
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics?.Record("warning", "broker", $"{step}: failed ({exception.GetType().Name})");
            return new BrokerOutcome.Failed();
        }

        cancellationToken.ThrowIfCancellationRequested();
        _diagnostics?.Record("info", "broker", $"{step}: {outcome.GetType().Name}");
        return outcome;
    }

    private static Exception Fail(BrokerOutcome outcome) => outcome switch
    {
        BrokerOutcome.Cancelled => new OperationCanceledException(),
        BrokerOutcome.Network => new EngineServiceException(ServiceErrorKind.Network, "network"),
        _ => new EngineServiceException(ServiceErrorKind.Failed, "failed"),
    };
}
