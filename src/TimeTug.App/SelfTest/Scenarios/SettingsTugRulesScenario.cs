#if SELFTEST
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using TimeTug.App.Settings;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>settings-tugrules</c>: the real Settings window on the Tug Rules pane over the real app and the real native engine (testing
/// mode, manual clock, one recorded Google account with invented names). Each control is changed the way a person changes it and the
/// engine's own published state is checked to echo the change (and to have rescheduled the next tug), the flyout's footer follows the lead
/// time, the warnings come and go with their requirements, and the Test tug button shows the engine's sample tug with Enable Tug off.
/// </summary>
internal static class SettingsTugRulesScenario
{
    public const string Name = "settings-tugrules";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartAsync(options, cancellation);
        var run = rig.Run;
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        var window = await rig.OpenSettingsAsync(SettingsPane.TugRules);
        run.Check(window is not null, "the Settings window opens on Tug Rules");
        if (window is null)
        {
            return rig.Finish();
        }

        run.Check(await rig.WaitFor(() => window.CurrentPage is TugRulesPage), "the Tug Rules page is showing");
        if (window.CurrentPage is not TugRulesPage page)
        {
            return rig.Finish();
        }

        // ---- what the engine says now ------------------------------------------------------------------------------------
        var start = rig.Latest!.Settings;
        run.Check(start.Enabled && start.LeadTime == TimeSpan.FromMinutes(1) && !start.RequireConferenceLink && !start.RequireOtherAttendees, "the engine starts with Enable Tug on, a minute of lead time and no requirements");
        run.Check(page.EnableToggle.IsOn && SelectedMinutes(page) == 1 && !page.LinkToggle.IsOn && !page.AttendeesToggle.IsOn, "the controls show the engine's settings");
        run.Check(page.HeroSubtitleText.Text == "Pulls your attention to meetings before they start.", "the hero says what Tug does: " + page.HeroSubtitleText.Text);
        run.Check(page.LinkWarningRow.Visibility == Visibility.Collapsed && page.AttendeesWarningRow.Visibility == Visibility.Collapsed, "no warning shows while no requirement is on");
        var choices = page.LeadTime.Items.OfType<LeadTimeOption>().Select(o => o.Title).ToList();
        run.Check(choices.SequenceEqual(["At start", "1 minute", "2 minutes", "3 minutes", "5 minutes", "10 minutes", "15 minutes", "20 minutes", "30 minutes"]), "the lead-time picker lists the Mac's choices: " + string.Join(", ", choices));
        await rig.Settle(1200);
        await run.ShotAsync(window, "tug-rules-default");

        // ---- lead time: the engine echoes it and re-times the tug ---------------------------------------------------------------
        page.LeadTime.SelectedItem = page.LeadTime.Items.OfType<LeadTimeOption>().First(o => o.Minutes == 10);
        run.Check(await rig.WaitFor(() => rig.Latest is { } s && s.Settings.LeadTime == TimeSpan.FromMinutes(10)), "choosing 10 minutes reaches the engine and comes back in its state: " + rig.Latest?.Settings.LeadTime);
        run.Check(await rig.WaitFor(() => rig.Latest?.NextTakeover?.FireAt == EngineRig.ClockStart.AddMinutes(6)), "the engine moved the next tug to ten minutes before the meeting: " + rig.Latest?.NextTakeover?.FireAt);
        run.Check(SelectedMinutes(page) == 10, "the picker shows 10 minutes");
        var footer = rig.Shell.Services.GetRequiredService<FlyoutViewModel>().Footer;
        run.Check(footer == "Tugs you 10 min before", "the flyout's footer follows: " + footer);

        // ---- requirements and their warnings ---------------------------------------------------------------------------------
        page.LinkToggle.IsOn = true;
        run.Check(await rig.WaitFor(() => rig.Latest is { Settings.RequireConferenceLink: true }), "requiring a video link reaches the engine");
        run.Check(await rig.WaitFor(() => page.LinkWarningRow.Visibility == Visibility.Visible), "its warning appears");
        page.AttendeesToggle.IsOn = true;
        run.Check(await rig.WaitFor(() => rig.Latest is { Settings.RequireOtherAttendees: true }), "requiring other attendees reaches the engine");
        run.Check(await rig.WaitFor(() => page.AttendeesWarningRow.Visibility == Visibility.Visible), "its warning appears");
        run.Check(rig.Latest!.Settings.LeadTime == TimeSpan.FromMinutes(10) && rig.Latest.NextTakeover is not null, "the earlier change stands and the meeting (a link, another attendee) still has its tug");
        await rig.Settle(1200);
        await run.ShotAsync(window, "tug-rules-requirements");
        page.LinkToggle.IsOn = false;
        page.AttendeesToggle.IsOn = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { Settings: { RequireConferenceLink: false, RequireOtherAttendees: false } }), "turning them off reaches the engine");
        run.Check(await rig.WaitFor(() => page.LinkWarningRow.Visibility == Visibility.Collapsed && page.AttendeesWarningRow.Visibility == Visibility.Collapsed), "the warnings go");

        // ---- Enable Tug off, and the test tug anyway ---------------------------------------------------------------------------------
        page.EnableToggle.IsOn = false;
        run.Check(await rig.WaitFor(() => rig.Latest is { Settings.Enabled: false }), "turning Enable Tug off reaches the engine");
        run.Check(await rig.WaitFor(() => page.HeroSubtitleText.Text == "Off — your agenda and tray icon keep working."), "the hero says what still works: " + page.HeroSubtitleText.Text);
        run.Info("with Enable Tug off the engine's next takeover is: " + (rig.Latest?.NextTakeover?.FireAt.ToString("o") ?? "none"));
        await rig.Settle(1200);
        await run.ShotAsync(window, "tug-rules-off");
        page.ScrollArea.ChangeView(null, page.ScrollArea.ScrollableHeight, null, disableAnimation: true);
        await rig.Settle(1200);
        await run.ShotAsync(window, "tug-rules-off-bottom");
        run.Check(ScenarioUi.Invoke(window, "Test tug"), "the Test tug button is named and can be pressed");
        run.Check(await rig.WaitFor(() => rig.Windows.WindowCount > 0), "the engine's sample tug opens although Enable Tug is off");
        run.Check(rig.Latest is { Settings.Enabled: false }, "and Enable Tug is still off");
        await rig.Controller.DismissAsync();
        run.Check(await rig.WaitHiddenAsync(), "the sample tug closes");

        page.EnableToggle.IsOn = true;
        run.Check(await rig.WaitFor(() => rig.Latest is { Settings.Enabled: true } && rig.Latest.NextTakeover is not null), "turning Enable Tug on again brings the engine's next tug back: " + rig.Latest?.NextTakeover?.FireAt);

        // ---- every control is named ----------------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        run.Check(AutomationProperties.GetName(page.EnableToggle) == "Enable Tug" && AutomationProperties.GetName(page.LeadTime) == "Lead time", "the switch and the picker are named");
        return rig.Finish();
    }

    private static int SelectedMinutes(TugRulesPage page) => (page.LeadTime.SelectedItem as LeadTimeOption)?.Minutes ?? -1;
}
#endif
