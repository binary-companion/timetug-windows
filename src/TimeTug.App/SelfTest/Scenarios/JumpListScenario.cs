#if SELFTEST
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using TimeTug.App.Flyout;
using TimeTug.App.Tug;
using TimeTug.Platform.Hotkey;
using TimeTug.Platform.Instance;
using TimeTug.Platform.JumpList;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.JumpList;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tug;
using WinJumpList = Windows.UI.StartScreen.JumpList;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>jump-list</c>: the real app shell over the real jump list (<c>Windows.UI.StartScreen.JumpList</c>), a scripted engine (invented
/// meetings) and this process as the first copy under the real instance key. It checks, in the order the app lives it:
/// <list type="number">
/// <item>startup saves the base list (three tasks) before the first state; a meeting in progress with a link adds "Join next meeting"; the list read back
/// from Windows has exactly those items, as tasks, with the fixed arguments; the file Windows keeps names no title, link or calendar;</item>
/// <item>each item is started the way the shell starts one (<c>ActivateApplication</c> with the item's arguments): the second process hands itself to this
/// copy through <c>AppInstance</c> redirection and each action has its one effect (flyout opens, Settings opens, the engine is sent the test tug, the link of
/// the meeting is opened);</item>
/// <item>the same arguments through the execution alias (the command-line path) do the same;</item>
/// <item>"Join next meeting" after the link is gone opens the flyout instead and opens no link;</item>
/// <item>an item chosen while the tug shows changes nothing and is logged;</item>
/// <item>quitting saves the base list again: no "Join next meeting" is left on disk.</item>
/// </list>
/// What it cannot do: right-click the taskbar button (the VM's taskbar is invisible to UI Automation); see docs/spikes/2026-10-jump-list.md.
/// </summary>
internal static class JumpListScenario
{
    public const string Name = "jump-list";

    // Invented content. None of it may appear in the list, its file or the log.
    private const string SecretTitle = "Budget offsite planning";
    private const string SecretLink = "https://meet.example.test/jump-room-4711";
    private const string SecretCalendar = "Confidential Calendar";

    private static readonly TimeSpan ExitLimit = TimeSpan.FromSeconds(30);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        FlyoutWindow.IgnoreSystemDeactivation = true;

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

        var queue = DispatcherQueue.GetForCurrentThread();
        var diagnostics = new RecordingDiagnostics();
        var text = new ResourceLoaderLocalizer();
        using var forms = new AccountFormService(queue, text);
        using var engine = new ScriptedEngineClient(forms);
        var launcher = new FakeLauncher();
        using var instance = AppInstanceService.CreateDefault(PackageFamily(), diagnostics);
        if (!run.Check(instance.IsPrimary, "this process is the first copy: it registered TimeTug.Main (close a running TimeTug first)"))
        {
            return run.Finish();
        }

        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "timetug-dev.exe");
        run.Check(File.Exists(alias), "the execution alias exists: " + alias);
        run.Check(WinJumpList.IsSupported(), "JumpList.IsSupported() is true for the packaged app");

        var presentation = new MemoryPresentationSettingsStore();
        var shell = new AppShell(queue, diagnostics, services =>
        {
            services.AddSingleton<IEngineClient>(engine);
            services.AddSingleton<ISessionState>(new FakeSession());
            services.AddSingleton<INotificationState>(new FakeNotifications());
            services.AddSingleton<IToastService>(new FakeToasts());
            services.AddSingleton<IUrlLauncher>(launcher);
            services.AddSingleton<IHotkeyStore>(new NoHotkeyStore());
            services.AddSingleton<IPresentationSettingsStore>(presentation);
        }, withTray: false, instance);

        // ---- 1. the base list at startup, before any state ----------------------------------------------------------------
        await ClearAsync();
        var started = shell.RunAsync();
        var baseArguments = new[] { "--jump flyout", "--jump test-tug", "--jump settings" };
        run.Check(await WaitFor(() => ArgumentsAsync().GetAwaiter().GetResult().SequenceEqual(baseArguments)),
            "startup saved the base list (Open today's meetings, Test tug, Settings) before the engine's first state");

        // ---- 2. a meeting in progress with a link adds "Join next meeting" ---------------------------------------------------
        var now = DateTimeOffset.UtcNow;
        engine.Push(Day(now, withLink: true));
        await started.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        run.Check(!shell.Failed, "the app started" + (shell.FailureReason is null ? string.Empty : ": " + shell.FailureReason));
        var withJoin = new[] { "--jump flyout", "--jump join-next", "--jump test-tug", "--jump settings" };
        run.Check(await WaitFor(() => ArgumentsAsync().GetAwaiter().GetResult().SequenceEqual(withJoin)), "a meeting in progress with a link adds Join next meeting, in the second place");

        var list = await WinJumpList.LoadCurrentAsync();
        run.Check(list.Items.Count == 4 && list.Items.All(i => i.Kind == Windows.UI.StartScreen.JumpListItemKind.Arguments && string.IsNullOrEmpty(i.GroupName) && !i.RemovedByUser),
            "read back from Windows: four items, all tasks (no group), none removed by the person");
        run.Info("items as Windows stores them: " + string.Join(" | ", list.Items.Select(i => i.DisplayName + " [" + i.Arguments + "]")));
        run.Check(list.SystemGroupKind == Windows.UI.StartScreen.JumpListSystemGroupKind.None, "no Recent category");
        run.Check(list.Items.Select(i => i.DisplayName).SequenceEqual(["Open today's meetings", "Join next meeting", "Test tug", "Settings"]), "the names are the generic, localized ones");
        run.Check(list.Items.All(i => !Contains(i.DisplayName + i.Arguments + i.Description, SecretTitle, SecretLink, SecretCalendar)), "no title, link or calendar name in any item");
        ScanDiskFile(run);

        // ---- 3. each item, started the way the shell starts one -----------------------------------------------------------------
        var flyout = shell.Flyout!;
        var settings = shell.Settings!;
        var tugs = () => engine.Sent.Count(s => s == "testTug");

        run.Check(await Launch(JumpAction.Settings, () => settings.IsOpen), "Settings: the Settings window opens");
        settings.Close();
        await Task.Delay(800, cancellation);

        var before = tugs();
        run.Check(await Launch(JumpAction.TestTug, () => tugs() == before + 1), "Test tug: the engine is sent the test tug once");
        await Task.Delay(800, cancellation);
        run.Check(tugs() == before + 1, "...and only once");

        run.Check(await Launch(JumpAction.JoinNext, () => launcher.Opened.Count == 1), "Join next meeting: the meeting's link is opened");
        run.Check(launcher.Opened.SequenceEqual([SecretLink]), "...the link of the meeting in progress, once");
        await Task.Delay(800, cancellation);

        var openings = 0;
        flyout.OpenChanged += () =>
        {
            if (flyout.IsOpen)
            {
                openings++;
            }
        };
        run.Check(await Launch(JumpAction.Flyout, () => flyout.IsOpen), "Open today's meetings: the flyout opens");
        await Task.Delay(800, cancellation);
        run.Check(openings == 1, "...once: " + openings);
        if (shell.Services.GetRequiredService<IFlyoutSurfaceFactory>() is FlyoutSurfaceFactory { Last.Window: { } window })
        {
            await SelfTestRunner.WaitForFrameAsync(window, cancellation, 700);
            await run.ShotAsync(window, "opened-by-the-jump-list");
        }

        flyout.Close();
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(200), cancellation);

        // ---- 4. the command-line path: the same arguments through the execution alias ------------------------------------------
        before = tugs();
        var viaAlias = await LaunchAliasAsync(alias, "--jump test-tug");
        run.Check(viaAlias.Exited && viaAlias.ExitCode == 0, $"the alias launch exits with code 0 ({viaAlias.Describe()})");
        run.Check(await WaitFor(() => tugs() == before + 1), "--jump test-tug through the alias reaches this copy: the test tug once");
        await Task.Delay(800, cancellation);
        run.Check(tugs() == before + 1, "...and only once");

        var plainAlias = await LaunchAliasAsync(alias, "--jump nonsense");
        run.Check(plainAlias.Exited && plainAlias.ExitCode == 0, $"an unknown jump word exits with code 0 ({plainAlias.Describe()})");
        run.Check(await WaitFor(() => flyout.IsOpen), "an unknown jump word is an ordinary second launch: the flyout opens");
        run.Check(tugs() == before + 1 && launcher.Opened.Count == 1, "...and no jump action ran");
        flyout.Close();
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(200), cancellation);

        // ---- 5. a stale Join next meeting (the link is gone) opens the flyout instead -------------------------------------------
        engine.Push(Day(now, withLink: false));
        run.Check(await WaitFor(() => ArgumentsAsync().GetAwaiter().GetResult().SequenceEqual(baseArguments)), "when the link is gone the list goes back to the base list");
        var stale = await ActivateAsync("--jump join-next");
        run.Check(stale, "a stale Join next meeting is started");
        run.Check(await WaitFor(() => flyout.IsOpen), "a stale Join next meeting opens the flyout instead");
        run.Check(launcher.Opened.Count == 1, "...and opens no link");
        flyout.Close();
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(200), cancellation);

        // ---- 6. an item chosen while the tug shows changes nothing ----------------------------------------------------------------
        engine.Push(Day(now, withLink: true));
        var controller = shell.Services.GetRequiredService<TugController>();
        var windows = shell.Services.GetRequiredService<TugWindowHost>();
        var request = new TakeoverRequest("e-jump", "Planning", DateTimeOffset.UtcNow.AddSeconds(180), DateTimeOffset.UtcNow.AddMinutes(33), false, false,
            "https://meet.example.test/jump-tug", [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300)], "Work", 5);
        await controller.OnTakeoverDueAsync(request);
        run.Check(await WaitFor(() => windows.Primary is not null), "the tug shows");
        before = tugs();
        var opened = launcher.Opened.Count;
        await ActivateAsync("--jump test-tug");
        await ActivateAsync("--jump join-next");
        await Task.Delay(2500, cancellation);
        run.Check(tugs() == before && launcher.Opened.Count == opened && !flyout.IsOpen && !settings.IsOpen, "while the tug shows, Test tug and Join next meeting change nothing");
        run.Check(diagnostics.Entries.Any(e => e.Message.Contains("tug is showing", StringComparison.Ordinal) && e.Category == "instance"), "the ignored items are in the log");
        if (windows.Primary is { } primary)
        {
            run.Check(ScenarioUi.Invoke(primary, "Dismiss this reminder"), "Dismiss is pressed");
        }

        run.Check(await WaitFor(() => windows.WindowCount == 0), "the tug closes");

        // ---- 7. quitting leaves the base list on disk ---------------------------------------------------------------------------------
        run.Check(await WaitFor(() => ArgumentsAsync().GetAwaiter().GetResult().SequenceEqual(withJoin)), "before quitting, the list holds Join next meeting again");
        await shell.QuitAsync(exitProcess: false).WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        run.Check((await ArgumentsAsync()).SequenceEqual(baseArguments), "quitting saved the base list: no Join next meeting is left on disk");
        ScanDiskFile(run);
        run.Check(diagnostics.Entries.All(e => !Contains(e.Message + e.Category, SecretTitle, SecretLink, SecretCalendar, "meet.example.test")), "the log names no title, link or calendar");
        run.Check(diagnostics.Entries.All(e => e.Level != "error"), "nothing was recorded as an error: " + string.Join("; ", diagnostics.Entries.Where(e => e.Level == "error").Select(e => e.Category + ": " + e.Message)));
        run.Info("log (jump list and instance): " + string.Join(" | ", diagnostics.Entries.Where(e => e.Category is "jumplist" or "instance").Select(e => e.Message)));
        return run.Finish();

        async Task<bool> Launch(JumpAction action, Func<bool> effect)
        {
            if (!await ActivateAsync(JumpArguments.For(action)))
            {
                return false;
            }

            return await WaitFor(effect);
        }

        async Task<bool> ActivateAsync(string arguments)
        {
            var (pid, error) = PackagedLauncher.Activate(arguments);
            run.Check(pid is not null, $"ActivateApplication \"{arguments}\" started the app" + (error is null ? string.Empty : ": " + error));
            if (pid is { } id)
            {
                await WaitForExitAsync(run, id, cancellation);
            }

            return pid is not null;
        }
    }

    private static AppState Day(DateTimeOffset now, bool withLink) => Scenes.Connected(now) with
    {
        Agenda =
        [
            new AgendaItem("j1", SecretTitle, now.AddMinutes(-10), now.AddMinutes(40), false, AgendaPhase.Current, SecretCalendar, "#2E7AF5", withLink ? new Uri(SecretLink) : null, 4, false, null),
            new AgendaItem("j2", "Weekly sync", now.AddMinutes(90), now.AddMinutes(120), false, AgendaPhase.Upcoming, "Work", "#E8710A", null, 2, false, null) { IsNext = true },
        ],
        LeadTime = TimeSpan.FromSeconds(60),
    };

    private static bool Contains(string haystack, params string[] needles) => needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static async Task ClearAsync()
    {
        var list = await WinJumpList.LoadCurrentAsync();
        list.Items.Clear();
        await list.SaveAsync();
    }

    private static async Task<List<string>> ArgumentsAsync()
    {
        var list = await WinJumpList.LoadCurrentAsync();
        return list.Items.Select(i => i.Arguments).ToList();
    }

    /// <summary>The files Windows keeps jump lists in: the one holding our item names must hold no title, link or calendar name (UTF-16 or UTF-8).</summary>
    private static void ScanDiskFile(ScenarioRun run)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent", "CustomDestinations");
        var marker = Encoding.Unicode.GetBytes("Test tug");
        var found = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.customDestinations-ms"))
            {
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(file);
                }
                catch (IOException)
                {
                    continue;
                }

                if (bytes.AsSpan().IndexOf(marker) < 0)
                {
                    continue;
                }

                found++;
                foreach (var secret in new[] { SecretTitle, SecretLink, SecretCalendar, "example.test" })
                {
                    run.Check(bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(secret)) < 0 && bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) < 0,
                        $"the file Windows keeps ({bytes.Length} bytes) does not contain \"{(secret.Length > 12 ? secret[..12] : secret)}...\"");
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            run.Info("the jump list file could not be read: " + e.GetType().Name);
        }

        run.Info($"files holding our item names: {found}");
    }

    private static async Task WaitForExitAsync(ScenarioRun run, uint processId, CancellationToken cancellation)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var exited = await Task.Run(() => process.WaitForExit(ExitLimit), cancellation);
            if (!exited)
            {
                run.Info("the second process did not exit; stopping it");
                process.Kill(entireProcessTree: true);
            }
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    private sealed record Launched(bool Exited, int? ExitCode, double Seconds, string? Error)
    {
        public string Describe() => Error ?? (Exited ? $"exit code {ExitCode} after {Seconds:0.0} s" : $"still running after {Seconds:0.0} s");
    }

    private static async Task<Launched> LaunchAliasAsync(string alias, string arguments)
    {
        var watch = Stopwatch.StartNew();
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(alias, arguments) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new Launched(false, null, 0, "could not start (" + e.GetType().Name + ")");
        }

        if (process is null)
        {
            return new Launched(false, null, 0, "no process was started");
        }

        using (process)
        {
            var exited = await Task.Run(() => process.WaitForExit(ExitLimit));
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                return new Launched(false, null, watch.Elapsed.TotalSeconds, null);
            }

            return new Launched(true, process.ExitCode, watch.Elapsed.TotalSeconds, null);
        }
    }

    private static string? PackageFamily()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.Id.FamilyName;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private sealed class NoHotkeyStore : IHotkeyStore
    {
        private HotkeyState _state = HotkeyState.None;

        public HotkeyState Load() => _state;

        public void Save(HotkeyState value) => _state = value;
    }
}
#endif
