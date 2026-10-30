#if SELFTEST
using System.Diagnostics;
using TimeTug.Platform.Capture;
using TimeTug.Platform.Instance;
using TimeTug.Presentation.Startup;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>cross-flavour-instance</c> (VM only: it needs a second distribution installed; <c>scripts/vm/vm-cross-flavour.sh</c> builds a Direct package,
/// installs it and writes its app user model id to <c>%LOCALAPPDATA%\TimeTug\selftest-other-aumid.txt</c>). This process stands for a running
/// development copy: it holds the Dev marker. The other distribution's app is then started the way the Start menu starts it. It must find the marker,
/// show the collision notice with the approved wording and exit with code 0 without starting its engine: nothing appears in its data folder. After this
/// process lets go of its marker, a new first copy sees no other distribution.
/// </summary>
internal static class CrossFlavourInstanceScenario
{
    public const string Name = "cross-flavour-instance";

    private const string Notice = "TimeTug is already running from a different installation. Close it to use this copy.";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var file = Path.Combine(local, "TimeTug", "selftest-other-aumid.txt");
        if (!run.Check(File.Exists(file), "selftest-other-aumid.txt names the other distribution's app (scripts/vm/vm-cross-flavour.sh writes it)"))
        {
            return run.Finish();
        }

        var aumid = File.ReadAllText(file).Trim();
        var family = aumid.Split('!')[0];
        var data = Path.Combine(local, "Packages", family, "LocalState");
        run.Info("the other distribution is package family " + family);

        var markers = new NamedMutexMarkers();
        using (var guard = DistributionGuard.Enter(Flavour.Dev, markers))
        {
            run.Check(guard.OthersRunning.Count == 0, "no other distribution is running before the test");
            run.Check(markers.IsPresent(DistributionGuard.MarkerName(Flavour.Dev)), "this copy holds the Dev marker");
            var filesBefore = CountFiles(data);

            var (processId, error) = PackagedLauncher.Activate(null, aumid);
            if (!run.Check(processId is not null, "the other distribution's app was started (" + (error ?? "ok") + ")"))
            {
                return run.Finish();
            }

            var pid = (int)processId!.Value;
            (nint Window, string Title, string Text)? notice = null;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(30) && notice is null && !cancellation.IsCancellationRequested)
            {
                notice = NoticeProbe.Find(pid);
                if (notice is null)
                {
                    await Task.Delay(250, cancellation);
                }
            }

            run.Check(notice is not null, "the other distribution shows a notice");
            if (notice is { } shown)
            {
                run.Info($"notice '{shown.Title}': {shown.Text}");
                run.Check(shown.Text == Notice, "the notice says: " + Notice);
                run.Check(shown.Title == "TimeTug", "the notice is titled TimeTug");
                NoticeProbe.Close(shown.Window);
            }

            var exit = await WaitForExitAsync(pid, TimeSpan.FromSeconds(30));
            run.Check(exit.Exited, "the other distribution exits once the notice is closed");
            run.Check(exit.ExitCode is null or 0, "it exits with code 0 (" + (exit.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "not readable") + ")");
            run.Check(CountFiles(data) == filesBefore, $"it wrote nothing to its data folder ({filesBefore} file(s) before and after)");
            run.Check(!File.Exists(Path.Combine(data, "logs", "timetug.log")), "it started no log: the engine never ran");
            run.Check(markers.IsPresent(DistributionGuard.MarkerName(Flavour.Dev)), "this copy still holds the Dev marker");
        }

        run.Check(!markers.IsPresent(DistributionGuard.MarkerName(Flavour.Dev)), "letting go of the marker removes it");
        using (var again = DistributionGuard.Enter(Flavour.Dev, markers))
        {
            run.Check(again.OthersRunning.Count == 0, "a new first copy sees no other distribution once the other one has exited");
        }

        return run.Finish();
    }

    private static int CountFiles(string folder) => Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count() : 0;

    private static async Task<(bool Exited, int? ExitCode)> WaitForExitAsync(int processId, TimeSpan limit)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var exited = await Task.Run(() => process.WaitForExit(limit));
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                return (false, null);
            }

            try
            {
                return (true, process.ExitCode);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return (true, null);
            }
        }
        catch (ArgumentException)
        {
            // Already gone before the handle could be opened.
            return (true, null);
        }
    }
}
#endif
