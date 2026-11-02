using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Settings;

/// <summary>
/// Keeps the "the person removed the Windows account" flag in the app's local settings (<c>ApplicationData.Current.LocalSettings.Values</c>) as one boolean.
/// Never throws: a container that cannot be read says "not dismissed", one that cannot be written drops the value, and either is logged without any account data.
/// </summary>
public sealed class LocalSettingsWindowsAccountDismissalStore(IDictionary<string, object> values, string key = LocalSettingsWindowsAccountDismissalStore.DefaultKey, IDiagnostics? diagnostics = null)
    : IWindowsAccountDismissalStore
{
    public const string DefaultKey = "Accounts.WindowsAccountDismissed";

    /// <summary>The store over the package's local settings. Needs package identity.</summary>
    public static LocalSettingsWindowsAccountDismissalStore ForCurrentPackage(IDiagnostics? diagnostics = null) =>
        new(Windows.Storage.ApplicationData.Current.LocalSettings.Values, DefaultKey, diagnostics);

    public bool IsDismissed
    {
        get
        {
            try
            {
                return values.TryGetValue(key, out var stored) && stored is true;
            }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or KeyNotFoundException or NotSupportedException)
            {
                diagnostics?.Record("warning", "accounts", $"the stored choice about the Windows account could not be read ({e.GetType().Name})");
                return false;
            }
        }
    }

    public void Dismiss()
    {
        try
        {
            values[key] = true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            diagnostics?.Record("warning", "accounts", $"the choice about the Windows account could not be stored ({e.GetType().Name})");
        }
    }
}
