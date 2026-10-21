#if SELFTEST
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using TimeTug.App.SelfTest.Scenarios;
using TimeTug.Platform.Capture;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace TimeTug.App.SelfTest;

/// <summary>Parsed <c>--selftest &lt;scenario&gt; [--out &lt;folder&gt;] [--hold &lt;seconds&gt;]</c> switch. <c>--hold</c> keeps the first thing a scenario shows on screen that long, for a person or a screen capture to look at.</summary>
internal sealed record SelfTestOptions(string Scenario, string OutputFolder, int HoldSeconds = 0)
{
    public static SelfTestOptions? Parse(IReadOnlyList<string> args)
    {
        string? scenario = null;
        string? output = null;
        var hold = 0;
        for (var i = 1; i < args.Count; i++)
        {
            if (args[i] == "--selftest" && i + 1 < args.Count)
            {
                scenario = args[++i];
            }
            else if (args[i] == "--out" && i + 1 < args.Count)
            {
                output = args[++i];
            }
            else if (args[i] == "--hold" && i + 1 < args.Count)
            {
                _ = int.TryParse(args[++i], System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out hold);
            }
        }

        return scenario is null
            ? null
            : new SelfTestOptions(scenario, output ?? Path.Combine(Path.GetTempPath(), "timetug-selftest"), hold);
    }
}

/// <summary>
/// Runs a named self-test scenario inside the real app and writes <c>result.json</c> next to its screenshots. Exists only in
/// SELFTEST builds. Every scenario is one class in <c>Scenarios/</c> with a <c>Name</c> and a <c>RunAsync</c>; the table below
/// is the list <c>scripts/ci/selftest-scenarios.txt</c> must match (a repository check compares them).
/// </summary>
internal static class SelfTestRunner
{
    private static readonly TimeSpan Overall = TimeSpan.FromSeconds(120);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly Dictionary<string, Func<SelfTestOptions, CancellationToken, Task<SelfTestResult>>> Scenarios = new()
    {
        [HelloScenario.Name] = HelloScenario.RunAsync,
        [WelcomeScenario.Name] = WelcomeScenario.RunAsync,
        [ToastScenario.Name] = ToastScenario.RunAsync,
        [StartupScenario.Name] = StartupScenario.RunAsync,
        [TrayScenario.Name] = TrayScenario.RunAsync,
        [TrayStatesScenario.Name] = TrayStatesScenario.RunAsync,
        [TugBasicScenario.Name] = TugBasicScenario.RunAsync,
        [TugJoinScenario.Name] = TugJoinScenario.RunAsync,
        [TugSnoozeScenario.Name] = TugSnoozeScenario.RunAsync,
        [TugDismissScenario.Name] = TugDismissScenario.RunAsync,
        [TugNoFocusScenario.Name] = TugNoFocusScenario.RunAsync,
        [TugToastScenario.Name] = TugToastScenario.RunAsync,
        [TugLateReturnScenario.Name] = TugLateReturnScenario.RunAsync,
        [TugMeetingGoneScenario.Name] = TugMeetingGoneScenario.RunAsync,
        [TugJoinFailsScenario.Name] = TugJoinFailsScenario.RunAsync,
        [TugMonitorsScenario.Name] = TugMonitorsScenario.RunAsync,
        [TugContrastAndMotionScenario.Name] = TugContrastAndMotionScenario.RunAsync,
        [FlyoutEmptyScenario.Name] = FlyoutEmptyScenario.RunAsync,
        [FlyoutDayScenario.Name] = FlyoutDayScenario.RunAsync,
        [FlyoutProblemsScenario.Name] = FlyoutProblemsScenario.RunAsync,
        [FlyoutTugWinsScenario.Name] = FlyoutTugWinsScenario.RunAsync,
        [FlyoutAnchorScenario.Name] = FlyoutAnchorScenario.RunAsync,
        [SecondLaunchScenario.Name] = SecondLaunchScenario.RunAsync,
        [HotkeyScenario.Name] = HotkeyScenario.RunAsync,
        [ComposeScenario.Name] = ComposeScenario.RunAsync,
        [SettingsShellScenario.Name] = SettingsShellScenario.RunAsync,
        [A11yHoldScenario.Name] = A11yHoldScenario.RunAsync,
    };

    /// <summary>Runs the scenario, writes the result and exits.</summary>
    public static async Task RunAndExitAsync(SelfTestOptions options)
    {
        SelfTestResult result;
        try
        {
            var limit = Overall + TimeSpan.FromSeconds(options.HoldSeconds);
            using var timeout = new CancellationTokenSource(limit);
            // Something that never completes and ignores the token (a capture of a window that never rendered) must still end in a
            // result, so the wait itself has a limit.
            result = Scenarios.TryGetValue(options.Scenario, out var scenario)
                ? await scenario(options, timeout.Token).WaitAsync(limit + TimeSpan.FromSeconds(5))
                : Failure(options, "unknown scenario " + options.Scenario);
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the run must still write a result file for the script to read.
            result = Failure(options, ex.GetType().Name + ": " + ex.Message);
            Directory.CreateDirectory(options.OutputFolder);
            await File.WriteAllTextAsync(Path.Combine(options.OutputFolder, "selftest-exception.txt"), ex.ToString());
        }

        Directory.CreateDirectory(options.OutputFolder);
        await File.WriteAllTextAsync(Path.Combine(options.OutputFolder, "result.json"), JsonSerializer.Serialize(result, JsonOptions));
        Application.Current.Exit();
    }

    private static SelfTestResult Failure(SelfTestOptions options, string error) => new ScenarioRun(options).Finish(error);

    /// <summary>Waits until the window's content has loaded and had a moment to present its first frame.</summary>
    internal static async Task WaitForFrameAsync(Window window, CancellationToken cancellation, int settleMilliseconds = 1500)
    {
        var root = (FrameworkElement)window.Content;
        if (!root.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            root.Loaded += (_, _) => loaded.TrySetResult();
            await loaded.Task.WaitAsync(cancellation);
        }

        await Task.Delay(settleMilliseconds, cancellation);
    }

    /// <summary>
    /// Saves <c>&lt;name&gt;.png</c>: the window as Windows draws it (so dialogs and Mica are included), or the XAML root
    /// when that comes out blank. Returns the kept picture (null when every attempt was blank) and the attempts, for the result file.
    /// </summary>
    internal static async Task<(SelfTestShot? Best, List<SelfTestAttempt> Attempts)> CaptureAsync(Window window, string folder, string name)
    {
        var attempts = new List<SelfTestAttempt>();
        var scratch = Path.Combine(folder, ".attempts");
        Directory.CreateDirectory(scratch);
        var printWindow = await ScreenCapture.WindowAsync(WindowNative.GetWindowHandle(window), Path.Combine(scratch, name + "-printwindow.png"));
        attempts.Add(new SelfTestAttempt(name + "-printwindow", printWindow.Method, printWindow.DistinctColours, printWindow.Error));

        var xaml = await CaptureXamlAsync((FrameworkElement)window.Content, Path.Combine(scratch, name + "-xaml.png"), name + "-xaml");
        attempts.Add(xaml.Attempt);

        // <name>.png is the first attempt that is not blank.
        string? source = null;
        SelfTestShot? best = null;
        if (printWindow.Succeeded)
        {
            source = printWindow.Path;
            best = new SelfTestShot(name + ".png", printWindow.Method, printWindow.DistinctColours);
        }
        else if (xaml.Attempt.DistinctColours > 1)
        {
            source = xaml.Path;
            best = new SelfTestShot(name + ".png", xaml.Attempt.Method, xaml.Attempt.DistinctColours);
        }

        if (source is not null)
        {
            File.Copy(source, Path.Combine(folder, name + ".png"), overwrite: true);
        }

        foreach (var file in Directory.GetFiles(scratch, name + "-*.png"))
        {
            File.Delete(file);
        }

        if (!Directory.EnumerateFileSystemEntries(scratch).Any())
        {
            Directory.Delete(scratch);
        }

        return (best, attempts);
    }

    private static async Task<(SelfTestAttempt Attempt, string? Path)> CaptureXamlAsync(FrameworkElement root, string path, string name)
    {
        const string method = "RenderTargetBitmap";
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(root).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            var buffer = await bitmap.GetPixelsAsync();
            var pixels = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(pixels);
            }

            await PngWriter.WriteAsync(pixels, bitmap.PixelWidth, bitmap.PixelHeight, path);
            return (new SelfTestAttempt(name, method, PixelAnalysis.CountDistinctColours(pixels, 1000), null), path);
        }
        catch (Exception ex) when (ex is COMException or IOException or ArgumentException or InvalidOperationException or TimeoutException)
        {
            return (new SelfTestAttempt(name, method, 0, ex.GetType().Name + ": " + ex.Message), null);
        }
    }
}
#endif
