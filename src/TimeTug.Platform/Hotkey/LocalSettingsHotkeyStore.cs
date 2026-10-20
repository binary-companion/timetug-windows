using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.Platform.Hotkey;

/// <summary>
/// Keeps the popup shortcut in the app's local settings (<c>ApplicationData.Current.LocalSettings.Values</c>, which is an
/// <see cref="IDictionary{TKey, TValue}"/> of strings and numbers), as one string. Never throws: a container that cannot be read gives
/// "no shortcut", one that cannot be written drops the value, and either is logged.
/// </summary>
public sealed class LocalSettingsHotkeyStore(IDictionary<string, object> values, string key = LocalSettingsHotkeyStore.DefaultKey, IDiagnostics? diagnostics = null) : IHotkeyStore
{
    public const string DefaultKey = "Hotkey.State";

    /// <summary>The store over the package's local settings. Needs package identity.</summary>
    public static LocalSettingsHotkeyStore ForCurrentPackage(IDiagnostics? diagnostics = null) =>
        new(Windows.Storage.ApplicationData.Current.LocalSettings.Values, DefaultKey, diagnostics);

    public HotkeyState Load()
    {
        try
        {
            return values.TryGetValue(key, out var stored) && stored is string text ? HotkeyState.FromStorage(text) : HotkeyState.None;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or KeyNotFoundException or NotSupportedException)
        {
            diagnostics?.Record("warning", "hotkey", $"the stored shortcut could not be read ({e.GetType().Name})");
            return HotkeyState.None;
        }
    }

    public void Save(HotkeyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            values[key] = state.ToStorage();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            diagnostics?.Record("warning", "hotkey", $"the shortcut could not be stored ({e.GetType().Name})");
        }
    }
}
