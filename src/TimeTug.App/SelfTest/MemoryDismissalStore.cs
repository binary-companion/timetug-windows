#if SELFTEST
using TimeTug.Presentation.Accounts;

namespace TimeTug.App.SelfTest;

/// <summary>The "the person removed the Windows account" flag, in memory: a self-test never touches the package's real settings, and can see what was recorded.</summary>
internal sealed class MemoryDismissalStore(bool dismissed = false) : IWindowsAccountDismissalStore
{
    public bool IsDismissed { get; private set; } = dismissed;

    public void Dismiss() => IsDismissed = true;

    /// <summary>Forgets the removal (a scenario that walks two ways of removing the Windows account starts the second from a first run again).</summary>
    public void Clear() => IsDismissed = false;
}
#endif
