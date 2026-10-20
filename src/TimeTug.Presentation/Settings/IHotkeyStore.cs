namespace TimeTug.Presentation.Settings;

/// <summary>
/// Where the popup shortcut is kept between runs (an app-side presentation setting; the engine knows nothing of it). The default is
/// <see cref="HotkeyState.None"/>: nothing is registered until a person records a shortcut. The Windows implementation is over
/// <c>ApplicationData.LocalSettings</c>.
/// </summary>
public interface IHotkeyStore
{
    /// <summary>The stored state; <see cref="HotkeyState.None"/> when nothing is stored or it cannot be read. Never throws.</summary>
    HotkeyState Load();

    /// <summary>Stores the state. Never throws; a store that cannot write drops the value and the shortcut is gone at the next start.</summary>
    void Save(HotkeyState state);
}
