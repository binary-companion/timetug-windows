using Pkg = global::TimeTug.Engine;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// The one place that maps the engine package's settings to this app's types and back. Pure: it applies no rule and reads no clock.
/// The engine's names are <c>takeover.enabled</c>, <c>takeover.leadTime</c> (seconds), <c>takeover.requireConferenceLink</c>,
/// <c>takeover.requireOtherAttendees</c>, <c>takeover.skipAllDayEvents</c>, <c>takeover.hiddenCalendarKeys</c>,
/// <c>takeover.takeoverCalendarKeys</c> and the top-level <c>inferenceEnabled</c>; <c>systemCalendarEnabled</c> is the Mac's Apple Calendar switch and
/// is neither read nor written here.
/// </summary>
public static class EngineSettingsMapping
{
    public static EngineSettingsView ToView(Pkg.EngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var takeover = settings.Takeover ?? new Pkg.TakeoverSettings();
        return new EngineSettingsView(
            takeover.Enabled,
            ToLeadTime(takeover.LeadTime),
            takeover.RequireConferenceLink,
            takeover.RequireOtherAttendees,
            takeover.SkipAllDayEvents,
            settings.InferenceEnabled,
            ToSet(takeover.HiddenCalendarKeys),
            ToSet(takeover.TakeoverCalendarKeys));
    }

    /// <summary>Seconds on the wire; a value that is not a finite, non-negative number of seconds is zero ("at the start").</summary>
    public static TimeSpan ToLeadTime(double seconds) =>
        double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;

    /// <summary>The engine's <c>inferenceStatus.kind</c>. A word a later engine adds counts as unavailable, so the switch stays off and disabled.</summary>
    public static InferenceAvailability ToAvailability(Pkg.InferenceStatus? status) => status?.Kind switch
    {
        null or "disabled" => InferenceAvailability.Disabled,
        "noEngine" => InferenceAvailability.NoEngine,
        "unavailable" => InferenceAvailability.Unavailable,
        "notOnDevice" => InferenceAvailability.NotOnDevice,
        "active" => InferenceAvailability.Active,
        _ => InferenceAvailability.Unavailable,
    };

    /// <summary>The <c>settings.apply</c> command for a patch: a field that is not set stays null (the engine leaves it alone); a key set replaces the stored list.</summary>
    public static Pkg.EngineCommand.ApplySettings ToCommand(EngineSettingsPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return new Pkg.EngineCommand.ApplySettings
        {
            Enabled = patch.Enabled,
            LeadTime = patch.LeadTime?.TotalSeconds,
            RequireConferenceLink = patch.RequireConferenceLink,
            RequireOtherAttendees = patch.RequireOtherAttendees,
            SkipAllDayEvents = patch.SkipAllDayEvents,
            InferenceEnabled = patch.InferenceEnabled,
            HiddenCalendarKeys = patch.HiddenCalendarKeys is null ? null : Sorted(patch.HiddenCalendarKeys),
            TakeoverCalendarKeys = patch.TakeoverCalendarKeys is null ? null : Sorted(patch.TakeoverCalendarKeys),
        };
    }

    private static HashSet<string> ToSet(IReadOnlyList<string>? keys) =>
        keys is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(keys, StringComparer.Ordinal);

    private static List<string> Sorted(IReadOnlySet<string> keys)
    {
        var list = keys.ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }
}
