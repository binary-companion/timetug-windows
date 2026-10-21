using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Settings;

/// <summary>One choice in the lead-time picker: whole minutes (the value sent is seconds) and the words for it.</summary>
public sealed record LeadTimeOption(int Minutes, string Title);

/// <summary>
/// The lead-time choices, one list for the picker and its tests (the Mac's <c>LeadTimeOptions</c>): at start, then 1, 2, 3, 5, 10, 15, 20 and
/// 30 minutes, plus the saved value when it is not one of those, so a value set elsewhere is shown rather than lost.
/// </summary>
public static class LeadTimeOptions
{
    /// <summary>The standard choices, in minutes.</summary>
    public static IReadOnlyList<int> BaseMinutes { get; } = [0, 1, 2, 3, 5, 10, 15, 20, 30];

    /// <summary>The standard choices plus the saved lead time when it is not one of them, in ascending order.</summary>
    public static IReadOnlyList<LeadTimeOption> Make(TimeSpan current, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. BaseMinutes.Append(MinutesOf(current)).Distinct().Order().Select(m => new LeadTimeOption(m, Title(m, text)))];
    }

    public static string Title(int minutes, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return minutes switch
        {
            0 => text.GetString("LeadTime_AtStart"),
            1 => text.GetString("LeadTime_OneMinute"),
            _ => text.Format("LeadTime_Minutes", minutes),
        };
    }

    /// <summary>The picker's minutes for a lead time (halves round up; never negative).</summary>
    public static int MinutesOf(TimeSpan leadTime) => Math.Max(0, (int)Math.Round(leadTime.TotalMinutes, MidpointRounding.AwayFromZero));
}
