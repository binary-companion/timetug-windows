#if SELFTEST
using System.Diagnostics;
using TimeTug.Platform.Instance;
using TimeTug.Presentation.Engine;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>second-launch</c>: this process is the first copy (registered under the real key, <c>TimeTug.Main</c>) and the packaged
/// app is started again through its execution alias, as a person starting it from the Start menu would. Each second launch must
/// hand itself to this copy and exit with code 0 within seconds, leaving one process; this copy then opens its flyout.
/// <list type="number">
/// <item>A launch that arrives before startup has finished is held: the flyout stays closed until <c>MarkReady</c>, then opens once.</item>
/// <item>Two launches at once open one flyout.</item>
/// <item>A launch while a tug is showing changes nothing: the flyout stays closed, the tug stays, and the engine is sent nothing.</item>
/// </list>
/// </summary>
internal static class SecondLaunchScenario
{
    public const string Name = "second-launch";

    private static readonly TimeSpan ExitLimit = TimeSpan.FromSeconds(30);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = FlyoutRig.Create(options, cancellation);
        var run = rig.Run;
        var tug = rig.Tug;
        tug.Engine.Push(rig.State(rig.Day()));

        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "timetug-dev.exe");
        if (!run.Check(File.Exists(alias), "the execution alias exists: " + alias))
        {
            return tug.Finish();
        }

        var baseline = Process.GetProcessesByName("TimeTug").Length;
        run.Info($"{baseline} TimeTug process(es) before the second launches (this one included)");

        using var instance = AppInstanceService.CreateDefault(PackageFamily(), tug.Diagnostics);
        run.Check(instance.IsPrimary, "this copy is the first: it registered TimeTug.Main");
        if (!instance.IsPrimary)
        {
            return tug.Finish();
        }

        var activations = 0;
        instance.Activated += _ => Interlocked.Increment(ref activations);
        var openings = 0;
        rig.Host.OpenChanged += () =>
        {
            if (rig.Host.IsOpen)
            {
                openings++;
            }
        };
        var ui = new DispatcherQueueDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        using var trigger = FlyoutTriggers.OpenOnActivation(instance, rig.Host, ui);

        // ---- 1. a launch before startup has finished ----------------------------------------------------------------
        var early = await LaunchAsync(alias, run, "early");
        run.Check(early.Exited && early.ExitCode == 0, $"the second launch exits with code 0 ({early.Describe()})");
        run.Check(Process.GetProcessesByName("TimeTug").Length == baseline, "one process is left: the second launch did not stay");
        await tug.Settle(1500);
        run.Check(!rig.Host.IsOpen && openings == 0 && activations == 0, "the flyout stays closed while startup has not finished: the activation is held");

        instance.MarkReady(rig.Presence);
        run.Check(await tug.WaitFor(() => rig.Host.IsOpen), "the held activation opens the flyout once startup has finished");
        run.Check(activations == 1 && openings == 1, $"exactly one activation and one opening (activations {activations}, openings {openings})");
        if (rig.Window is { } window)
        {
            await SelfTestRunner.WaitForFrameAsync(window, cancellation, 700);
            await run.ShotAsync(window, "opened-by-second-launch");
        }

        await tug.HoldAsync();
        rig.Host.Close();
        await tug.Settle(700);

        // ---- 2. two launches at once --------------------------------------------------------------------------------
        activations = 0;
        openings = 0;
        var pair = await Task.WhenAll(LaunchAsync(alias, run, "pair-1"), LaunchAsync(alias, run, "pair-2"));
        run.Check(pair.All(p => p.Exited && p.ExitCode == 0), "both launches exit with code 0: " + string.Join("; ", pair.Select(p => p.Describe())));
        run.Check(await tug.WaitFor(() => rig.Host.IsOpen), "the flyout opens");
        await tug.Settle(1500);
        run.Check(openings == 1 && rig.Host.IsOpen, $"two launches at once open one flyout (openings {openings}, activations {activations})");
        run.Check(Process.GetProcessesByName("TimeTug").Length == baseline, "still one process");
        rig.Host.Close();
        await tug.Settle(700);

        // ---- 3. a launch while the tug shows ------------------------------------------------------------------------
        activations = 0;
        openings = 0;
        run.Check(await tug.PresentAsync(tug.Request("e-second", "Planning", TimeSpan.FromSeconds(180))), "the takeover fires and the tug shows");
        var during = await LaunchAsync(alias, run, "tug");
        run.Check(during.Exited && during.ExitCode == 0, $"the launch during the tug exits with code 0 ({during.Describe()})");
        await tug.Settle(1500);
        run.Check(!rig.Host.IsOpen && openings == 0 && activations == 0, $"the flyout does not open over the tug (activations {activations}, openings {openings})");
        run.Check(tug.Host.WindowCount == 1 && tug.Host.Primary is not null && rig.Presence.IsTugShowing, "the tug is unaffected");
        run.Check(tug.Terminal.Count == 0, "nothing was sent to the engine: " + string.Join(", ", tug.Terminal));
        run.Check(tug.Diagnostics.Entries.Any(e => e.Message.Contains("tug is showing", StringComparison.Ordinal)), "the ignored launch is in the log");
        run.Check(Process.GetProcessesByName("TimeTug").Length == baseline, "still one process");

        run.Check(ScenarioUi.Invoke(tug.Host.Primary!, "Dismiss this reminder"), "Dismiss is pressed");
        run.Check(await tug.WaitHiddenAsync(), "the tug closes");
        run.Check(tug.Terminal.SequenceEqual(["dismiss:e-second"]), "exactly one terminal command went to the engine: " + string.Join(", ", tug.Terminal));

        var after = await LaunchAsync(alias, run, "after");
        run.Check(after.Exited && after.ExitCode == 0, $"a launch after the tug exits with code 0 ({after.Describe()})");
        run.Check(await tug.WaitFor(() => rig.Host.IsOpen), "with the tug gone a launch opens the flyout again");
        rig.Host.Close();
        return tug.Finish();
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

    private sealed record Launched(bool Exited, int? ExitCode, double Seconds, string? Error)
    {
        public string Describe() => Error ?? (Exited ? $"exit code {ExitCode} after {Seconds:0.0} s" : $"still running after {Seconds:0.0} s");
    }

    /// <summary>Starts the packaged app through its alias (the way a person starting it again would) and waits for it to exit; a copy that stays is stopped.</summary>
    private static async Task<Launched> LaunchAsync(string alias, ScenarioRun run, string label)
    {
        var watch = Stopwatch.StartNew();
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(alias) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new Launched(false, null, 0, $"{label}: could not start ({e.GetType().Name}: {e.Message})");
        }

        if (process is null)
        {
            return new Launched(false, null, 0, label + ": no process was started");
        }

        using (process)
        {
            var exited = await Task.Run(() => process.WaitForExit(ExitLimit));
            if (!exited)
            {
                run.Info(label + ": the second launch did not exit; stopping it");
                process.Kill(entireProcessTree: true);
                return new Launched(false, null, watch.Elapsed.TotalSeconds, null);
            }

            return new Launched(true, process.ExitCode, watch.Elapsed.TotalSeconds, null);
        }
    }
}
#endif
