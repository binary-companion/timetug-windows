#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using TimeTug.App.Settings;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>settings-calendars</c>: the real Settings window on the Calendars pane over the real app and the real native engine (testing
/// mode, one recorded Google account that lists five invented calendars, two of them a holiday feed and birthdays). Each control is
/// changed the way a person changes it and the engine's own published state is checked to echo it: a calendar's Tug switch, its visibility
/// checkbox (hiding a tugging calendar also takes it out of the tug list), the group's "All" switch, skip all-day. The inference switch
/// stays disabled and off and the engine's stored value is left alone. It also asks the engine what it does with a patch that contradicts
/// itself (a calendar both hidden and in the tug list), which the app never sends, and writes the answer to the result.
/// </summary>
internal static class SettingsCalendarsScenario
{
    public const string Name = "settings-calendars";

    private const string Source = "google-conn-1/";
    private const string Me = Source + "me@example.com";
    private const string Team = Source + "team@example.com";
    private const string Cafe = Source + "cafe@example.com";
    private const string Holidays = Source + "en.usa#holiday@group.v.calendar.google.com";
    private const string Birthdays = Source + "addressbook#contacts@group.v.calendar.google.com";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartAsync(options, cancellation, "google-calendar-list-many.json");
        var run = rig.Run;
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        run.Check(await rig.WaitFor(() => rig.Latest is { CalendarGroups.Count: > 0 }), "the engine listed the account's calendars");
        var state = rig.Latest!;
        var main = state.CalendarGroups.SelectMany(g => g.Calendars).Select(c => c.Key).Order().ToList();
        run.Check(main.SequenceEqual(new[] { Cafe, Me, Team }.Order()), "three ordinary calendars are listed: " + string.Join(", ", main));
        run.Check(state.SystemCalendars.Select(c => c.Key).Order().SequenceEqual(new[] { Birthdays, Holidays }.Order()), "the holiday feed and the birthdays are set apart: " + string.Join(", ", state.SystemCalendars.Select(c => c.Key)));
        run.Info($"inference before any change: {state.Inference} (stored {state.Settings.InferenceEnabled}), reason '{state.InferenceReason}'");

        var window = await rig.OpenSettingsAsync(SettingsPane.Calendars);
        run.Check(window is not null, "the Settings window opens on Calendars");
        if (window is null)
        {
            return rig.Finish();
        }

        run.Check(await rig.WaitFor(() => window.CurrentPage is CalendarsPage { EmptyState.Visibility: Visibility.Collapsed } page && page.ControlsOf(Me) is not null), "the Calendars page lists the calendars");
        if (window.CurrentPage is not CalendarsPage page)
        {
            return rig.Finish();
        }

        // ---- what the engine says now ------------------------------------------------------------------------------------------
        run.Check(page.ControlsOf(Me)!.Value.Tug.IsOn && !page.ControlsOf(Team)!.Value.Tug.IsOn, "Tug is on for the calendar the engine's settings opt in, and off for the others");
        run.Check(page.ControlsOf(Me)!.Value.Show.IsChecked == true && page.ControlsOf(Cafe)!.Value.Show.IsChecked == true, "every calendar is shown");
        run.Check(page.SkipToggle.IsOn, "skip all-day events is on, as the engine has it");
        run.Check(!page.InferenceToggle.IsEnabled && !page.InferenceToggle.IsOn, "the inference switch is disabled and off");
        run.Check(page.InferenceStatusText.Text == "No on-device model is available on this PC. Using rules only.", "and says why: " + page.InferenceStatusText.Text);
        await rig.Settle(1200);
        await run.ShotAsync(window, "calendars-default");

        // ---- a calendar's Tug switch --------------------------------------------------------------------------------------------
        page.ControlsOf(Team)!.Value.Tug.IsOn = true;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && s.Settings.TakeoverCalendarKeys.Contains(Team)), "turning Tug on for Team Sync reaches the engine and comes back in its state");
        run.Check(rig.Latest!.Settings.TakeoverCalendarKeys.Contains(Me) && rig.Latest.Settings.HiddenCalendarKeys.Count == 0, "the other opted-in calendar stays, and nothing was hidden");

        // ---- visibility: hiding a calendar ------------------------------------------------------------------------------------------
        page.ControlsOf(Cafe)!.Value.Show.IsChecked = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && s.Settings.HiddenCalendarKeys.Contains(Cafe)), "hiding Café Sync reaches the engine");
        run.Check(await rig.WaitFor(() => !page.ControlsOf(Cafe)!.Value.Tug.IsEnabled), "its Tug switch is disabled while it is hidden");
        page.ControlsOf(Team)!.Value.Show.IsChecked = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && s.Settings.HiddenCalendarKeys.Contains(Team)), "hiding Team Sync (which tugs) reaches the engine");
        run.Check(!rig.Latest!.Settings.TakeoverCalendarKeys.Contains(Team), "and the same patch took it out of the tug list: " + string.Join(", ", rig.Latest.Settings.TakeoverCalendarKeys));
        run.Check(await rig.WaitFor(() => page.ControlsOf(Team) is { Tug.IsOn: false, Tug.IsEnabled: false }), "its Tug switch shows off and disabled");
        await rig.Settle(1200);
        await run.ShotAsync(window, "calendars-hidden");

        // ---- what the engine does with a patch that contradicts itself --------------------------------------------------------------
        await ProbeNormalisationAsync(rig);

        // ---- "All" for the group -----------------------------------------------------------------------------------------------------
        var group = rig.Latest!.CalendarGroups[0];
        var all = page.AllSwitchOf(group.SourceId, group.AccountName);
        run.Check(all is not null && !all.IsOn, "the group's All switch is off while some calendars do not tug");
        all!.IsOn = true;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && new[] { Cafe, Me, Team }.All(s.Settings.TakeoverCalendarKeys.Contains)), "All turns Tug on for every calendar of the group in the engine");
        run.Check(rig.Latest!.Settings.HiddenCalendarKeys.Count == 0, "and shows the hidden ones, so the lists agree: hidden = " + string.Join(", ", rig.Latest.Settings.HiddenCalendarKeys));
        run.Check(await rig.WaitFor(() => page.ControlsOf(Cafe) is { Show.IsChecked: true, Tug.IsOn: true, Tug.IsEnabled: true }), "the rows show it");
        await rig.Settle(1200);
        await run.ShotAsync(window, "calendars-all");
        all = page.AllSwitchOf(group.SourceId, group.AccountName)!;
        all.IsOn = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && !new[] { Cafe, Me, Team }.Any(s.Settings.TakeoverCalendarKeys.Contains)), "All off clears them again");

        // ---- the subscribed and system calendars ---------------------------------------------------------------------------------------
        run.Check(page.Disclosure is { IsExpanded: false }, "the subscribed and system calendars are closed");
        window.Shell.Open(SettingsPane.Calendars, "calendar:" + Holidays);
        run.Check(await rig.WaitFor(() => page.Disclosure is { IsExpanded: true }), "a search result for a calendar in it opens the list");
        // The tint lasts 1.5 s from the request: look for it at once, then take the picture while it is still there.
        run.Check(await rig.WaitFor(() => SettingsHighlight.Hosts(page).Select(SettingsHighlight.OverlayOf).Any(o => o is { Opacity: > 0 }), 1500), "and that calendar is tinted");
        await run.ShotAsync(window, "calendars-system");

        // ---- options ---------------------------------------------------------------------------------------------------------------------
        page.SkipToggle.IsOn = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { Settings.SkipAllDayEvents: false }), "skip all-day events off reaches the engine");
        page.SkipToggle.IsOn = true;
        run.Check(await rig.WaitFor(() => rig.Latest is { Settings.SkipAllDayEvents: true }), "and back on");
        run.Check(rig.Latest is { Settings.InferenceEnabled: false }, "the engine's stored inference value is untouched");
        page.ScrollArea.ChangeView(null, page.ScrollArea.ScrollableHeight, null, disableAnimation: true);
        await rig.Settle(1200);
        await run.ShotAsync(window, "calendars-options");

        // What the engine reports once the switch is on (the app never turns it on: the switch is disabled), then back off.
        await rig.Client.ApplySettingsAsync(new EngineSettingsPatch { InferenceEnabled = true });
        await rig.WaitFor(() => rig.Latest is { Settings.InferenceEnabled: true });
        await rig.Settle(500);
        run.Info($"inference with the stored switch on: {rig.Latest?.Inference}, reason '{rig.Latest?.InferenceReason}'");
        await rig.Client.ApplySettingsAsync(new EngineSettingsPatch { InferenceEnabled = false });
        await rig.WaitFor(() => rig.Latest is { Settings.InferenceEnabled: false });

        // ---- every control is named ------------------------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        run.Check(AutomationProperties.GetName(page.ControlsOf(Me)!.Value.Tug) == "Tug for Me" && AutomationProperties.GetName(page.ControlsOf(Me)!.Value.Show) == "Show Me in list", "a calendar's switch and checkbox are named after it");
        return rig.Finish();
    }

    /// <summary>
    /// Sends the real engine two patches the app never sends (a calendar in both lists, once through the tug list and once through the hidden
    /// list) and records what it publishes, so the notes say whether the engine normalises an inconsistent patch.
    /// </summary>
    private static async Task ProbeNormalisationAsync(EngineRig rig)
    {
        var run = rig.Run;
        var before = rig.Latest!.Settings;
        await rig.Client.ApplySettingsAsync(new EngineSettingsPatch { TakeoverCalendarKeys = new HashSet<string> { Me, Cafe }, HiddenCalendarKeys = new HashSet<string> { Cafe, Team } });
        await rig.WaitFor(() => rig.Latest is { } s && s.Settings.TakeoverCalendarKeys.Contains(Me));
        await rig.Settle(600);
        var after = rig.Latest!.Settings;
        run.Info("engine probe: sent tug {Me, Cafe} and hidden {Cafe, Team}; the engine now holds tug {" + string.Join(", ", after.TakeoverCalendarKeys.Order()) + "} and hidden {" + string.Join(", ", after.HiddenCalendarKeys.Order()) + "}");
        var normalised = !after.TakeoverCalendarKeys.Contains(Cafe) && after.HiddenCalendarKeys.Contains(Cafe);
        run.Info("engine probe: Café in both lists was " + (normalised ? "changed by the engine (hidden wins: it left the tug list)" : "stored as sent (the engine does not normalise)"));
        run.Check(normalised && after.TakeoverCalendarKeys.Contains(Me), "the engine itself drops a hidden calendar from the tug list when a patch lists it in both, as the Mac's settings do on load");
        // Put it back the way the pane would have left it.
        await rig.Client.ApplySettingsAsync(new EngineSettingsPatch { TakeoverCalendarKeys = new HashSet<string>(before.TakeoverCalendarKeys), HiddenCalendarKeys = new HashSet<string>(before.HiddenCalendarKeys) });
        await rig.WaitFor(() => rig.Latest is { } s && s.Settings.TakeoverCalendarKeys.SetEquals(before.TakeoverCalendarKeys) && s.Settings.HiddenCalendarKeys.SetEquals(before.HiddenCalendarKeys));
    }
}
#endif
