namespace TimeTug.Presentation.Abstractions;

/// <summary>Whether the Windows session is locked.</summary>
public interface ISessionState
{
    bool IsLocked { get; }

    event Action? Locked;

    event Action? Unlocked;
}
