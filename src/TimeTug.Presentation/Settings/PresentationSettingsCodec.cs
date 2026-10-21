namespace TimeTug.Presentation.Settings;

/// <summary>
/// How <see cref="PresentationSettings"/> become the plain values a settings container keeps (one string per field), and back. Each field
/// is read on its own: a missing, mistyped or corrupt value gives that field's default and leaves the others alone. The container is
/// <c>ApplicationData.LocalSettings.Values</c> on Windows and a dictionary in tests.
/// </summary>
public static class PresentationSettingsCodec
{
    public const string ThemeKey = "Presentation.Theme";
    public const string TrayStyleKey = "Presentation.TrayStyle";
    public const string BackdropKey = "Presentation.Backdrop";
    public const string SettingsWindowKey = "Presentation.SettingsWindow";

    /// <summary>Reads every field from <paramref name="read"/> (which returns null for a missing key).</summary>
    public static PresentationSettings Read(Func<string, object?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var defaults = PresentationSettings.Defaults;
        return new PresentationSettings(
            ReadEnum(read(ThemeKey), defaults.Theme),
            ReadEnum(read(TrayStyleKey), defaults.TrayStyle),
            ReadEnum(read(BackdropKey), defaults.Backdrop),
            read(SettingsWindowKey) is string geometry ? WindowGeometry.FromStorage(geometry) : null);
    }

    /// <summary>The key and value to write for each field. A cleared window geometry has a null value: remove the key.</summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> Write(PresentationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return
        [
            new(ThemeKey, settings.Theme.ToString()),
            new(TrayStyleKey, settings.TrayStyle.ToString()),
            new(BackdropKey, settings.Backdrop.ToString()),
            new(SettingsWindowKey, settings.SettingsWindow?.ToStorage()),
        ];
    }

    /// <summary>An enum by name only: a number, a name that does not exist, or a value of another type is the default.</summary>
    private static T ReadEnum<T>(object? stored, T fallback)
        where T : struct, Enum
    {
        if (stored is not string text || text.Length == 0 || char.IsDigit(text[0]) || text[0] == '-')
        {
            return fallback;
        }

        return Enum.TryParse<T>(text, ignoreCase: false, out var value) && Enum.IsDefined(value) ? value : fallback;
    }
}
