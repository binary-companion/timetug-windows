#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using TimeTug.App.Settings;
using TimeTug.Platform.Startup;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>settings-general</c>: the real Settings window on the General pane over scripted data (invented account and calendars).
/// Launch at login is drawn against a scripted startup task in each state Windows can report (off, on, turned off by the person, turned
/// off or on by policy, a request the system refuses) and must show what Windows says, never what was asked. Then the machine's real startup
/// task is read through the app's own <see cref="StartupTaskService"/> and its state is written to <c>result.json</c>; when the task is
/// plainly off the scenario turns it on, reads it back and the package's startup-task registry key, and turns it off again, leaving it as it
/// found it (<c>--hold N</c> keeps it on for N seconds in between so Task Manager's Startup apps list can be looked at).
/// </summary>
internal static class SettingsGeneralScenario
{
    public const string Name = "settings-general";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var tug = TugRig.Create(options, cancellation);
        var run = tug.Run;
        using var settings = SettingsRig.Over(tug);
        tug.Engine.Push(SettingsRig.State(tug.Time.GetUtcNow()));

        var window = await settings.OpenAsync(SettingsPane.General, large: false);
        run.Check(window is not null, "the Settings window opens on General");
        if (window is null)
        {
            return tug.Finish();
        }

        run.Check(window.CurrentPage is GeneralPage, "the General pane is showing");
        if (window.CurrentPage is not GeneralPage page)
        {
            return tug.Finish();
        }

        await LaunchAtLoginAsync(tug, settings, window, page);
        await RealStartupTaskAsync(tug);
        return tug.Finish();
    }

    // ---- launch at login against a scripted startup task ---------------------------------------------------------------------------
    private static async Task LaunchAtLoginAsync(TugRig tug, SettingsRig settings, SettingsWindow window, GeneralPage page)
    {
        var run = tug.Run;
        var startup = settings.Startup;
        var name = "Launch at login";

        run.Check(await tug.WaitFor(() => startup.Calls.Contains("get")), "the page asked Windows for the state when it appeared");
        await tug.Settle(500);
        run.Check(!page.LaunchToggle.IsOn && page.LaunchToggle.IsEnabled, "with the task off, the switch is off and can be used");
        run.Check(AutomationProperties.GetName(page.LaunchToggle) == name, "the switch is named: " + AutomationProperties.GetName(page.LaunchToggle));
        run.Check(page.LaunchNoteText.Text.Length == 0 && page.LaunchSettingsLink.Visibility == Visibility.Collapsed, "and there is no note or link");
        await tug.Settle(800);
        await run.ShotAsync(window, "general-launch-off");

        // The person turns it on.
        run.Check(ScenarioUi.Toggle(page.LaunchToggle), "the switch is turned on through UI Automation");
        run.Check(await tug.WaitFor(() => startup.State == StartupState.Enabled && page.LaunchToggle.IsOn), "Windows was asked to enable it and the switch shows it on");
        run.Check(startup.Calls.Contains("enable"), "by a request to enable: " + string.Join(",", startup.Calls));
        await tug.Settle(800);
        await run.ShotAsync(window, "general-launch-on");
        run.Check(ScenarioUi.Toggle(page.LaunchToggle), "and off again");
        run.Check(await tug.WaitFor(() => startup.State == StartupState.Disabled && !page.LaunchToggle.IsOn), "Windows was asked to disable it and the switch shows it off");

        // The person turned it off in Windows Settings: seen when the window becomes active again.
        startup.State = StartupState.Enabled;
        page.Refresh();
        run.Check(await tug.WaitFor(() => page.LaunchToggle.IsOn), "a change made in Windows is seen on the next refresh (on)");
        startup.State = StartupState.DisabledByUser;
        page.Refresh();
        run.Check(await tug.WaitFor(() => !page.LaunchToggle.IsOn && !page.LaunchToggle.IsEnabled), "task manager turned it off: the switch is off and disabled");
        run.Check(page.LaunchSettingsLink.Visibility == Visibility.Visible && page.LaunchNoteText.Text.Contains("Startup apps", StringComparison.Ordinal), "with the reason and the link: " + page.LaunchNoteText.Text);
        run.Check(AutomationProperties.GetName(page.LaunchSettingsLink) == "Open Startup apps in Windows Settings", "the link is named");
        await tug.Settle(800);
        await run.ShotAsync(window, "general-launch-by-user");
        run.Check(ScenarioUi.Invoke(window, "Open Startup apps in Windows Settings"), "the link is pressed");
        run.Check(await tug.WaitFor(() => settings.Links.Opened.Count == 1), "it opens a link");
        run.Check(settings.Links.Opened.SequenceEqual([LaunchAtLoginViewModel.StartupSettingsUri]), "to the Startup apps page: " + string.Join(",", settings.Links.Opened));

        startup.State = StartupState.DisabledByPolicy;
        page.Refresh();
        run.Check(await tug.WaitFor(() => page.LaunchNoteText.Text.Contains("policy", StringComparison.Ordinal)), "a policy that keeps it off says so");
        run.Check(!page.LaunchToggle.IsOn && !page.LaunchToggle.IsEnabled && page.LaunchSettingsLink.Visibility == Visibility.Visible, "off, disabled, with the link");
        await tug.Settle(800);
        await run.ShotAsync(window, "general-launch-by-policy");

        startup.State = StartupState.EnabledByPolicy;
        page.Refresh();
        run.Check(await tug.WaitFor(() => page.LaunchToggle.IsOn && !page.LaunchToggle.IsEnabled), "a policy that keeps it on shows on and disabled");
        run.Check(page.LaunchSettingsLink.Visibility == Visibility.Collapsed && page.LaunchNoteText.Text.Contains("starts TimeTug", StringComparison.Ordinal), "with its note and no link");

        // Windows refuses the request: the switch goes back to what is true.
        startup.State = StartupState.Disabled;
        startup.StateAfterRequest = StartupState.DisabledByUser;
        page.Refresh();
        await tug.WaitFor(() => page.LaunchToggle.IsEnabled);
        run.Check(ScenarioUi.Toggle(page.LaunchToggle), "the switch is turned on while Windows will refuse");
        run.Check(await tug.WaitFor(() => !page.LaunchToggle.IsOn && !page.LaunchToggle.IsEnabled && page.LaunchSettingsLink.Visibility == Visibility.Visible), "the switch ends off and disabled with the link: Windows blocked it");

        // The system fails the request outright.
        startup.StateAfterRequest = null;
        startup.State = StartupState.Disabled;
        startup.ChangeThrows = new InvalidOperationException("The system cannot find the file specified.");
        page.Refresh();
        await tug.WaitFor(() => page.LaunchToggle.IsEnabled);
        run.Check(ScenarioUi.Toggle(page.LaunchToggle), "the switch is turned on while the system fails the request");
        run.Check(await tug.WaitFor(() => page.LaunchErrorText.Visibility == Visibility.Visible), "an error appears");
        run.Check(page.LaunchErrorText.Text == "Couldn’t change launch at login: The system cannot find the file specified." && !page.LaunchToggle.IsOn, "in the Mac's words, with the switch off: " + page.LaunchErrorText.Text);
        await tug.Settle(800);
        await run.ShotAsync(window, "general-launch-error");
        startup.ChangeThrows = null;

        // Search finds the setting and tints it.
        window.Shell.Open(SettingsPane.General, GeneralViewModel.Ids.LaunchAtLogin);
        run.Check(await tug.WaitFor(() => SettingsHighlight.Hosts(page).Any(h => SettingsHighlight.GetId(h) == GeneralViewModel.Ids.LaunchAtLogin && SettingsHighlight.OverlayOf(h) is { Opacity: > 0 }), 2000), "a search result for Launch at login tints the switch");
    }

    // ---- the machine's real startup task, through the app's own call ----------------------------------------------------------------------
    private static async Task RealStartupTaskAsync(TugRig tug)
    {
        var run = tug.Run;
        var service = new StartupTaskService();
        StartupState before;
        try
        {
            before = await service.GetStateAsync();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            run.Info("real startup task: could not be read: " + e.GetType().Name + ": " + e.Message);
            run.Check(false, "the real startup task can be read through StartupTask.GetAsync");
            return;
        }

        run.Info("real startup task before: " + before);
        run.Check(before is StartupState.Disabled or StartupState.Enabled or StartupState.DisabledByUser or StartupState.DisabledByPolicy or StartupState.EnabledByPolicy, "the package's startup task (TimeTugStartup) answers with a state");
        LogRegistry(run, "before");
        if (before != StartupState.Disabled)
        {
            run.Info("real startup task: left alone (only a plainly disabled task is turned on and off again)");
            return;
        }

        try
        {
            var enabled = await service.RequestEnableAsync();
            run.Info("real startup task after RequestEnableAsync: " + enabled);
            run.Check(enabled == StartupState.Enabled, "Windows enabled the real startup task when asked: " + enabled);
            run.Check(await service.GetStateAsync() == StartupState.Enabled, "and a fresh read says enabled");
            LogRegistry(run, "enabled");
            if (tug.Run.Options.HoldSeconds > 0)
            {
                run.Info($"real startup task: held enabled for {run.Options.HoldSeconds} s");
                await Task.Delay(TimeSpan.FromSeconds(run.Options.HoldSeconds));
            }
        }
        finally
        {
            var after = await service.DisableAsync();
            run.Info("real startup task after DisableAsync: " + after);
            run.Check(after == before, "the real startup task is back to " + before + " (as found): " + after);
            LogRegistry(run, "restored");
        }
    }

    /// <summary>Notes the startup-task state the package keeps under HKCU (value names and numbers only; nothing in it is calendar content).</summary>
    private static void LogRegistry(ScenarioRun run, string when)
    {
        try
        {
            var family = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            using var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\" + family);
            if (root is null)
            {
                run.Info($"registry ({when}): no SystemAppData key for {family}");
                return;
            }

            foreach (var path in Walk(root, string.Empty).Where(p => p.Contains(StartupTaskService.TaskId, StringComparison.OrdinalIgnoreCase) || p.Contains("startup", StringComparison.OrdinalIgnoreCase)))
            {
                run.Info($"registry ({when}): {path}");
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            run.Info($"registry ({when}): not readable: {e.GetType().Name}");
        }
    }

    private static IEnumerable<string> Walk(RegistryKey key, string path)
    {
        foreach (var valueName in key.GetValueNames())
        {
            yield return $"{path}\\{valueName} = {key.GetValue(valueName)}";
        }

        foreach (var sub in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(sub);
            if (child is null)
            {
                continue;
            }

            yield return $"{path}\\{sub}\\";
            foreach (var item in Walk(child, $"{path}\\{sub}"))
            {
                yield return item;
            }
        }
    }
}
#endif
