namespace TimeTug.Presentation.Settings;

/// <summary>
/// The engine's settings as the last published state carries them. The engine owns every value; the app only shows and patches them.
/// The names mirror the engine's wire names (<c>takeoverCalendarKeys</c>, <c>hiddenCalendarKeys</c>, ...) so a reader can follow one
/// setting from the engine to the screen; the single place that maps between the two is <see cref="EngineSettingsMapping"/>.
/// </summary>
/// <param name="Enabled">The Enable Tug switch (<c>takeover.enabled</c>). While false no tug fires; the agenda is unaffected.</param>
/// <param name="LeadTime">How long before a meeting starts the tug fires (<c>takeover.leadTime</c>, seconds on the wire).</param>
/// <param name="RequireConferenceLink">Only meetings with a video link tug (<c>takeover.requireConferenceLink</c>).</param>
/// <param name="RequireOtherAttendees">Only meetings with other attendees tug (<c>takeover.requireOtherAttendees</c>).</param>
/// <param name="SkipAllDayEvents">Hides all-day events from the day list (<c>takeover.skipAllDayEvents</c>); they never tug either way.</param>
/// <param name="InferenceEnabled">The person's choice for on-device duplicate finding (<c>inferenceEnabled</c>). Whether it can run is <see cref="InferenceAvailability"/>.</param>
/// <param name="HiddenCalendarKeys">Calendar keys the person hid from the list (<c>takeover.hiddenCalendarKeys</c>). A hidden calendar never tugs.</param>
/// <param name="TakeoverCalendarKeys">
/// The tug allow-list (<c>takeover.takeoverCalendarKeys</c>): calendar keys that may tug. It is opt-in, so it is empty by default and a
/// calendar tugs only when its key is here <em>and</em> not in <paramref name="HiddenCalendarKeys"/> (the engine decides; the app never evaluates it).
/// </param>
public sealed record EngineSettingsView(
    bool Enabled,
    TimeSpan LeadTime,
    bool RequireConferenceLink,
    bool RequireOtherAttendees,
    bool SkipAllDayEvents,
    bool InferenceEnabled,
    IReadOnlySet<string> HiddenCalendarKeys,
    IReadOnlySet<string> TakeoverCalendarKeys)
{
    private static readonly IReadOnlySet<string> NoKeys = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>What the engine's own defaults are, for the time before its first state: tug on, nothing opted in, all-day events skipped.</summary>
    public static EngineSettingsView Defaults { get; } = new(
        Enabled: true,
        LeadTime: TimeSpan.FromSeconds(60),
        RequireConferenceLink: false,
        RequireOtherAttendees: false,
        SkipAllDayEvents: true,
        InferenceEnabled: false,
        HiddenCalendarKeys: NoKeys,
        TakeoverCalendarKeys: NoKeys);

    /// <summary>Same settings (the key sets are compared by content, which record equality does not do).</summary>
    public bool SameAs(EngineSettingsView? other) =>
        other is not null
        && Enabled == other.Enabled
        && LeadTime == other.LeadTime
        && RequireConferenceLink == other.RequireConferenceLink
        && RequireOtherAttendees == other.RequireOtherAttendees
        && SkipAllDayEvents == other.SkipAllDayEvents
        && InferenceEnabled == other.InferenceEnabled
        && HiddenCalendarKeys.SetEquals(other.HiddenCalendarKeys)
        && TakeoverCalendarKeys.SetEquals(other.TakeoverCalendarKeys);
}
