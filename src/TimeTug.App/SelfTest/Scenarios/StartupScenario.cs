#if SELFTEST
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using TimeTug.App.Composition;
using TimeTug.App.Tug;
using TimeTug.Platform.OsState;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tray;
using TimeTug.Presentation.Tug;
using Pkg = TimeTug.Engine;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>startup</c>: the real composition root, the real native engine and the real OS-state services, with only the
/// engine's host services, the notification state, the toast service and the link launcher scripted and the data folder
/// pointed at the output folder (the engine runs on its manual test clock). It checks that the engine library loads inside the
/// package, that the first state opens the welcome window, that the Test tug path turns the engine's takeover into real tug
/// windows, that Dismiss sends a terminal command that closes them, and that quitting disposes everything without an error.
/// </summary>
internal static class StartupScenario
{
    public const string Name = "startup";

    private static readonly string[] ExpectedKinds = ["google", "microsoft", "icloud", "caldav"];

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        void Check(bool condition, string description) => run.Check(condition, description);

        async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 10000)
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

        var dataDirectory = Path.Combine(options.OutputFolder, "engine-data");
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        Directory.CreateDirectory(dataDirectory);

        var diagnostics = new RecordingDiagnostics();
        var host = new ScriptedCalendarHost();
        var notifications = new FakeNotifications();
        var toasts = new FakeToasts();
        var launcher = new FakeLauncher();
        var shell = new AppShell(DispatcherQueue.GetForCurrentThread(), diagnostics, services =>
        {
            services.AddSingleton(new EngineStartup(dataDirectory, Testing: true, new Dictionary<string, Pkg.OAuthClient>()));
            services.AddSingleton<Pkg.IEngineHost>(host);
            services.AddSingleton<INotificationState>(notifications);
            services.AddSingleton<IToastService>(toasts);
            services.AddSingleton<IUrlLauncher>(launcher);
        });

        // ---- start: the engine library loads, the engine runs and says its first state ------------------------------------
        await shell.RunAsync().WaitAsync(TimeSpan.FromSeconds(60), cancellation);
        Check(!shell.Failed, "the app started with the real engine" + (shell.FailureReason is null ? string.Empty : ": " + shell.FailureReason));
        if (shell.Failed)
        {
            return run.Finish();
        }

        var engineLibrary = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().FirstOrDefault(m => string.Equals(m.ModuleName, "TimeTugEngineC.dll", StringComparison.OrdinalIgnoreCase));
        Check(engineLibrary is not null, "TimeTugEngineC.dll is loaded in the app process");
        run.Info("engine library: " + engineLibrary?.FileName);

        var services = shell.Services;
        var client = services.GetRequiredService<IEngineClient>();
        var state = await FirstStateAsync(client, cancellation);
        Check(state is not null, "the engine published its first state");
        if (state is not null)
        {
            Check(state.Accounts.Count == 0, "a fresh data folder has no accounts");
            var kinds = state.AvailableKinds.ToDictionary(k => k.Id);
            Check(ExpectedKinds.All(kinds.ContainsKey), "the engine offers Google, Microsoft, iCloud and CalDAV: " + string.Join(", ", kinds.Keys));
            Check(kinds.TryGetValue("google", out var google) && !google.IsConfigured && kinds.TryGetValue("microsoft", out var microsoft) && !microsoft.IsConfigured,
                  "Google and Microsoft are unconfigured without OAuth client ids");
            Check(kinds.TryGetValue("icloud", out var icloud) && icloud.IsConfigured, "iCloud needs no client id and is available");
        }

        // ---- the composed graph ---------------------------------------------------------------------------------------
        Check(services.GetRequiredService<ISessionState>() is SessionState, "the real session state is composed");
        Check(services.GetRequiredService<IMonitorService>() is MonitorService { Monitors.Count: > 0 }, "the real monitor service sees at least one display");
        Check(services.GetRequiredService<IUiSettings>() is UiSettings, "the real UI settings are composed");
        Check(services.GetRequiredService<SystemEventsBridge>() is not null, "the system events bridge is composed");
        Check(services.GetRequiredService<Pkg.IEngineHost>() == host, "the scenario's scripted host replaced the real one");

        // ---- the welcome window ---------------------------------------------------------------------------------------
        Check(shell.Welcome is not null, "the welcome window opened at startup (no accounts)");
        if (shell.Welcome is { } welcome)
        {
            await SelfTestRunner.WaitForFrameAsync(welcome, cancellation);
            Check(ExpectedKinds.All(id => welcome.ViewModel.Providers.Any(p => p.Id == id)), "the welcome window lists the engine's kinds: " + string.Join(", ", welcome.ViewModel.Providers.Select(p => p.Id)));
            await run.ShotAsync(welcome, "welcome");
        }

        // ---- the Test tug path through the real engine ----------------------------------------------------------------------
        var runtime = services.GetRequiredService<AppRuntime>();
        var controller = services.GetRequiredService<TugController>();
        var windows = services.GetRequiredService<TugWindowHost>();
        var monitors = services.GetRequiredService<IMonitorService>();
        await runtime.TestTugAsync();
        var shown = await WaitFor(() => windows.Primary is not null && windows.WindowCount == monitors.Monitors.Count);
        Check(shown, $"the engine's test takeover opened one tug window per display ({windows.WindowCount} of {monitors.Monitors.Count})");
        Check(controller.State is TugState.Shown or TugState.ShownWithoutFocus, "the controller shows the tug: " + controller.State);
        if (windows.Primary is { } primary)
        {
            var text = services.GetRequiredService<ILocalizer>();
            await SelfTestRunner.WaitForFrameAsync(primary, cancellation, 800);
            Check(ScenarioUi.FindNamed<TextBlock>(primary, "TitleText")?.Text == text.GetString("TestTugTitle"), "the tug shows the app's own sample title");
            await run.ShotAsync(primary, "tug");
            Check(ScenarioUi.Invoke(primary, "Dismiss this reminder"), "Dismiss can be pressed");
            Check(await WaitFor(() => windows.WindowCount == 0), "Dismiss closes the tug windows");
            Check(controller.State == TugState.Hidden && controller.Current is null, "the controller owes the engine nothing after Dismiss");
        }

        // ---- the tray: the icon is in the notification area and its Test tug item reaches the real engine ----------------------
        // A runner without a taskbar (the hosted ARM64 image has none) cannot take the icon: that must be recorded, not fatal,
        // and the icon appears when a taskbar does (the library recreates it on TaskbarCreated).
        var trayCreated = shell.TrayIcon is { IsCreated: true };
        Check(trayCreated || diagnostics.Entries.Any(e => e.Category == "tray" && e.Level == "error"), "the tray icon was created in the notification area, or its failure was recorded: created=" + trayCreated);
        Check(shell.TrayModel is { } tray && tray.Current.Rows.Single(r => r.Command == TrayCommand.Quit).IsEnabled, "the tray menu offers Quit");
        run.Info("tray icon file " + shell.TrayIcon?.AppliedIconFile + ", tooltip \"" + shell.TrayIcon?.AppliedTooltip + "\"");

        // ---- a second test tug (from the tray menu) is left outstanding so that quitting must close it -----------------------
        await shell.TrayModel!.InvokeAsync(TrayCommand.TestTug);
        Check(await WaitFor(() => windows.WindowCount == monitors.Monitors.Count), "a second test takeover is shown");
        Check(controller.Current is not null, "the second request is outstanding");

        // ---- quit: the open request is closed first, then everything is disposed ------------------------------------------------
        await shell.QuitAsync(exitProcess: false).WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        Check(controller.Current is null && controller.State == TugState.Hidden, "quitting closed the outstanding request");
        Check(shell.TrayIcon is null, "quitting removed the tray icon");
        Check(await WaitFor(() => windows.WindowCount == 0, 3000), "quitting closed the tug windows");
        var files = Directory.GetFiles(dataDirectory, "*", SearchOption.AllDirectories);
        run.Info("the engine wrote " + files.Length + " file(s) under its data folder: " + string.Join(", ", files.Select(f => Path.GetRelativePath(dataDirectory, f))));
        Check(files.Length > 0, "the engine used its data folder");
        Check(diagnostics.Entries.All(e => e.Level != "error" || e.Category == "tray"), "nothing was recorded as an error (a tray that could not be created aside): " + string.Join("; ", diagnostics.Entries.Where(e => e.Level == "error").Select(e => e.Category + ": " + e.Message)));
        lock (host.Diagnostics)
        {
            Check(host.Diagnostics.All(d => d.Level != "error"), "the engine reported no error: " + string.Join("; ", host.Diagnostics.Where(d => d.Level == "error").Select(d => d.Category + ": " + d.Message)));
        }

        // ---- an engine that cannot load: the person is told, nothing is left running unseen -------------------------------
        var failing = new AppShell(DispatcherQueue.GetForCurrentThread(), diagnostics, services =>
            services.AddSingleton<Pkg.IEngine>(_ => throw new DllNotFoundException("the self-test pretends the engine library is missing")));
        await failing.RunAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        Check(failing.Failed && failing.FailureReason?.Contains("DllNotFoundException", StringComparison.Ordinal) == true, "an engine that cannot load shows the failure window: " + failing.FailureReason);
        Check(failing.Welcome is null, "no welcome window opens when the engine did not start");
        Check(diagnostics.Entries.Any(e => e.Level == "error" && e.Category == "startup"), "the startup failure is recorded in diagnostics");
        if (failing.FailureWindow is { } failureWindow)
        {
            await SelfTestRunner.WaitForFrameAsync(failureWindow, cancellation, 800);
            Check(ScenarioUi.HasButton(failureWindow, "Close TimeTug") && ScenarioUi.Unnamed(failureWindow).Count == 0, "the failure window has a named Close button");
            await run.ShotAsync(failureWindow, "failure");
        }

        await failing.QuitAsync(exitProcess: false).WaitAsync(TimeSpan.FromSeconds(30), cancellation);

        return run.Finish();
    }

    private static async Task<AppState?> FirstStateAsync(IEngineClient client, CancellationToken cancellation)
    {
        var first = new TaskCompletionSource<AppState>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.State.Subscribe(new StateObserver(first));
        try
        {
            return await first.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellation);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private sealed class StateObserver(TaskCompletionSource<AppState> first) : IObserver<AppState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(AppState value) => first.TrySetResult(value);
    }
}
#endif
