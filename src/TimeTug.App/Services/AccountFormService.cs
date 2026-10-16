using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TimeTug.App.Views;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;

namespace TimeTug.App;

/// <summary>
/// Shows the credential form as a dialog over the welcome window. The engine asks from its own thread; this hops to the UI
/// thread, shows one dialog at a time, and closes the dialog when the engine cancels the request.
/// </summary>
public sealed class AccountFormService(DispatcherQueue queue, ILocalizer text) : IAccountFormService, IDisposable
{
    private readonly SemaphoreSlim _one = new(1, 1);

    /// <summary>The root the dialog opens over. Set by the window on the UI thread; null when no window can host it (the form is then cancelled).</summary>
    public XamlRoot? XamlRoot { get; set; }

    public async ValueTask<IReadOnlyDictionary<string, string>?> ShowAsync(AccountFormRequest request, CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken);
        try
        {
            var result = new TaskCompletionSource<IReadOnlyDictionary<string, string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!queue.TryEnqueue(() => _ = RunAsync(request, result, cancellationToken)))
            {
                return null;
            }

            // The dialog always ends (the token hides it), so wait for the real outcome before the next form may open.
            return await result.Task;
        }
        finally
        {
            _one.Release();
        }
    }

    public void Dispose() => _one.Dispose();

    private async Task RunAsync(AccountFormRequest request, TaskCompletionSource<IReadOnlyDictionary<string, string>?> result, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.SetCanceled(cancellationToken);
                return;
            }

            if (XamlRoot is not { } root)
            {
                result.SetResult(null);
                return;
            }

            var dialog = new CredentialFormDialog(request, text) { XamlRoot = root };
            var shown = dialog.ShowAsync().AsTask();
            using var registration = cancellationToken.Register(() => queue.TryEnqueue(dialog.Hide));
            var outcome = await shown;
            if (cancellationToken.IsCancellationRequested)
            {
                result.SetCanceled(cancellationToken);
            }
            else
            {
                result.SetResult(outcome == ContentDialogResult.Primary ? dialog.Values : null);
            }
        }
        catch (Exception e)
        {
            result.TrySetException(e);
        }
    }
}
