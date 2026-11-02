namespace TimeTug.Presentation.Accounts;

/// <summary>
/// Remembers that the person removed the Windows Microsoft account, so the first-run Continue never adds it automatically again (spec A1.3). Host-local: the
/// engine has no place for it. It holds one flag and no account data. The Windows implementation is over <c>ApplicationData.LocalSettings</c>.
/// </summary>
public interface IWindowsAccountDismissalStore
{
    /// <summary>True once a removal was recorded. Never throws; a store that cannot be read says false.</summary>
    bool IsDismissed { get; }

    /// <summary>Records the removal. Never throws; a store that cannot write drops it.</summary>
    void Dismiss();
}
