using System.Threading.Channels;
using TimeTug.Engine;

namespace TimeTug.Presentation.Tests;

/// <summary>An in-memory IEngine the tests drive: push states and events, read the commands sent.</summary>
internal sealed class FakeEngine : IEngine
{
    private readonly Channel<EngineState> _states = Channel.CreateUnbounded<EngineState>();
    private readonly Channel<EngineEvent> _events = Channel.CreateUnbounded<EngineEvent>();
    private readonly List<(EngineCommand Command, string? Id)> _sent = [];

    public IReadOnlyList<EngineCommand> Commands
    {
        get
        {
            lock (_sent)
            {
                return _sent.Select(s => s.Command).ToList();
            }
        }
    }

    /// <summary>The command ids that were passed with the commands (null where none was).</summary>
    public IReadOnlyList<string?> Ids
    {
        get
        {
            lock (_sent)
            {
                return _sent.Select(s => s.Id).ToList();
            }
        }
    }

    public bool Disposed { get; private set; }

    public IAsyncEnumerable<EngineState> State => _states.Reader.ReadAllAsync();

    public IAsyncEnumerable<EngineEvent> Events => _events.Reader.ReadAllAsync();

    public void Push(EngineState state) => _states.Writer.TryWrite(state);

    public void Push(EngineEvent engineEvent) => _events.Writer.TryWrite(engineEvent);

    public ValueTask SendAsync(EngineCommand command, string? id = null)
    {
        lock (_sent)
        {
            _sent.Add((command, id));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _states.Writer.TryComplete();
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Records what an observable emits and lets a test wait for the nth item.</summary>
internal sealed class Collector<T> : IObserver<T>, IDisposable
{
    private readonly List<T> _items = [];
    private readonly IDisposable _subscription;

    public Collector(IObservable<T> source) => _subscription = source.Subscribe(this);

    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_items)
            {
                return _items.ToList();
            }
        }
    }

    public async Task<IReadOnlyList<T>> WaitForAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = Items;
            if (snapshot.Count >= count)
            {
                return snapshot;
            }

            await Task.Delay(5);
        }

        throw new TimeoutException($"Expected {count} item(s), got {Items.Count}.");
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void OnNext(T value)
    {
        lock (_items)
        {
            _items.Add(value);
        }
    }

    public void Dispose() => _subscription.Dispose();
}
