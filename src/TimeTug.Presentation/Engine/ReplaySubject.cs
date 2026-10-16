namespace TimeTug.Presentation.Engine;

/// <summary>
/// A minimal subject: every emission goes to the current subscribers in order. With <c>replayLatest</c> a new
/// subscriber first receives the latest value (latest-wins); without it, only later values.
/// </summary>
internal sealed class Subject<T>(bool replayLatest, Action<Exception>? onObserverError = null) : IObservable<T>
{
    private readonly object _gate = new();
    private readonly List<IObserver<T>> _observers = [];
    private T? _latest;
    private bool _hasLatest;

    public IDisposable Subscribe(IObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            _observers.Add(observer);
            if (replayLatest && _hasLatest)
            {
                Deliver(observer, _latest!);
            }
        }

        return new Subscription(this, observer);
    }

    public void OnNext(T value)
    {
        lock (_gate)
        {
            _latest = value;
            _hasLatest = true;
            foreach (var observer in _observers.ToArray())
            {
                Deliver(observer, value);
            }
        }
    }

    private void Deliver(IObserver<T> observer, T value)
    {
        try
        {
            observer.OnNext(value);
        }
        catch (Exception e)
        {
            // One failing subscriber must not stop the stream for the others.
            onObserverError?.Invoke(e);
        }
    }

    private void Remove(IObserver<T> observer)
    {
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Subscription(Subject<T> owner, IObserver<T> observer) : IDisposable
    {
        public void Dispose() => owner.Remove(observer);
    }
}
