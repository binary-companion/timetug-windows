using System.Globalization;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Flyout;

/// <summary>
/// The flyout's wording, ported from the Mac's <c>PopupText</c> (<c>Apps/macOS/Sources/PopupText.swift</c>), <c>TimeFormatting.compact</c>
/// and <c>JoinLabel</c>. Pure functions of their arguments, the culture and the app's strings, so they can be tested with a fixed culture
/// and time zone. Every sentence comes from <c>Resources.resw</c> (keys <c>Flyout_*</c>); nothing here decides a meeting rule.
/// </summary>
public sealed class FlyoutText(ILocalizer text, CultureInfo culture)
{
    /// <summary>(provider hosts, string key) in the order the Mac checks them. A host matches itself or any subdomain of it.</summary>
    private static readonly (string[] Hosts, string Key)[] Providers =
    [
        (["zoom.us", "zoom.com"], "Flyout_JoinLabel_Zoom"),
        (["meet.google.com"], "Flyout_JoinLabel_Meet"),
        (["teams.microsoft.com", "teams.live.com"], "Flyout_JoinLabel_Teams"),
        (["webex.com"], "Flyout_JoinLabel_Webex"),
        (["app.slack.com"], "Flyout_JoinLabel_Slack"),
    ];

    /// <summary>"Friday" (Mac: <c>weekday</c>).</summary>
    public string Weekday(DateTimeOffset now, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(now, zone).ToString("dddd", culture);

    /// <summary>"Sep 18 · 3 meetings left" (Mac: <c>summary</c>); "No meetings today" when the day has no timed meeting at all.</summary>
    public string Summary(DateTimeOffset now, int meetingsLeft, int totalTimed, TimeZoneInfo zone)
    {
        var tail = totalTimed == 0 ? text.GetString("Flyout_NoMeetingsToday")
            : meetingsLeft <= 0 ? text.GetString("Flyout_NoMeetingsLeft")
            : meetingsLeft == 1 ? text.GetString("Flyout_OneMeetingLeft")
            : Format("Flyout_MeetingsLeft", meetingsLeft);
        return Format("Flyout_Summary", ShortDate(now, zone), tail);
    }

    /// <summary>"45s", "4m", "1h 5m": whole units, truncated, never negative (Mac: <c>TimeFormatting.compact</c>).</summary>
    public string Compact(TimeSpan span)
    {
        var total = Math.Max(0, (long)span.TotalSeconds);
        return total >= 3600 ? Format("Flyout_Compact_Hours", total / 3600, total % 3600 / 60)
            : total >= 60 ? Format("Flyout_Compact_Minutes", total / 60)
            : Format("Flyout_Compact_Seconds", total);
    }

    /// <summary>"in 3h 40m" (Mac: <c>countdown</c>).</summary>
    public string Countdown(TimeSpan untilStart) => Format("Flyout_Countdown", Compact(untilStart));

    /// <summary>"Tugs you 1 min before", or "Tugs you at start" (Mac: <c>tugFooter</c>; whole minutes, halves round up).</summary>
    public string TugFooter(TimeSpan leadTime)
    {
        var minutes = (int)Math.Round(leadTime.TotalMinutes, MidpointRounding.AwayFromZero);
        return minutes <= 0 ? text.GetString("Flyout_Footer_AtStart") : Format("Flyout_Footer_Before", minutes);
    }

    /// <summary>"30 min", "1 h", "1 h 30 min" (Mac: <c>duration</c>; rounded to whole minutes).</summary>
    public string Duration(TimeSpan span)
    {
        var minutes = Math.Max(0, (long)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero));
        var (hours, rest) = (minutes / 60, minutes % 60);
        return hours == 0 ? Format("Flyout_Duration_Minutes", rest)
            : rest == 0 ? Format("Flyout_Duration_Hours", hours)
            : Format("Flyout_Duration_HoursMinutes", hours, rest);
    }

    /// <summary>"3 hours 40 minutes", for a screen reader; whole minutes, "less than a minute" below one (Mac: <c>spokenDuration</c>).</summary>
    public string SpokenDuration(TimeSpan span)
    {
        var minutes = Math.Max(0, (long)span.TotalMinutes);
        var parts = new List<string>(2);
        if (minutes / 60 > 0)
        {
            parts.Add(Plural("Tug_Spoken_Hour", minutes / 60));
        }

        if (minutes % 60 > 0)
        {
            parts.Add(Plural("Tug_Spoken_Minute", minutes % 60));
        }

        return parts.Count == 0 ? text.GetString("Tug_Spoken_LessThanAMinute") : string.Join(' ', parts);
    }

    /// <summary>"7:15 PM": the short local time with a plain space so the text is stable across Windows and ICU versions (Mac: <c>clock</c>).</summary>
    public string Clock(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("t", culture).Replace(' ', ' ').Replace(' ', ' ');

    /// <summary>"7:15 – 8:00 PM": the AM/PM marker is written once when both ends share it (Mac: <c>range</c>).</summary>
    public string Range(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var (a, b) = RangeEnds(start, end, zone);
        return Format("Flyout_Range", a, b);
    }

    /// <summary>"7:15 to 8:00 PM" for a screen reader.</summary>
    public string SpokenRange(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var (a, b) = RangeEnds(start, end, zone);
        return Format("Flyout_Spoken_Range", a, b);
    }

    /// <summary>The provider's wording for the Join button's tooltip (Mac: <c>JoinLabel.text</c>).</summary>
    public string JoinLabel(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (string.Equals(url.Scheme, "zoommtg", StringComparison.OrdinalIgnoreCase))
        {
            return text.GetString("Flyout_JoinLabel_Zoom");
        }

        var host = url.IsAbsoluteUri ? url.Host.ToLowerInvariant() : string.Empty;
        foreach (var (hosts, key) in Providers)
        {
            if (hosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal)))
            {
                return text.GetString(key);
            }
        }

        return text.GetString("Flyout_JoinLabel_Other");
    }

    /// <summary>Fraction of a meeting elapsed, clamped to 0..1; 0 for an empty or inverted range (Mac: <c>progress</c>).</summary>
    public static double Progress(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        var total = (end - start).TotalSeconds;
        return total > 0 ? Math.Clamp((now - start).TotalSeconds / total, 0, 1) : 0;
    }

    /// <summary>The month and day in the culture's own order, with the month abbreviated ("Sep 18", "18. Sep").</summary>
    private string ShortDate(DateTimeOffset now, TimeZoneInfo zone)
    {
        var pattern = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM", StringComparison.Ordinal);
        return TimeZoneInfo.ConvertTime(now, zone).ToString(pattern, culture);
    }

    private (string Start, string End) RangeEnds(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var a = Clock(start, zone);
        var b = Clock(end, zone);
        foreach (var marker in new[] { culture.DateTimeFormat.AMDesignator, culture.DateTimeFormat.PMDesignator })
        {
            if (marker.Length > 0 && a.EndsWith(" " + marker, StringComparison.Ordinal) && b.EndsWith(" " + marker, StringComparison.Ordinal))
            {
                return (a[..^(marker.Length + 1)], b);
            }
        }

        return (a, b);
    }

    private string Plural(string keyPrefix, long count) =>
        count == 1 ? text.GetString(keyPrefix + "_One") : Format(keyPrefix + "_Other", count);

    private string Format(string key, params object[] arguments) => string.Format(culture, text.GetString(key), arguments);
}
