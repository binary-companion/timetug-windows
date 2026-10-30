using System.Globalization;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Engine;

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
    public const string ChipModeKey = "Presentation.ChipMode";
    public const string ChipLeadKey = "Presentation.ChipLeadMinutes";
    public const string ChipPositionsKey = "Presentation.ChipPositions";

    /// <summary>Reads every field from <paramref name="read"/> (which returns null for a missing key).</summary>
    public static PresentationSettings Read(Func<string, object?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var defaults = PresentationSettings.Defaults;
        return new PresentationSettings(
            ReadEnum(read(ThemeKey), defaults.Theme),
            ReadEnum(read(TrayStyleKey), defaults.TrayStyle),
            ReadEnum(read(BackdropKey), defaults.Backdrop),
            read(SettingsWindowKey) is string geometry ? WindowGeometry.FromStorage(geometry) : null)
        {
            ChipMode = ReadEnum(read(ChipModeKey), defaults.ChipMode),
            ChipLeadMinutes = ReadLead(read(ChipLeadKey)),
            ChipPositions = ReadPositions(read(ChipPositionsKey)),
        };
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
            new(ChipModeKey, settings.ChipMode.ToString()),
            new(ChipLeadKey, settings.ChipLeadMinutes.ToString(CultureInfo.InvariantCulture)),
            new(ChipPositionsKey, settings.ChipPositions.Count == 0 ? null : WritePositions(settings.ChipPositions)),
        ];
    }

    /// <summary>"id|x|y;id|x|y": the monitor id escaped (it may hold a backslash, a bar or a semicolon), the numbers in invariant digits with at most two decimals.</summary>
    private static string WritePositions(ValueList<ChipPosition> positions) => string.Join(
        ';',
        positions.TakeLast(ChipSettings.MaxPositions).Select(p => string.Create(
            CultureInfo.InvariantCulture, $"{Uri.EscapeDataString(p.MonitorId)}|{p.X:0.##}|{p.Y:0.##}")));

    /// <summary>Each position on its own: one that does not parse (or has no id, or a number out of range) is dropped and the others are kept; a monitor appears once, the last entry wins.</summary>
    private static ValueList<ChipPosition> ReadPositions(object? stored)
    {
        if (stored is not string text || text.Length == 0)
        {
            return default;
        }

        const double Limit = 100_000;
        var found = new List<ChipPosition>();
        foreach (var entry in text.Split(';'))
        {
            var parts = entry.Split('|');
            if (parts.Length != 3
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > Limit || Math.Abs(y) > Limit)
            {
                continue;
            }

            string id;
            try
            {
                id = Uri.UnescapeDataString(parts[0]);
            }
            catch (UriFormatException)
            {
                continue;
            }

            if (id.Length == 0)
            {
                continue;
            }

            found.RemoveAll(p => string.Equals(p.MonitorId, id, StringComparison.Ordinal));
            found.Add(new ChipPosition(id, x, y));
        }

        return new ValueList<ChipPosition>(found.TakeLast(ChipSettings.MaxPositions));
    }

    /// <summary>The lead time as one of the offered choices; anything else (missing, text, another number) is the default.</summary>
    private static int ReadLead(object? stored) =>
        stored is string text && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && ChipSettings.LeadChoices.Contains(minutes)
            ? minutes
            : ChipSettings.DefaultLeadMinutes;

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
