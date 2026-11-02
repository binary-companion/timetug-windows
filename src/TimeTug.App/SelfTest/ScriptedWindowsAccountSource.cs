#if SELFTEST
using TimeTug.Presentation.Accounts;

namespace TimeTug.App.SelfTest;

/// <summary>What Windows lists, scripted: the scenario sets the answer, can hold it back, and sees how often it was asked.</summary>
internal sealed class ScriptedWindowsAccountSource(WindowsAccountListing listing) : IWindowsAccountSource
{
    private readonly object _gate = new();
    private int _asked;

    public static ScriptedWindowsAccountSource Named(string email) => new(new WindowsAccountListing(WindowsListingKind.OneNamed, email));

    /// <summary>What the next question is answered with.</summary>
    public WindowsAccountListing Listing { get; set; } = listing;

    /// <summary>When set, the answer comes only when the scenario completes it (a slow broker).</summary>
    public TaskCompletionSource<WindowsAccountListing>? Gate { get; set; }

    public int Asked
    {
        get
        {
            lock (_gate)
            {
                return _asked;
            }
        }
    }

    public ValueTask<WindowsAccountListing> GetListingAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _asked++;
        }

        return Gate is { } gate ? new ValueTask<WindowsAccountListing>(gate.Task) : ValueTask.FromResult(Listing);
    }
}
#endif
