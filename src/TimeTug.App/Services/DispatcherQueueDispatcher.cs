using Microsoft.UI.Dispatching;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.App;

/// <summary>Runs work on the UI thread through the window's dispatcher queue.</summary>
public sealed class DispatcherQueueDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;

    public void Post(Action action) => queue.TryEnqueue(() => action());

    public Task InvokeAsync(Action action)
    {
        if (queue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }))
        {
            done.SetException(new InvalidOperationException("The UI thread is no longer accepting work."));
        }

        return done.Task;
    }
}
