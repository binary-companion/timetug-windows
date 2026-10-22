namespace TimeTug.Presentation.Settings;

/// <summary>
/// One <c>settings.apply</c> patch: a null member leaves that setting alone, a key set replaces the stored set. A patch starts empty
/// and an edit fills in only the fields it changes, so two edits to different fields can never overwrite each other.
/// <see cref="Current"/> is what the engine last published when the edit runs, so an edit that changes a key set builds it from the
/// engine's truth (add a key to <c>Current.TakeoverCalendarKeys</c>) rather than from a copy that may be a moment old.
/// </summary>
public sealed record EngineSettingsPatch
{
    /// <summary>The engine's last published settings (<see cref="EngineSettingsView.Defaults"/> before the first state). Never part of what is sent.</summary>
    public EngineSettingsView Current { get; init; } = EngineSettingsView.Defaults;

    public bool? Enabled { get; init; }

    public TimeSpan? LeadTime { get; init; }

    public bool? RequireConferenceLink { get; init; }

    public bool? RequireOtherAttendees { get; init; }

    public bool? SkipAllDayEvents { get; init; }

    public bool? InferenceEnabled { get; init; }

    public IReadOnlySet<string>? HiddenCalendarKeys { get; init; }

    public IReadOnlySet<string>? TakeoverCalendarKeys { get; init; }

    /// <summary>True when no field is set: there is nothing to send.</summary>
    public bool IsEmpty =>
        Enabled is null && LeadTime is null && RequireConferenceLink is null && RequireOtherAttendees is null
        && SkipAllDayEvents is null && InferenceEnabled is null && HiddenCalendarKeys is null && TakeoverCalendarKeys is null;

    /// <summary>True when every field that is set already holds that value in <see cref="Current"/>: sending it would change nothing.</summary>
    public bool ChangesNothing =>
        (Enabled is null || Enabled == Current.Enabled)
        && (LeadTime is null || LeadTime == Current.LeadTime)
        && (RequireConferenceLink is null || RequireConferenceLink == Current.RequireConferenceLink)
        && (RequireOtherAttendees is null || RequireOtherAttendees == Current.RequireOtherAttendees)
        && (SkipAllDayEvents is null || SkipAllDayEvents == Current.SkipAllDayEvents)
        && (InferenceEnabled is null || InferenceEnabled == Current.InferenceEnabled)
        && (HiddenCalendarKeys is null || HiddenCalendarKeys.SetEquals(Current.HiddenCalendarKeys))
        && (TakeoverCalendarKeys is null || TakeoverCalendarKeys.SetEquals(Current.TakeoverCalendarKeys));

    /// <summary>
    /// True when <paramref name="published"/> holds every value this patch sets: the engine's published state contains the change. The
    /// engine's own normalisation is allowed for: hidden wins over the tug list, so the stored tug list is the patched one minus the
    /// calendars the published state hides.
    /// </summary>
    public bool IsReflectedBy(EngineSettingsView published)
    {
        ArgumentNullException.ThrowIfNull(published);
        return (Enabled is null || Enabled == published.Enabled)
            && (LeadTime is null || LeadTime == published.LeadTime)
            && (RequireConferenceLink is null || RequireConferenceLink == published.RequireConferenceLink)
            && (RequireOtherAttendees is null || RequireOtherAttendees == published.RequireOtherAttendees)
            && (SkipAllDayEvents is null || SkipAllDayEvents == published.SkipAllDayEvents)
            && (InferenceEnabled is null || InferenceEnabled == published.InferenceEnabled)
            && (HiddenCalendarKeys is null || HiddenCalendarKeys.SetEquals(published.HiddenCalendarKeys))
            && (TakeoverCalendarKeys is null
                || TakeoverCalendarKeys.Where(key => !published.HiddenCalendarKeys.Contains(key)).ToHashSet(StringComparer.Ordinal).SetEquals(published.TakeoverCalendarKeys));
    }
}
