namespace TimeTug.Presentation.Settings;

/// <summary>
/// How an edit changes the engine's two calendar key lists, ported from the Mac's <c>TakeoverSettings</c> mutators so the lists the app
/// sends are always consistent with each other: Tug on also shows the calendar (a calendar that is not shown never tugs), hiding a
/// calendar also takes it out of the tug list, showing and Tug off touch one list only. The engine replaces a whole list with the one
/// sent, so an edit starts from <see cref="EngineSettingsPatch.Current"/> (what the engine last published) and sets a list only when the
/// edit changed it. The engine still decides what tugs; this only keeps the stored choice coherent.
/// </summary>
public static class CalendarKeyEdits
{
    /// <summary>The Mac's <c>setTakeover(_:forCalendars:)</c>: on adds the keys and unhides them; off removes them from the tug list.</summary>
    public static EngineSettingsPatch SetTug(EngineSettingsPatch patch, IEnumerable<string> keys, bool on)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(keys);
        var tug = new HashSet<string>(patch.TakeoverCalendarKeys ?? patch.Current.TakeoverCalendarKeys, StringComparer.Ordinal);
        var hidden = new HashSet<string>(patch.HiddenCalendarKeys ?? patch.Current.HiddenCalendarKeys, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (on)
            {
                tug.Add(key);
                hidden.Remove(key);
            }
            else
            {
                tug.Remove(key);
            }
        }

        return With(patch, tug, hidden);
    }

    /// <summary>The Mac's <c>setShownInList(_:forCalendar:)</c>: showing removes the key from the hidden list; hiding adds it and removes it from the tug list.</summary>
    public static EngineSettingsPatch SetShown(EngineSettingsPatch patch, string key, bool shown)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(key);
        var tug = new HashSet<string>(patch.TakeoverCalendarKeys ?? patch.Current.TakeoverCalendarKeys, StringComparer.Ordinal);
        var hidden = new HashSet<string>(patch.HiddenCalendarKeys ?? patch.Current.HiddenCalendarKeys, StringComparer.Ordinal);
        if (shown)
        {
            hidden.Remove(key);
        }
        else
        {
            hidden.Add(key);
            tug.Remove(key);
        }

        return With(patch, tug, hidden);
    }

    private static EngineSettingsPatch With(EngineSettingsPatch patch, HashSet<string> tug, HashSet<string> hidden) => patch with
    {
        TakeoverCalendarKeys = tug.SetEquals(patch.Current.TakeoverCalendarKeys) ? null : tug,
        HiddenCalendarKeys = hidden.SetEquals(patch.Current.HiddenCalendarKeys) ? null : hidden,
    };
}
