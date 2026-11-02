using TimeTug.Platform.Broker;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Tests.Broker;

/// <summary>A scripted <see cref="IBrokerClient"/> that records every call. An interactive call can be held open until the test releases it or cancels its token.</summary>
internal sealed class FakeBrokerClient : IBrokerClient
{
    private readonly object _gate = new();

    public List<string> Calls { get; } = [];

    public List<(BrokerIntent Intent, string? AccountId, string? Email, IReadOnlyList<string> Scopes)> InteractiveRequests { get; } = [];

    public List<(string AccountId, string? Email, IReadOnlyList<string> Scopes, bool ForceRefresh)> SilentRequests { get; } = [];

    public IReadOnlyList<BrokerAccount> Accounts { get; set; } = [];

    /// <summary>When set, <see cref="ListAccountsAsync"/> throws this.</summary>
    public Exception? ListThrows { get; set; }

    /// <summary>When set, <see cref="ListAccountsAsync"/> answers only when the test completes it, and ignores its cancellation token (a broker that never answers).</summary>
    public TaskCompletionSource<IReadOnlyList<BrokerAccount>>? ListGate { get; set; }

    public BrokerOutcome Silent { get; set; } = new BrokerOutcome.Failed();

    public BrokerOutcome Interactive { get; set; } = new BrokerOutcome.Failed();

    /// <summary>When set, <see cref="AcquireInteractiveAsync"/> throws this instead of returning an outcome.</summary>
    public Exception? InteractiveThrows { get; set; }

    public Exception? SilentThrows { get; set; }

    /// <summary>When true an interactive call waits for <see cref="Release"/>; cancelling its token ends it with <see cref="OperationCanceledException"/>.</summary>
    public bool HoldInteractive { get; set; }

    private readonly List<TaskCompletionSource> _held = [];

    public int HeldCount
    {
        get
        {
            lock (_gate)
            {
                return _held.Count;
            }
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            foreach (var held in _held)
            {
                held.TrySetResult();
            }
        }
    }

    public ValueTask<IReadOnlyList<BrokerAccount>> ListAccountsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Calls.Add("list");
        }

        if (ListThrows is { } error)
        {
            throw error;
        }

        return ListGate is { } gate ? new ValueTask<IReadOnlyList<BrokerAccount>>(gate.Task) : ValueTask.FromResult(Accounts);
    }

    public ValueTask<BrokerOutcome> AcquireSilentAsync(string accountId, string? email, IReadOnlyList<string> scopes, bool forceRefresh, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Calls.Add("silent");
            SilentRequests.Add((accountId, email, scopes, forceRefresh));
        }

        return SilentThrows is { } error ? throw error : ValueTask.FromResult(Silent);
    }

    public async ValueTask<BrokerOutcome> AcquireInteractiveAsync(BrokerIntent intent, string? accountId, string? email, IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        TaskCompletionSource? hold = null;
        lock (_gate)
        {
            Calls.Add("interactive");
            InteractiveRequests.Add((intent, accountId, email, scopes));
            if (HoldInteractive)
            {
                hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _held.Add(hold);
            }
        }

        if (hold is not null)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        return InteractiveThrows is { } error ? throw error : Interactive;
    }
}
