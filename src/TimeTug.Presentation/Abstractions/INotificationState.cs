namespace TimeTug.Presentation.Abstractions;

/// <summary>What Windows says about interrupting the user (exclusive full-screen, presentation mode, quiet time).</summary>
public interface INotificationState
{
    /// <summary>False for exclusive full-screen or presentation mode.</summary>
    bool CanShowFullScreen { get; }

    bool ToastsAllowed { get; }

    /// <summary>Raised when full-screen or presentation mode ends (only while <see cref="SetWatchingForFullScreenEnd"/> was last called with true).</summary>
    event Action? FullScreenEnded;

    /// <summary>
    /// Windows has no notification for "full-screen ended", so the platform implementation polls. The tug controller
    /// turns the polling on while a request is held (or only a toast was shown) and off again afterwards.
    /// </summary>
    void SetWatchingForFullScreenEnd(bool watching);
}
