#if SELFTEST
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using TimeTug.App.Composition;
using TimeTug.App.Chip;
using TimeTug.App.Flyout;
using TimeTug.App.Tug;
using TimeTug.Platform.Capture;
using TimeTug.Platform.Chip;
using TimeTug.Platform.Hotkey;
using TimeTug.Platform.Instance;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tray;
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>compose</c>: the real app shell and composition root (real flyout, tray model, tug controller and windows, shortcut
/// service over the real <c>RegisterHotKey</c>, single-instance service) with the engine, the shortcut store and the machine's second
/// launch scripted. It checks the composed behaviour, in the order the app lives it:
/// <list type="number">
/// <item>before the engine's first state: a second launch is held (the flyout stays closed), the shortcut is not registered with Windows
/// and a left click on the icon does nothing;</item>
/// <item>the first state arrives: the held launch opens the flyout once, and the shortcut is registered once;</item>
/// <item>a left click on the icon toggles the flyout; the shortcut toggles it; a later second launch opens it once and leaves an open one open;</item>
/// <item>a tug closes an open flyout and blocks every trigger until it is dismissed;</item>
/// <item>with no account a left click opens the welcome window and not the flyout;</item>
/// <item>the countdown chip starts after the first state, is off until the setting says otherwise, opens the flyout from a real click, gives way to the tug
/// and comes back, and is closed before the flyout when the person quits;</item>
/// <item>quitting closes the chip and the flyout and removes the icon, and no trigger opens the flyout afterwards.</item>
/// </list>
/// </summary>
internal static class ComposeScenario
{
    public const string Name = "compose";

    // Ctrl+Alt+Shift+F23: a chord no program is likely to hold.
    private static readonly HotkeyBinding Chord = new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x86);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        FlyoutWindow.IgnoreSystemDeactivation = true;

        async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 8000)
        {
            for (var waited = 0; waited < milliseconds; waited += 100)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100, cancellation);
            }

            return condition();
        }

        var queue = DispatcherQueue.GetForCurrentThread();
        var diagnostics = new RecordingDiagnostics();
        var text = new ResourceLoaderLocalizer();
        using var forms = new AccountFormService(queue, text);
        using var engine = new ScriptedEngineClient(forms);
        using var registry = new InProcessRegistry();
        using var instance = new AppInstanceService(registry, AppInstanceService.MainKey, TimeProvider.System, diagnostics);
        var presentation = new MemoryPresentationSettingsStore();
        var store = new MemoryHotkeyStore(new HotkeyState(Chord, HotkeyStatus.Registered, DateTimeOffset.UtcNow));
        RecordingRegistrar? registrar = null;
        var shell = new AppShell(queue, diagnostics, services =>
        {
            services.AddSingleton<IEngineClient>(engine);
            services.AddSingleton<ISessionState>(new FakeSession());
            services.AddSingleton<INotificationState>(new FakeNotifications());
            services.AddSingleton<IToastService>(new FakeToasts());
            services.AddSingleton<IUrlLauncher>(new FakeLauncher());
            services.AddSingleton<IHotkeyStore>(store);
            services.AddSingleton<IPresentationSettingsStore>(presentation);
            services.AddSingleton<IHotkeyRegistrar>(sp => registrar = new RecordingRegistrar(sp.GetRequiredService<HotkeyRegistrar>()));
        }, withTray: true, instance);

        // ---- 1. started, but the engine has not spoken yet ----------------------------------------------------------------
        var started = shell.RunAsync();
        run.Check(await WaitFor(() => shell.TrayModel is not null && shell.Flyout is not null), "the shell built the services and the tray while waiting for the engine's first state");
        run.Check(!started.IsCompleted, "startup is not finished before the first state");
        if (shell.Flyout is not { } flyout || shell.TrayModel is not { } tray || registrar is null)
        {
            return run.Finish("the shell did not start");
        }

        var openings = 0;
        flyout.OpenChanged += () =>
        {
            if (flyout.IsOpen)
            {
                openings++;
            }
        };

        registry.Activate();
        tray.PrimaryClick();
        await Task.Delay(1500, cancellation);
        run.Check(!flyout.IsOpen && openings == 0, "a second launch before the first state is held: the flyout stays closed");
        run.Check(registrar.Calls.Count == 0, "the shortcut is not registered before the first state: " + string.Join(", ", registrar.Calls));

        // ---- 2. the first state: the held launch opens the flyout once, the shortcut is registered ----------------------------
        var now = DateTimeOffset.UtcNow;
        engine.Push(Connected(now));
        await started.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        run.Check(!shell.Failed, "the app started" + (shell.FailureReason is null ? string.Empty : ": " + shell.FailureReason));
        run.Check(await WaitFor(() => flyout.IsOpen), "the held second launch opens the flyout when the first state is in");
        await Task.Delay(1000, cancellation);
        run.Check(openings == 1, "the held launch opened the flyout exactly once: " + openings);
        run.Check(registrar.Calls.Count == 1 && registrar.Calls[0].StartsWith("register ", StringComparison.Ordinal), "the shortcut was registered once after the first state: " + string.Join(", ", registrar.Calls));
        run.Check(shell.Chip is { HasWindow: false }, "the countdown chip is started after the first state and is off by default: no window");
        var factory = shell.Services.GetRequiredService<IFlyoutSurfaceFactory>() as FlyoutSurfaceFactory;
        if (factory?.Last?.Window is { } window)
        {
            await SelfTestRunner.WaitForFrameAsync(window, cancellation, 700);
            await run.ShotAsync(window, "opened-by-the-held-launch");
        }

        // ---- 3. the triggers ---------------------------------------------------------------------------------------------
        openings = 0;
        tray.PrimaryClick();
        run.Check(!flyout.IsOpen, "a left click on the icon closes the open flyout");
        tray.PrimaryClick();
        run.Check(flyout.IsOpen && openings == 1, "a left click on the icon opens it: once (" + openings + ")");
        tray.PrimaryClick();
        run.Check(!flyout.IsOpen && shell.Welcome is null, "the next left click closes it and opens no welcome window while an account is connected");

        var service = shell.Services.GetRequiredService<HotkeyService>();
        if (service.State.BindingIsActive)
        {
            run.Info("path: RegisterHotKey accepted the shortcut");
            openings = 0;
            registrar.Press();
            run.Check(await WaitFor(() => flyout.IsOpen) && openings == 1, "the shortcut opens the flyout exactly once");
            await Task.Delay(700, cancellation);
            registrar.Press();
            run.Check(await WaitFor(() => !flyout.IsOpen), "the next press closes it");
        }
        else
        {
            run.Info("path: RegisterHotKey refused the shortcut (no desktop?); the hotkey presses are not exercised here (the hotkey scenario asserts the conflict path)");
            run.Check(service.State.HasConflict, "a refused shortcut is recorded as a conflict");
        }

        await Task.Delay(700, cancellation);
        openings = 0;
        registry.Activate();
        run.Check(await WaitFor(() => flyout.IsOpen) && openings == 1, "a second launch opens the flyout once");
        await Task.Delay(800, cancellation);
        registry.Activate();
        await Task.Delay(1200, cancellation);
        run.Check(flyout.IsOpen && openings == 1, "a second launch while it is open leaves it open and opens no second window: " + openings);

        // ---- 3b. the countdown chip ---------------------------------------------------------------------------------------------
        flyout.Close();
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(100), cancellation);
        presentation.Save(presentation.Load() with { ChipMode = ChipMode.Always });
        var chip = shell.Chip;
        run.Check(chip is not null && await WaitFor(() => chip.IsShown), "turning the chip on in the Appearance setting shows it for the meeting in progress, at once");
        var chipWindow = shell.Services.GetRequiredService<ChipWindowFactory>().Last;
        if (chipWindow is not null && ChipWindowChrome.OuterBounds(chipWindow.Handle) is { } chipBounds)
        {
            await Task.Delay(700, cancellation);
            openings = 0;
            run.Check(MouseInput.ClickAt((int)chipBounds.CenterX, (int)chipBounds.CenterY), "a real click lands on the chip");
            run.Check(await WaitFor(() => flyout.IsOpen) && openings == 1, "a click on the chip opens the flyout (the same call as the tray icon's)");
            flyout.Close();
            await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(100), cancellation);
        }

        // ---- 4. the tug wins ------------------------------------------------------------------------------------------------
        var controller = shell.Services.GetRequiredService<TugController>();
        var windows = shell.Services.GetRequiredService<TugWindowHost>();
        var monitors = shell.Services.GetRequiredService<IMonitorService>();
        var request = new TakeoverRequest("e-compose", "Planning", DateTimeOffset.UtcNow.AddSeconds(180), DateTimeOffset.UtcNow.AddMinutes(33), false, false,
            "https://meet.example.test/compose", [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300)], "Work", 5);
        await controller.OnTakeoverDueAsync(request);
        run.Check(await WaitFor(() => windows.Primary is not null && windows.WindowCount == monitors.Monitors.Count), "the tug shows on every display");
        run.Check(await WaitFor(() => !flyout.IsOpen), "the tug closed the open flyout");
        openings = 0;
        tray.PrimaryClick();
        registry.Activate();
        registrar.Press();
        await Task.Delay(1500, cancellation);
        run.Check(!flyout.IsOpen && openings == 0, "while the tug shows, neither the icon, a second launch nor the shortcut opens the flyout: " + openings);
        run.Check(windows.WindowCount == monitors.Monitors.Count, "the tug is unaffected");
        run.Check(chip is not null && !chip.IsShown, "the chip gives way to the tug");
        if (windows.Primary is { } primary)
        {
            run.Check(ScenarioUi.Invoke(primary, "Dismiss this reminder"), "Dismiss is pressed");
        }

        run.Check(await WaitFor(() => windows.WindowCount == 0), "the tug closes");
        run.Check(chip is not null && await WaitFor(() => chip.IsShown), "the chip comes back when the tug is gone");
        await Task.Delay(600, cancellation);
        tray.PrimaryClick();
        run.Check(flyout.IsOpen, "with the tug gone a left click opens the flyout again");
        flyout.Close();

        // ---- 5. no account: the left click opens the welcome window, not the flyout -----------------------------------------
        engine.Push(Scenes.Empty());
        await Task.Delay(300, cancellation);
        tray.PrimaryClick();
        run.Check(await WaitFor(() => shell.Welcome is not null) && !flyout.IsOpen, "with no account a left click opens the welcome window and not the flyout");

        // ---- 6. quitting --------------------------------------------------------------------------------------------------------
        engine.Push(Connected(DateTimeOffset.UtcNow));
        await Task.Delay(300, cancellation);
        tray.PrimaryClick();
        run.Check(flyout.IsOpen, "the flyout is open when the person quits");
        await shell.QuitAsync(exitProcess: false).WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        run.Check(!flyout.IsOpen, "quitting closed the flyout");
        run.Check(shell.Chip is null && chip is not null && !chip.HasWindow, "quitting closed the chip first");
        run.Check(shell.TrayIcon is null, "quitting removed the tray icon");
        registry.Activate();
        tray.PrimaryClick();
        await Task.Delay(1500, cancellation);
        run.Check(!flyout.IsOpen, "after quitting no trigger opens the flyout");
        run.Check(diagnostics.Entries.All(e => e.Level != "error" || e.Category == "tray"), "nothing was recorded as an error (a tray that could not be created aside): "
            + string.Join("; ", diagnostics.Entries.Where(e => e.Level == "error").Select(e => e.Category + ": " + e.Message)));

        return run.Finish();
    }

    private static AppState Connected(DateTimeOffset now) => Scenes.Connected(now) with
    {
        Agenda =
        [
            new AgendaItem("c1", "Team stand-up", now.AddMinutes(-70), now.AddMinutes(-40), false, AgendaPhase.Past, "Team", "#9334E6", null, 0, false, null),
            new AgendaItem("c2", "Design review", now.AddMinutes(-10), now.AddMinutes(40), false, AgendaPhase.Current, "Work", "#2E7AF5", new Uri("https://zoom.example.test/j/1"), 5, false, null),
            new AgendaItem("c3", "Quarterly planning", now.AddMinutes(90), now.AddMinutes(150), false, AgendaPhase.Upcoming, "Work", "#E8710A", null, 8, false, null) { IsNext = true },
        ],
        LeadTime = TimeSpan.FromSeconds(60),
    };

    /// <summary>The machine's table of running copies, with only this process in it; <see cref="Activate"/> is a second launch.</summary>
    private sealed class InProcessRegistry : IInstanceRegistry, IDisposable
    {
        private readonly Registration _registration = new();

        public IInstanceRegistration FindOrRegister(string key) => _registration;

        public void Activate() => _registration.Raise();

        public void Dispose() => _registration.Dispose();

        private sealed class Registration : IInstanceRegistration
        {
            public bool IsCurrent => true;

            public event Action<ActivationKind>? ActivationReceived;

            public void Raise() => ActivationReceived?.Invoke(ActivationKind.Flyout);

            public Task RedirectToAsync(ActivationKind kind) => Task.CompletedTask;

            public void Dispose()
            {
            }
        }
    }

    private sealed class MemoryHotkeyStore(HotkeyState state) : IHotkeyStore
    {
        private HotkeyState _state = state;

        public HotkeyState Load() => _state;

        public void Save(HotkeyState value) => _state = value;
    }

    /// <summary>The real registrar, with every call written down.</summary>
    private sealed class RecordingRegistrar(HotkeyRegistrar inner) : IHotkeyRegistrar
    {
        public List<string> Calls { get; } = [];

        public event Action? Pressed
        {
            add => inner.Pressed += value;
            remove => inner.Pressed -= value;
        }

        public Task<bool> RegisterAsync(HotkeyBinding binding)
        {
            Calls.Add("register " + binding.ToText());
            return inner.RegisterAsync(binding);
        }

        public Task UnregisterAsync()
        {
            Calls.Add("unregister");
            return inner.UnregisterAsync();
        }

        /// <summary>Posts a <c>WM_HOTKEY</c> to the real window, as the system does when the keys are pressed.</summary>
        public void Press() => inner.SimulatePress();

        public void Dispose()
        {
            // The container owns the real registrar and disposes it.
        }
    }
}
#endif
