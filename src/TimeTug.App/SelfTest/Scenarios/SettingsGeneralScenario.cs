#if SELFTEST
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using TimeTug.App.Settings;
using TimeTug.Platform.Capture;
using TimeTug.Platform.Hotkey;
using TimeTug.Platform.OsState;
using TimeTug.Platform.Startup;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using Windows.Storage;
using VirtualKey = Windows.System.VirtualKey;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>settings-general</c>: the real Settings window on the General pane over scripted data (invented account and calendars).
/// Launch at login is drawn against a scripted startup task in each state Windows can report (off, on, turned off by the person, turned
/// off or on by policy, a request the system refuses) and must show what Windows says, never what was asked. Then the machine's real startup
/// task is read through the app's own <see cref="StartupTaskService"/> and its state is written to <c>result.json</c>; when the task is
/// plainly off the scenario turns it on, reads it back and the package's startup-task registry key, and turns it off again, leaving it as it
/// found it (<c>--hold N</c> keeps it on for N seconds in between so Task Manager's Startup apps list can be looked at).
/// The Shortcut row runs the recorder over the real <c>HotkeyService</c> and the real <c>RegisterHotKey</c> (chords nobody uses: Ctrl+Alt+Shift
/// with F22 and F23, in settings keys of the scenario's own) with injected key events: a chord is recorded and registered, a bare key is
/// refused, a modifier held alone is ignored, Escape cancels, the chord that is already registered is recorded again (a second registrar proves
/// the registration was released while recording and is held again after it), another program's chord is a conflict that is shown and still shown
/// after a restart, and Backspace and the clear button remove the shortcut.
/// </summary>
internal static class SettingsGeneralScenario
{
    public const string Name = "settings-general";

    // Wide enough for the cards to keep their controls on the right, tall enough for every row (pixels).
    private static readonly (int Width, int Height) WindowSize = (1500, 840);

    private const string StoreKey = "SelfTest.GeneralHotkey";

    // Ctrl+Alt+Shift+F23 and F22: chords no program is likely to hold.
    private static readonly HotkeyBinding Chord = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x86);
    private static readonly HotkeyBinding Other = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x85);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        values.Remove(StoreKey);
        try
        {
            return await ExerciseAsync(options, values, cancellation);
        }
        finally
        {
            values.Remove(StoreKey);
        }
    }

    private static async Task<SelfTestResult> ExerciseAsync(SelfTestOptions options, IDictionary<string, object> values, CancellationToken cancellation)
    {
        using var tug = TugRig.Create(options, cancellation);
        var run = tug.Run;
        var ui = new DispatcherQueueDispatcher(DispatcherQueue.GetForCurrentThread());
        using var messageWindow = new MessageWindow();
        using var registrar = new HotkeyRegistrar(messageWindow, tug.Diagnostics);
        var store = new LocalSettingsHotkeyStore(values, StoreKey, tug.Diagnostics);
        await using var service = new HotkeyService(registrar, store, new TugPresence(tug.Controller), ui, TimeProvider.System, tug.Diagnostics);
        await service.StartAsync();
        using var settings = SettingsRig.Over(tug, service);
        tug.Engine.Push(SettingsRig.State(tug.Time.GetUtcNow()));

        var window = await settings.OpenAsync(SettingsPane.General, size: WindowSize);
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
        await ShortcutAsync(tug, settings, window, page, service, store, values, ui);
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

    // ---- the Shortcut row: the recorder over the real HotkeyService and RegisterHotKey ---------------------------------------------------
    private static async Task ShortcutAsync(
        TugRig tug, SettingsRig settings, SettingsWindow window, GeneralPage page, HotkeyService service, LocalSettingsHotkeyStore store,
        IDictionary<string, object> values, IUiDispatcher ui)
    {
        var run = tug.Run;
        var recorder = page.RecorderControl;
        var model = recorder.Model;
        using var probeWindow = new MessageWindow();
        using var probe = new HotkeyRegistrar(probeWindow, tug.Diagnostics);
        // "Another program": holds a chord for as long as the scenario says.
        using var holderWindow = new MessageWindow();
        using var holder = new HotkeyRegistrar(holderWindow, tug.Diagnostics);

        // True while another registrar cannot take the chord, which is the case exactly while this app holds it.
        async Task<bool> HeldAsync(HotkeyBinding chord)
        {
            if (!await probe.RegisterAsync(chord))
            {
                return true;
            }

            await probe.UnregisterAsync();
            return false;
        }

        async Task<bool> WaitHeldAsync(HotkeyBinding chord, bool held)
        {
            for (var i = 0; i < 40; i++)
            {
                if (await HeldAsync(chord) == held)
                {
                    return true;
                }

                await Task.Delay(100);
            }

            return false;
        }

        async Task<bool> StartRecordingAsync(GeneralPage on, SettingsWindow inWindow)
        {
            var button = on.RecorderControl;
            return ScenarioUi.Invoke(inWindow, AutomationProperties.GetName(button.Record)) && await tug.WaitFor(() => button.Model.IsRecording);
        }

        // ---- nothing set ---------------------------------------------------------------------------------------------------------
        run.Check(service.State == HotkeyState.None, "no shortcut is stored at first");
        run.Check(model.DisplayText == "Record shortcut" && !recorder.Clear.IsEnabled, "the button invites recording and there is nothing to clear");
        run.Check(page.ShortcutStatusRow.Visibility == Visibility.Collapsed, "and nothing is said under the row");
        run.Check(AutomationProperties.GetName(page.RecorderControl.Record) == "Shortcut for Show today’s meetings: Record shortcut", "the button is named for a screen reader: " + AutomationProperties.GetName(recorder.Record));
        run.Check(AutomationProperties.GetName(recorder.Clear) == "Clear shortcut", "and so is the clear button");
        run.Check(model.Label == "Show today’s meetings" && model.Description == "Press a shortcut to show or hide the popup from anywhere. Click ✕ to clear it.", "the row says what the Mac's does");
        await tug.Settle(800);
        await run.ShotAsync(window, "general-shortcut-none");

        // ---- recording: the prompt, a modifier alone, a bare key --------------------------------------------------------------
        run.Check(await StartRecordingAsync(page, window), "the record button is pressed and starts recording");
        run.Check((string?)recorder.Record.Content == "Press a shortcut…" && AutomationProperties.GetName(recorder.Record).StartsWith("Recording a shortcut", StringComparison.Ordinal), "the button asks for a shortcut");
        run.Check(await recorder.InjectKeyAsync(VirtualKey.Control, control: true) && (string?)recorder.Record.Content == "Ctrl+…" && model.IsRecording, "Ctrl held alone is ignored and shown as Ctrl+…");
        await tug.Settle(500);
        await run.ShotAsync(window, "general-shortcut-recording");
        run.Check(await recorder.InjectKeyAsync(VirtualKey.F23) && model.IsRecording, "a bare key is taken by the recorder and recording goes on");
        run.Check(await tug.WaitFor(() => page.ShortcutStatusRow.Visibility == Visibility.Visible), "and a message appears");
        run.Check(page.ShortcutStatusText.Text == "A shortcut needs Ctrl, Alt, Shift or the Windows key as well as another key." && page.ShortcutIcon.Visibility == Visibility.Visible, "saying a modifier is needed: " + page.ShortcutStatusText.Text);
        await tug.Settle(800);
        await run.ShotAsync(window, "general-shortcut-invalid");

        // ---- a chord is recorded and registered ---------------------------------------------------------------------------------------
        run.Check(await recorder.InjectKeyAsync(VirtualKey.F23, control: true, alt: true, shift: true), "Ctrl+Alt+Shift+F23 is recorded");
        run.Check(await tug.WaitFor(() => service.State.Binding == Chord && !model.IsRecording), "the service holds it and recording ended");
        run.Check(service.State.BindingIsActive && model.DisplayText == "Ctrl+Alt+Shift+F23" && recorder.Clear.IsEnabled, "the button shows it and the clear button is enabled");
        run.Check(await tug.WaitFor(() => page.ShortcutStatusText.Text == "Shortcut is active." && page.ShortcutIcon.Visibility == Visibility.Collapsed), "the row says it is active: " + page.ShortcutStatusText.Text);
        run.Check(await HeldAsync(Chord), "Windows really holds the chord (a second registrar is refused)");
        run.Check(values.TryGetValue(StoreKey, out var stored) && stored is string text && text.Contains("Ctrl+Alt+Shift+F23", StringComparison.Ordinal), "and it is stored in the local settings");
        await tug.Settle(800);
        await run.ShotAsync(window, "general-shortcut-set");

        // ---- the chord that is already registered ----------------------------------------------------------------------------------------
        run.Check(await StartRecordingAsync(page, window), "recording starts again with the chord registered");
        run.Check(await WaitHeldAsync(Chord, held: false), "the registration is released while recording, so the chord reaches the recorder and not Windows");
        run.Check(await recorder.InjectKeyAsync(VirtualKey.F23, control: true, alt: true, shift: true), "the chord that was registered is recorded");
        run.Check(await tug.WaitFor(() => !model.IsRecording && service.State is { BindingIsActive: true } && service.State.Binding == Chord), "it ends registered, as it was");
        run.Check(await WaitHeldAsync(Chord, held: true), "and Windows holds it again");

        // ---- Escape cancels and restores ----------------------------------------------------------------------------------------------------
        run.Check(await StartRecordingAsync(page, window) && await WaitHeldAsync(Chord, held: false), "recording again releases it");
        run.Check(await recorder.InjectKeyAsync(VirtualKey.Escape), "Escape is recorded as a cancel");
        run.Check(await tug.WaitFor(() => !model.IsRecording) && model.DisplayText == "Ctrl+Alt+Shift+F23", "recording ends and the shortcut is unchanged");
        run.Check(await WaitHeldAsync(Chord, held: true), "and the registration is restored after the cancel");

        // ---- leaving the button restores it too ---------------------------------------------------------------------------------------------
        run.Check(await StartRecordingAsync(page, window) && await WaitHeldAsync(Chord, held: false), "recording once more releases it");
        recorder.Record.Focus(FocusState.Keyboard);
        recorder.Clear.Focus(FocusState.Keyboard);
        run.Check(await tug.WaitFor(() => !model.IsRecording), "moving the focus away ends the recording");
        run.Check(await WaitHeldAsync(Chord, held: true), "and the registration is restored");

        // The window losing the active state (to another program) ends a recording and restores the registration too.
        run.Check(await StartRecordingAsync(page, window) && await WaitHeldAsync(Chord, held: false), "recording again releases it");
        window.LoseActivation();
        run.Check(await tug.WaitFor(() => !model.IsRecording), "the window losing the keyboard to another program ends the recording");
        run.Check(await WaitHeldAsync(Chord, held: true), "and the registration is restored");

        // ---- another program holds the chord: a conflict that is shown --------------------------------------------------------------------------
        run.Check(await holder.RegisterAsync(Other), "another program (a second registrar) takes Ctrl+Alt+Shift+F22");
        run.Check(await StartRecordingAsync(page, window), "recording starts");
        run.Check(await recorder.InjectKeyAsync(VirtualKey.F22, control: true, alt: true, shift: true), "that chord is recorded");
        run.Check(await tug.WaitFor(() => !model.IsRecording && service.State.Refused == Other), "Windows refuses it and the service records the conflict");
        run.Check(service.State.Binding == Chord && await HeldAsync(Chord), "the earlier shortcut works again, held by this app");
        run.Check(await tug.WaitFor(() => page.ShortcutStatusText.Text == "Ctrl+Alt+Shift+F22 is already used by another program. Ctrl+Alt+Shift+F23 still works."), "the row names the conflict: " + page.ShortcutStatusText.Text);
        run.Check(page.ShortcutIcon.Visibility == Visibility.Visible && model.IsProblem, "as a problem");
        await tug.Settle(800);
        await run.ShotAsync(window, "general-shortcut-conflict");

        // ---- a restart: the stored conflict is still reported -------------------------------------------------------------------------------------
        settings.Host.Close();
        await service.DisposeAsync();
        await holder.UnregisterAsync();
        using (var secondWindow = new MessageWindow())
        using (var secondRegistrar = new HotkeyRegistrar(secondWindow, tug.Diagnostics))
        {
            await using var restarted = new HotkeyService(secondRegistrar, new LocalSettingsHotkeyStore(values, StoreKey, tug.Diagnostics), new TugPresence(tug.Controller), ui, TimeProvider.System, tug.Diagnostics);
            await restarted.StartAsync();
            run.Check(restarted.State.Binding == Chord && restarted.State.Refused == Other && restarted.State.HasConflict, "after a restart the service still reports the earlier conflict, read from the settings");
        }

        // Another program now holds the stored chord when the app starts: the shortcut is inactive, and Settings says so.
        run.Check(await holder.RegisterAsync(Chord), "a program holds Ctrl+Alt+Shift+F23 while the app is not running");
        using var thirdWindow = new MessageWindow();
        using var thirdRegistrar = new HotkeyRegistrar(thirdWindow, tug.Diagnostics);
        await using var again = new HotkeyService(thirdRegistrar, new LocalSettingsHotkeyStore(values, StoreKey, tug.Diagnostics), new TugPresence(tug.Controller), ui, TimeProvider.System, tug.Diagnostics);
        await again.StartAsync();
        run.Check(again.State.Binding == Chord && again.State.Refused == Chord && !again.State.BindingIsActive, "the shortcut is stored but inactive");
        using var settingsAgain = SettingsRig.Over(tug, again);
        tug.Engine.Push(SettingsRig.State(tug.Time.GetUtcNow()));
        var windowAgain = await settingsAgain.OpenAsync(SettingsPane.General, size: WindowSize);
        run.Check(windowAgain?.CurrentPage is GeneralPage, "the Settings window opens again on General");
        if (windowAgain?.CurrentPage is not GeneralPage pageAgain)
        {
            return;
        }

        var recorderAgain = pageAgain.RecorderControl;
        run.Check(recorderAgain.Model.DisplayText == "Ctrl+Alt+Shift+F23" && recorderAgain.Model.IsProblem, "the page shows the stored shortcut as a problem");
        run.Check(await tug.WaitFor(() => pageAgain.ShortcutStatusText.Text == "Ctrl+Alt+Shift+F23 is already used by another program, so it is inactive. Record a different shortcut."), "and says why after the restart: " + pageAgain.ShortcutStatusText.Text);
        await tug.Settle(800);
        await run.ShotAsync(windowAgain, "general-shortcut-conflict-restart");

        // ---- Backspace and the clear button -------------------------------------------------------------------------------------------------------------
        run.Check(await StartRecordingAsync(pageAgain, windowAgain), "recording starts on the restarted page");
        run.Check(await recorderAgain.InjectKeyAsync(VirtualKey.Back), "Backspace is taken by the recorder");
        run.Check(await tug.WaitFor(() => again.State.Binding is null && !recorderAgain.Model.IsRecording), "it clears the shortcut");
        run.Check(recorderAgain.Model.DisplayText == "Record shortcut" && !recorderAgain.Clear.IsEnabled && pageAgain.ShortcutStatusText.Text == "Shortcut cleared.", "the button invites recording again and the row says it was cleared");
        await holder.UnregisterAsync();
        run.Check(await StartRecordingAsync(pageAgain, windowAgain) && await recorderAgain.InjectKeyAsync(VirtualKey.F23, control: true, alt: true, shift: true), "the chord, now free, can be recorded again");
        run.Check(await tug.WaitFor(() => again.State.BindingIsActive && again.State.Binding == Chord), "and registers");
        run.Check(await WaitHeldAsync(Chord, held: true), "Windows holds it");
        run.Check(ScenarioUi.Invoke(windowAgain, "Clear shortcut"), "the ✕ button is pressed");
        run.Check(await tug.WaitFor(() => again.State.Binding is null), "it clears the shortcut");
        run.Check(await WaitHeldAsync(Chord, held: false), "and releases it with Windows");

        // ---- what the engine and the search say ----------------------------------------------------------------------------------------------------------
        windowAgain.Shell.Open(SettingsPane.General, GeneralViewModel.Ids.PopupShortcut);
        run.Check(await tug.WaitFor(() => SettingsHighlight.Hosts(pageAgain).Any(h => SettingsHighlight.GetId(h) == GeneralViewModel.Ids.PopupShortcut && SettingsHighlight.OverlayOf(h) is { Opacity: > 0 }), 2000), "a search result for the shortcut tints the row");
        run.Check(ScenarioUi.Unnamed(windowAgain).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(windowAgain)));
        run.Check(tug.Terminal.Count == 0, "the engine was told nothing by any of this: " + string.Join(", ", tug.Terminal));

        // ---- a real key press (a note, not a check: it reaches the button only when the window really has the keyboard) ----------------------------------
        run.Check(await StartRecordingAsync(pageAgain, windowAgain), "recording starts for the real key press");
        windowAgain.Activate();
        recorderAgain.Record.Focus(FocusState.Keyboard);
        await tug.Settle(500);
        var sent = KeyboardInput.Chord([0x11, 0x12, 0x10], 0x86);
        await tug.Settle(1200);
        var reached = again.State.Binding == Chord;
        run.Info($"real key press (SendInput Ctrl+Alt+Shift+F23): sent {sent}; reached the recorder: {reached}");
        if (!reached)
        {
            await recorderAgain.Model.CancelRecordingAsync();
        }

        run.Check(await WaitHeldAsync(Other, held: false) && !recorderAgain.Model.IsRecording, "recording is over and nothing stays released");
        await again.SetAsync(null);
        settingsAgain.Host.Close();
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
