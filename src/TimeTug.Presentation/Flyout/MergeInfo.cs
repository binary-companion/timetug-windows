using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Flyout;

/// <summary>
/// What a merged card shows and offers, with all text decided.
/// </summary>
/// <param name="Summary">"Merged with Apple Intelligence · 4 events", "Merged manually · 2 events" or "3 events merged".</param>
/// <param name="BadgeText">Who merged them when it was not the rules: "Merged with Apple Intelligence", "Merged manually"; null for a rule merge (silent, as on the Mac).</param>
/// <param name="CalendarsText">The calendars the copies are on: "Work, Team".</param>
/// <param name="Members">The distinct events in the merge, one entry in the "Not the same meeting" menu each.</param>
public sealed record MergeInfo(string Summary, string? BadgeText, string CalendarsText, ValueList<MergeMemberRow> Members)
{
    /// <summary>True when the menu has members to separate (a merge the engine did not describe member by member still offers Unmerge).</summary>
    public bool CanSeparate => Members.Count > 0;
}

/// <summary>One distinct event inside a merge, as the "Not the same meeting" menu lists it.</summary>
/// <param name="MemberId">The engine's key for the event: what <see cref="FlyoutViewModel.SeparateAsync(string, string)"/> sends.</param>
/// <param name="Label">The calendars it is on and its time: "Work · Acme · 1:00 – 1:30 PM".</param>
/// <param name="AutomationName">"Not the same meeting: Work, 1:00 to 1:30 PM".</param>
public sealed record MergeMemberRow(string MemberId, string Label, string AutomationName);

/// <summary>An event the card can be merged with by hand, as the "Merge with…" menu lists it.</summary>
/// <param name="EventId">The other event's id: what <see cref="FlyoutViewModel.MergeAsync"/> sends.</param>
/// <param name="Label">"“Q3 budget” (2:15 PM)", as on the Mac.</param>
/// <param name="AutomationName">"Merge with Q3 budget, Team calendar, 2:15 PM".</param>
public sealed record CandidateRow(string EventId, string Label, string AutomationName);

/// <summary>Words for the merge indicator. A rule merge is silent; a model or manual merge says who did it (the Mac's <c>MergeBadge</c>).</summary>
public static class MergeBadge
{
    /// <summary>The provenance kinds the engine sends.</summary>
    public const string Rule = "rule";

    public const string Inference = "inference";

    public const string UserConfirmed = "userConfirmed";

    /// <summary>"Merged with Apple Intelligence" for a model's merge, "Merged manually" for the person's; null for a rule merge or no merge.</summary>
    public static string? Text(string? kind, string? engineName, Abstractions.ILocalizer text, System.Globalization.CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(culture);
        return kind switch
        {
            Inference when !string.IsNullOrWhiteSpace(engineName) => string.Format(culture, text.GetString("Flyout_MergedWith"), engineName),
            UserConfirmed => text.GetString("Flyout_MergedManually"),
            _ => null,
        };
    }
}
