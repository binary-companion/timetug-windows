using System.Globalization;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Tray;

/// <summary>
/// What the notification-area icon and its tooltip should be right now. <see cref="Digit"/> is the remaining whole minutes (9 down to 1)
/// drawn on the colour icon, and is non-null only in the last ten minutes before the engine's next takeover starts.
/// <para>
/// The rules are the Mac's menu-bar rules, read from <c>Apps/macOS/Sources</c>, with the display arithmetic done against the injected
/// clock (display formatting, not scheduling):
/// </para>
/// <list type="bullet">
/// <item><description><b>Window.</b> <c>MenuBarIconState.resolve</c>: the icon is "soon" when the next pending takeover's meeting starts
/// within <c>soonWindow</c> = 10 minutes, inclusive (<c>start - now &lt;= soonWindow</c>), and a meeting that already started still counts.
/// <see cref="TrayViewModel"/> takes the engine's <c>nextTakeover</c> for "pending takeover" (the Mac asks <c>Scheduler.next</c>).</description></item>
/// <item><description><b>Rounding.</b> <c>TimeFormatting.compact</c> (the menu-bar countdown, tested at 299 s = "4m", 60 s = "1m", 45 s = "45s"):
/// whole seconds truncated, then whole minutes truncated, and below one minute it counts seconds. The digit therefore shows only
/// 60 to 599 seconds before the start: exactly ten minutes shows no digit ("10m" does not fit one digit), 9:01 shows 9, 1:00 shows 1 and
/// 0:59 shows none (the Mac would show "59s").</description></item>
/// <item><description><b>Day.</b> An all-day event has no start time to count to, so it never produces a digit.</description></item>
/// </list>
/// The tooltip counts minutes the same way (whole minutes truncated, at least "1 min" until the start) so it never disagrees with the digit.
/// </summary>
public sealed record TrayIconPlan(TrayIconState State, int? Digit, string Tooltip)
{
    /// <summary>How far ahead of its start the icon turns colour and a digit appears (the Mac's <c>MenuBarIconState.soonWindow</c>).</summary>
    public static readonly TimeSpan SoonWindow = TimeSpan.FromMinutes(10);

    /// <summary>The notification area shows at most 127 characters of tooltip.</summary>
    private const int MaxTooltip = 127;

    /// <summary>The digit for a meeting <paramref name="remaining"/> from starting: 1 to 9 in the last ten minutes (a whole minute or more left), else null.</summary>
    public static int? DigitFor(TimeSpan remaining)
    {
        var seconds = (long)remaining.TotalSeconds;
        return seconds is >= 60 and < 600 ? (int)(seconds / 60) : null;
    }

    /// <summary>
    /// The icon state, digit and tooltip for <paramref name="state"/> (null before the engine's first state) at <paramref name="now"/>.
    /// <paramref name="style"/> is the person's Appearance choice: with <see cref="TrayIconStyle.IconOnly"/> there is never a digit.
    /// </summary>
    public static TrayIconPlan Create(AppState? state, DateTimeOffset now, bool attention, ILocalizer text, CultureInfo culture, TrayIconStyle style = TrayIconStyle.IconWithMinuteDigit) =>
        Create(Measure(state, now), state, now, attention, text, culture, style);

    internal static TrayIconPlan Create(Nearness nearness, AppState? state, DateTimeOffset now, bool attention, ILocalizer text, CultureInfo culture, TrayIconStyle style = TrayIconStyle.IconWithMinuteDigit)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(culture);
        var tooltip = TooltipText(state, now, attention, text, culture);
        if (attention)
        {
            return new TrayIconPlan(TrayIconState.Attention, null, tooltip);
        }

        // Icon only keeps the colour icon (the state) and the tooltip; only the minute digit is left off.
        var digit = nearness.AllDay || style == TrayIconStyle.IconOnly ? null : DigitFor(nearness.UntilStart);
        return nearness.Near
            ? new TrayIconPlan(TrayIconState.Soon, digit, tooltip)
            : new TrayIconPlan(TrayIconState.Idle, null, tooltip);
    }

    /// <summary>
    /// Whether the engine has a takeover pending for a meeting that starts within <see cref="SoonWindow"/> (or has started), how long until
    /// it starts, and whether that meeting is the one the Next row names and has a join link, so the row may say Join.
    /// </summary>
    internal static Nearness Measure(AppState? state, DateTimeOffset now)
    {
        if (state is not { NextTakeover: { } takeover })
        {
            return default;
        }

        var scheduled = state.Events.FirstOrDefault(e => e.Id == takeover.EventId);
        var until = (scheduled?.Start ?? takeover.FireAt) - now;
        if (until > SoonWindow)
        {
            return default;
        }

        var sameMeeting = state.NextMeeting is { } next && scheduled is not null && next.Title == scheduled.Title && next.Start == scheduled.Start;
        return new Nearness(true, sameMeeting && state.NextMeeting!.HasJoinLink, until, scheduled?.IsAllDay == true);
    }

    private static string TooltipText(AppState? state, DateTimeOffset now, bool attention, ILocalizer text, CultureInfo culture)
    {
        if (attention)
        {
            return text.GetString("Tray_Tooltip_Attention");
        }

        var off = state is { Enabled: false };
        if (state?.NextMeeting is not { } next)
        {
            // Before the engine has spoken, or with nothing connected, the app's name; with accounts and nothing coming up, say so.
            var quiet = state is { Accounts.Count: > 0 } ? text.GetString("Tray_Tooltip_NoMore") : text.GetString("Tray_Tooltip_Default");
            return off ? string.Format(culture, text.GetString("Tray_Tooltip_TugOff"), quiet) : quiet;
        }

        string Compose(string title)
        {
            var line = WhenText(title, next.Start - now, text, culture);
            return off ? string.Format(culture, text.GetString("Tray_Tooltip_TugOff"), line) : line;
        }

        // Shorten only the title, on whole characters, so the time stays readable and the limit always holds.
        return TextCut.Fit(next.Title, MaxTooltip, Compose);
    }

    private static string WhenText(string title, TimeSpan until, ILocalizer text, CultureInfo culture)
    {
        if (until <= TimeSpan.Zero)
        {
            return string.Format(culture, text.GetString(until > -TimeSpan.FromMinutes(1) ? "Tray_Tooltip_StartsNow" : "Tray_Tooltip_Started"), title);
        }

        // Whole minutes, truncated like the digit (Mac: TimeFormatting.compact); under a minute still reads "1 min".
        var minutes = Math.Max(1, (long)until.TotalMinutes);
        if (minutes < 60)
        {
            return string.Format(culture, text.GetString("Tray_Tooltip_InMinutes"), title, minutes);
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0
            ? string.Format(culture, text.GetString("Tray_Tooltip_InHours"), title, hours)
            : string.Format(culture, text.GetString("Tray_Tooltip_InHoursMinutes"), title, hours, rest);
    }

    internal readonly record struct Nearness(bool Near, bool OffersJoin, TimeSpan UntilStart, bool AllDay);
}
