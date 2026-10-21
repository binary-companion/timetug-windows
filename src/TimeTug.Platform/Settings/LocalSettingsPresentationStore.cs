using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.Platform.Settings;

/// <summary>
/// Keeps <see cref="PresentationSettings"/> in the app's local settings (<c>ApplicationData.Current.LocalSettings.Values</c>, an
/// <see cref="IDictionary{TKey, TValue}"/> of strings and numbers), one string per field. Never throws: a container that cannot be read
/// gives the defaults, one that cannot be written drops the value, and either is logged. A field that is missing or corrupt is at its
/// default and the other fields are kept (see <see cref="PresentationSettingsCodec"/>).
/// </summary>
public sealed class LocalSettingsPresentationStore(IDictionary<string, object> values, IDiagnostics? diagnostics = null) : IPresentationSettingsStore
{
    /// <summary>The store over the package's local settings. Needs package identity.</summary>
    public static LocalSettingsPresentationStore ForCurrentPackage(IDiagnostics? diagnostics = null) =>
        new(Windows.Storage.ApplicationData.Current.LocalSettings.Values, diagnostics);

    public event Action? Changed;

    public PresentationSettings Load()
    {
        try
        {
            return PresentationSettingsCodec.Read(key => values.TryGetValue(key, out var stored) ? stored : null);
        }
        catch (Exception e) when (IsContainerFailure(e))
        {
            diagnostics?.Record("warning", "presentation-settings", $"the stored settings could not be read ({e.GetType().Name})");
            return PresentationSettings.Defaults;
        }
    }

    public void Save(PresentationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            foreach (var (key, value) in PresentationSettingsCodec.Write(settings))
            {
                if (value is null)
                {
                    values.Remove(key);
                }
                else
                {
                    values[key] = value;
                }
            }
        }
        catch (Exception e) when (IsContainerFailure(e))
        {
            diagnostics?.Record("warning", "presentation-settings", $"the settings could not be stored ({e.GetType().Name})");
        }

        Changed?.Invoke();
    }

    private static bool IsContainerFailure(Exception e) =>
        e is InvalidOperationException or System.Runtime.InteropServices.COMException or KeyNotFoundException or NotSupportedException;
}
