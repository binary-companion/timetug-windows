namespace TimeTug.Presentation.Abstractions;

/// <summary>Runs work on the UI thread.</summary>
public interface IUiDispatcher
{
    bool HasThreadAccess { get; }

    void Post(Action action);

    Task InvokeAsync(Action action);
}
