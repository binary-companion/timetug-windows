#if SELFTEST
using Microsoft.UI.Xaml;
using TimeTug.App.Views;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest;

/// <summary>
/// The last two steps of the first-run window over the real engine, for the scenarios that get an account connected by their own means: step 3 lists the account's
/// calendars with their switches (nothing preset), Continue goes on to step 4 (start at sign-in, the taskbar card and its guide, Done), and Done closes the window.
/// </summary>
internal static class WelcomeWalk
{
    /// <summary>Walks from "You're connected" through "Keep TimeTug handy" and presses Done. The window must already be on step 3. True when every check held.</summary>
    public static async Task<bool> FromConnectedToDoneAsync(EngineRig rig, WelcomeWindow window, string label)
    {
        var run = rig.Run;
        var before = run.Checks.Count(c => !c.Ok);
        var model = window.ViewModel;
        var list = window.CalendarsList;

        run.Check(model.Step == WelcomeStep.Connected, $"({label}) the window is on step 3, You’re connected");
        run.Check(await rig.WaitFor(() => model.Calendars is { IsEmpty: false }), $"({label}) the engine lists the account's calendars");
        var rows = model.Calendars!.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Rows).ToList();
        run.Check(rows.Count > 0 && rows.All(r => list.ControlsOf(r.Key) is not null), $"({label}) every one has its check box and Tug switch on screen ({rows.Count} calendars)");
        run.Check(rows.All(r => !r.Tugs), $"({label}) nothing is preset to tug");
        run.Check(model.Calendars.Sections.All(s => s.Problem is null), $"({label}) no account has a problem line");
        run.Check(window.CalendarsEmptyText.Visibility == Visibility.Collapsed && window.ConnectedContinue.Visibility == Visibility.Visible && window.Done.Visibility == Visibility.Collapsed,
            $"({label}) the button is Continue and the empty line is not showing");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, $"({label}) every control on step 3 has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Settle(700);
        await run.ShotAsync(window, label + "-connected");

        run.Check(ScenarioUi.Invoke(window.ConnectedContinue), $"({label}) Continue is pressed on step 3");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Handy), $"({label}) the window moves to step 4, Keep TimeTug handy");
        window.TaskbarStepCard.StartBringIntoView();
        run.Check(model.ShowLaunchAtLogin && window.LaunchCheckBox.Content is "Start TimeTug when I sign in", $"({label}) step 4 offers “Start TimeTug when I sign in”");
        run.Check(window.TaskbarStepCard.Visibility == Visibility.Visible && window.TaskbarSettingsButton.Visibility == Visibility.Visible, $"({label}) and the taskbar card with its button");
        run.Check(window.GuideAsset == "Assets/taskbar-guide.gif", $"({label}) and the guide");
        run.Check(window.Done.Visibility == Visibility.Visible && window.ConnectedContinue.Visibility == Visibility.Collapsed, $"({label}) with Done");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, $"({label}) every control on step 4 has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Settle(700);
        await run.ShotAsync(window, label + "-handy");

        run.Check(ScenarioUi.Invoke(window.Done), $"({label}) Done is pressed");
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is null), $"({label}) Done closes the window");
        return run.Checks.Count(c => !c.Ok) == before;
    }
}
#endif
