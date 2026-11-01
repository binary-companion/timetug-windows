#if SELFTEST
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Accounts;

namespace TimeTug.App.SelfTest;

/// <summary>
/// The Windows account broker, scripted for the brokered scenarios: it answers from the outcomes a scenario sets and records every call (kinds and intents only:
/// no token, email or account id), so a scenario can assert what the app asked of Windows. Tokens it hands out expire an hour after the app's clock.
/// </summary>
internal sealed class FakeBrokerClient(Func<DateTimeOffset> now) : IBrokerClient
{
    public const string AccountId = "acct-1";

    private readonly object _gate = new();
    private readonly List<string> _calls = [];

    /// <summary>The calls made so far, oldest first: <c>list</c>, <c>silent</c>, <c>interactive:WindowsAccount</c>, <c>interactive:PickAccount</c>.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>What <see cref="ListAccountsAsync"/> returns (the Windows account the welcome card names).</summary>
    public IReadOnlyList<BrokerAccount> Accounts { get; set; } = [new BrokerAccount(AccountId, "me@example.com")];

    /// <summary>The answer to a silent request; null: a token.</summary>
    public BrokerOutcome? Silent { get; set; }

    /// <summary>The answer to the Windows dialog; null: a token for the account.</summary>
    public BrokerOutcome? Interactive { get; set; }

    /// <summary>When true the Windows dialog never answers on its own (until cancelled), like a person who has not decided yet.</summary>
    public bool HoldInteractive { get; set; }

    /// <summary>How long the Windows dialog takes to answer: a person needs a moment, and the engine's "working" state must be seen before it ends.</summary>
    public TimeSpan InteractiveDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    public ValueTask<IReadOnlyList<BrokerAccount>> ListAccountsAsync(CancellationToken cancellationToken)
    {
        Record("list");
        return ValueTask.FromResult(Accounts);
    }

    public ValueTask<BrokerOutcome> AcquireSilentAsync(string accountId, string? email, IReadOnlyList<string> scopes, bool forceRefresh, CancellationToken cancellationToken)
    {
        Record("silent");
        return ValueTask.FromResult(Silent ?? Token());
    }

    public async ValueTask<BrokerOutcome> AcquireInteractiveAsync(BrokerIntent intent, string? accountId, string? email, IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        Record("interactive:" + intent);
        await Task.Delay(HoldInteractive ? Timeout.InfiniteTimeSpan : InteractiveDelay, cancellationToken);

        return Interactive ?? Token();
    }

    private BrokerOutcome.Ok Token() => new BrokerOutcome.Ok(AccountId, "fake-access-token", now().AddHours(1));

    private void Record(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }
}
#endif
