namespace TimeTug.Presentation.Settings;

/// <summary>
/// What the shortcut recorder needs from the global shortcut service (implemented by <c>HotkeyService</c> in Platform, faked in tests):
/// the state to show, the change that sets or clears the shortcut, and the pause that lets the chord already registered reach the
/// recorder instead of Windows.
/// </summary>
public interface IHotkeyControl
{
    /// <summary>The shortcut, whether it works and whether another program held one that was tried (also after a restart).</summary>
    HotkeyState State { get; }

    /// <summary>The state changed (a shortcut was set, cleared or found in conflict).</summary>
    event Action? StateChanged;

    /// <summary>Sets the shortcut (null clears it). The answer says whether it is registered, in conflict or invalid. Setting ends a suspension.</summary>
    Task<HotkeyResult> SetAsync(HotkeyBinding? binding);

    /// <summary>
    /// Releases the registered shortcut with Windows without forgetting it, so that pressing it while the recorder has focus reaches the
    /// recorder and does not fire the popup. Every call must be followed by <see cref="Resume"/> or a <see cref="SetAsync"/>.
    /// </summary>
    Task SuspendForRecording();

    /// <summary>Registers the shortcut that <see cref="SuspendForRecording"/> released. Nothing happens when it is not suspended.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716", Justification = "The service's own name for it, as in the plan; only C# implements this.")]
    Task Resume();
}
