#if SELFTEST
using TimeTug.Platform.Hotkey;
using TimeTug.Platform.Instance;
using TimeTug.Platform.OsState;
using TimeTug.Presentation.Settings;
using Windows.Storage;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>hotkey</c>: the real shortcut service over a real hidden window, the real <c>RegisterHotKey</c> and the app's local
/// settings (under a key of its own). Nothing is registered until a shortcut is stored. A stored shortcut opens the flyout when its
/// <c>WM_HOTKEY</c> reaches the window (posted to the real window, as the system does) and closes it on the next press; a press is
/// ignored while a tug shows. A second service asking for a shortcut the first one holds gets a real conflict, which is stored and
/// still reported by a service started afterwards (a restart). Changing or clearing the shortcut releases the old one; disposing the
/// service releases it too. Pausing the shortcut for the Settings recorder (<c>SuspendForRecording</c>) lets a second registrar take the chord until
/// <c>Resume</c> takes it back, and a press while it is paused does nothing.
/// A runner without a desktop can refuse <c>RegisterHotKey</c> altogether; the scenario then asserts the conflict path instead (nothing
/// registered, the conflict stored, presses ignored) and records which path it took.
/// </summary>
internal static class HotkeyScenario
{
    public const string Name = "hotkey";

    private const string KeyOne = "SelfTest.Hotkey.One";
    private const string KeyTwo = "SelfTest.Hotkey.Two";

    // Ctrl+Alt+Shift+F23 and F22: chords no program is likely to hold.
    private static readonly HotkeyBinding Chord = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x86);
    private static readonly HotkeyBinding Other = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x85);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = FlyoutRig.Create(options, cancellation);
        var run = rig.Run;
        var tug = rig.Tug;
        tug.Engine.Push(rig.State(rig.Day()));
        var values = ApplicationData.Current.LocalSettings.Values;
        values.Remove(KeyOne);
        values.Remove(KeyTwo);
        try
        {
            await ExerciseAsync(rig, values, cancellation);
        }
        finally
        {
            values.Remove(KeyOne);
            values.Remove(KeyTwo);
        }

        return tug.Finish();
    }

    private static async Task ExerciseAsync(FlyoutRig rig, IDictionary<string, object> values, CancellationToken cancellation)
    {
        var run = rig.Run;
        var tug = rig.Tug;
        var ui = new DispatcherQueueDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        using var window = new MessageWindow();
        using var registrar = new HotkeyRegistrar(window, tug.Diagnostics);
        var store = new LocalSettingsHotkeyStore(values, KeyOne, tug.Diagnostics);
        var service = new HotkeyService(registrar, store, rig.Presence, ui, TimeProvider.System, tug.Diagnostics);
        using var trigger = FlyoutTriggers.ToggleOnHotkey(service, rig.Host);
        // A second registrar stands for "another program": it can take a chord only while this app does not hold it.
        using var probeWindow = new MessageWindow();
        using var probe = new HotkeyRegistrar(probeWindow, tug.Diagnostics);

        // ---- nothing until a shortcut is stored ---------------------------------------------------------------------
        await service.StartAsync();
        run.Check(service.State == HotkeyState.None, "with nothing stored there is no shortcut");
        registrar.SimulatePress();
        await tug.Settle(600);
        run.Check(!rig.Host.IsOpen, "a press with no shortcut set does nothing");

        // ---- storing one --------------------------------------------------------------------------------------------
        var result = await service.SetAsync(Chord);
        var registered = result.Status == HotkeyStatus.Registered;
        run.Info("path: " + (registered ? "RegisterHotKey accepted the shortcut" : "RegisterHotKey refused the shortcut (no desktop?); the conflict path is asserted instead"));
        run.Check(result.Status is HotkeyStatus.Registered or HotkeyStatus.Conflict, "setting a shortcut ends in Registered or Conflict: " + result.Status);
        run.Check(values.TryGetValue(KeyOne, out var stored) && stored is string text && text.Contains("Ctrl+Alt+Shift+F23", StringComparison.Ordinal), "the shortcut is stored in the local settings");
        var restored = new LocalSettingsHotkeyStore(values, KeyOne).Load();
        run.Check(restored == service.State, "what is stored reads back as the service's state");

        if (registered)
        {
            run.Check(service.State.BindingIsActive && !service.State.HasConflict, "the state says the shortcut is active");
            registrar.SimulatePress();
            run.Check(await tug.WaitFor(() => rig.Host.IsOpen), "the press opens the flyout");
            if (rig.Window is { } window1)
            {
                await SelfTestRunner.WaitForFrameAsync(window1, cancellation, 700);
            }

            registrar.SimulatePress();
            run.Check(await tug.WaitFor(() => !rig.Host.IsOpen), "the next press closes it");
            await tug.Settle(FlyoutHostGrace);

            // ---- a press while the tug shows --------------------------------------------------------------------------
            run.Check(await tug.PresentAsync(tug.Request("e-hotkey", "Planning", TimeSpan.FromSeconds(180))), "the takeover fires and the tug shows");
            registrar.SimulatePress();
            await tug.Settle(1000);
            run.Check(!rig.Host.IsOpen && rig.Presence.IsTugShowing, "a press while the tug shows does not open the flyout");
            run.Check(ScenarioUi.Invoke(tug.Host.Primary!, "Dismiss this reminder"), "Dismiss is pressed");
            run.Check(await tug.WaitHiddenAsync(), "the tug closes");
            registrar.SimulatePress();
            run.Check(await tug.WaitFor(() => rig.Host.IsOpen), "with the tug gone the press opens the flyout again");
            rig.Host.Close();
            await tug.Settle(FlyoutHostGrace);

            // ---- the Settings recorder pauses the shortcut so the same chord reaches it ---------------------------------
            await service.SuspendForRecording();
            run.Check(await probe.RegisterAsync(Chord), "while the recorder listens, Windows no longer holds the shortcut (a second registrar can take it)");
            await probe.UnregisterAsync();
            registrar.SimulatePress();
            await tug.Settle(600);
            run.Check(!rig.Host.IsOpen, "a press that was already on its way does nothing while the shortcut is paused");
            run.Check(service.State.BindingIsActive && service.State.Binding == Chord, "the state still says the shortcut is Ctrl+Alt+Shift+F23");
            await service.Resume();
            run.Check(!await probe.RegisterAsync(Chord), "resuming takes the shortcut back (a second registrar is refused again)");
            registrar.SimulatePress();
            run.Check(await tug.WaitFor(() => rig.Host.IsOpen), "and a press opens the flyout again");
            rig.Host.Close();
            await tug.Settle(FlyoutHostGrace);

            // Recording the chord that is already registered: pause, record it, and it is held once.
            await service.SuspendForRecording();
            var again = await service.SetAsync(Chord);
            run.Check(again.Status == HotkeyStatus.Registered && !await probe.RegisterAsync(Chord), "recording the same chord while paused ends registered and held");
            await service.Resume();
            run.Check(!await probe.RegisterAsync(Chord), "and a later Resume changes nothing");
        }
        else
        {
            run.Check(service.State.HasConflict && !service.State.BindingIsActive, "the state says the shortcut is inactive because of a conflict");
            registrar.SimulatePress();
            await tug.Settle(800);
            run.Check(!rig.Host.IsOpen, "a press does nothing when no shortcut is registered");
        }

        // ---- a conflict with another holder is stored and survives a restart ---------------------------------------
        using var windowTwo = new MessageWindow();
        using var registrarTwo = new HotkeyRegistrar(windowTwo, tug.Diagnostics);
        var storeTwo = new LocalSettingsHotkeyStore(values, KeyTwo, tug.Diagnostics);
        await using (var competing = new HotkeyService(registrarTwo, storeTwo, rig.Presence, ui, TimeProvider.System, tug.Diagnostics))
        {
            await competing.StartAsync();
            var conflict = await competing.SetAsync(Chord);
            run.Check(conflict.Status == HotkeyStatus.Conflict && conflict.Reason == HotkeyReason.OtherAppHoldsIt, "a second holder of the same shortcut gets a conflict: " + conflict.Status);
            run.Check(competing.State.Refused == Chord && competing.State.HasConflict, "the conflict is in its state");
        }

        using var windowThree = new MessageWindow();
        using var registrarThree = new HotkeyRegistrar(windowThree, tug.Diagnostics);
        await using (var restarted = new HotkeyService(registrarThree, new LocalSettingsHotkeyStore(values, KeyTwo, tug.Diagnostics), rig.Presence, ui, TimeProvider.System, tug.Diagnostics))
        {
            await restarted.StartAsync();
            run.Check(restarted.State.HasConflict && restarted.State.Refused == Chord, "after a restart the stored conflict is still reported");
        }

        run.Check(tug.Diagnostics.Entries.Any(e => e.Category == "hotkey" && e.Message.Contains("another program", StringComparison.Ordinal)), "the conflict is in the log");

        // ---- changing, clearing and disposing release the shortcut ---------------------------------------------------
        if (registered)
        {
            var changed = await service.SetAsync(Other);
            run.Check(changed.Status == HotkeyStatus.Registered, "a different shortcut can replace it: " + changed.Status);
            run.Check(await probe.RegisterAsync(Chord), "the old shortcut was released when it was replaced");
            await probe.UnregisterAsync();

            var cleared = await service.SetAsync(null);
            run.Check(cleared.Status == HotkeyStatus.Cleared && service.State == new HotkeyState(null, HotkeyStatus.Cleared, service.State.At), "clearing removes the shortcut");
            run.Check(await probe.RegisterAsync(Other), "the replacement was released when it was cleared");
            await probe.UnregisterAsync();

            run.Check((await service.SetAsync(Chord)).Status == HotkeyStatus.Registered, "it can be set again");
            await service.DisposeAsync();
            run.Check(await probe.RegisterAsync(Chord), "disposing the service released the shortcut");
            await probe.UnregisterAsync();
        }
        else
        {
            var invalid = await service.SetAsync(new HotkeyBinding(HotkeyModifiers.None, 'T'));
            run.Check(invalid.Status == HotkeyStatus.Invalid && invalid.Reason == HotkeyReason.NoModifier, "a shortcut without a modifier is invalid");
            var cleared = await service.SetAsync(null);
            run.Check(cleared.Status == HotkeyStatus.Cleared, "clearing works on the conflict path too");
            await service.DisposeAsync();
        }

        run.Check(tug.Terminal.Count == 0 || tug.Terminal.SequenceEqual(["dismiss:e-hotkey"]), "the engine received no terminal command but the one dismissal: " + string.Join(", ", tug.Terminal));
    }

    private const int FlyoutHostGrace = 500;
}
#endif
