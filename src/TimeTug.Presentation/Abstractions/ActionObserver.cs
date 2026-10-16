namespace TimeTug.Presentation.Abstractions;

/// <summary>An observer built from a delegate, so view models can subscribe without a reactive library.</summary>
internal sealed class ActionObserver<T>(Action<T> onNext) : IObserver<T>
{
    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void OnNext(T value) => onNext(value);
}
