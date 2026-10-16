using System.Globalization;

namespace TimeTug.Presentation.Abstractions;

/// <summary>The app's own strings (Strings/en-US/Resources.resw). The engine supplies no display text.</summary>
public interface ILocalizer
{
    /// <summary>The string for <paramref name="key"/>; the key itself when it is missing so a gap is visible, never a crash.</summary>
    string GetString(string key);

    /// <summary>The string for <paramref name="key"/>, or null when there is none (used for engine-keyed labels with an English fallback).</summary>
    string? TryGetString(string key);
}

public static class LocalizerExtensions
{
    public static string Format(this ILocalizer localizer, string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, localizer.GetString(key), arguments);
}
