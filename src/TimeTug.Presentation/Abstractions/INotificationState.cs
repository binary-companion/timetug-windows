namespace TimeTug.Presentation.Abstractions;

/// <summary>What Windows says about interrupting the user (exclusive full-screen, presentation mode, quiet time).</summary>
public interface INotificationState
{
    /// <summary>False for exclusive full-screen or presentation mode.</summary>
    bool CanShowFullScreen { get; }

    bool ToastsAllowed { get; }

    /// <summary>Raised when full-screen or presentation mode ends.</summary>
    event Action? FullScreenEnded;
}
