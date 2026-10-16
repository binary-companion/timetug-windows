#if SELFTEST
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using TimeTug.Platform.Capture;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace TimeTug.App.SelfTest;

/// <summary>Parsed <c>--selftest &lt;scenario&gt; [--out &lt;folder&gt;]</c> switch.</summary>
internal sealed record SelfTestOptions(string Scenario, string OutputFolder)
{
    public static SelfTestOptions? Parse(IReadOnlyList<string> args)
    {
        string? scenario = null;
        string? output = null;
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
        }

        return scenario is null
            ? null
            : new SelfTestOptions(scenario, output ?? Path.Combine(Path.GetTempPath(), "timetug-selftest"));
    }
}

internal sealed record SelfTestShot(string Name, string Method, string? File, int DistinctColours, string? Error);

internal sealed record SelfTestResult(
    bool Ok,
    string Scenario,
    string? Error,
    string Architecture,
    string? PackageFullName,
    string? ScreenshotFile,
    IReadOnlyList<SelfTestShot> Attempts,
    IReadOnlyList<string>? Checks = null);

/// <summary>Runs a named self-test scenario inside the real app and writes <c>selftest-result.json</c>. Exists only in SELFTEST builds.</summary>
internal static class SelfTestRunner
{
    private static readonly TimeSpan Overall = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Runs the scenario, writes the result and exits. <paramref name="window"/> is the hello window; other scenarios open their own.</summary>
    public static async Task RunAndExitAsync(Window? window, SelfTestOptions options)
    {
        SelfTestResult result;
        try
        {
            using var timeout = new CancellationTokenSource(Overall);
            result = await RunAsync(window, options, timeout.Token);
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the run must still write a result file for the script to read.
            result = Result(options, null, ex.GetType().Name + ": " + ex.Message, []);
        }

        Directory.CreateDirectory(options.OutputFolder);
        await File.WriteAllTextAsync(Path.Combine(options.OutputFolder, "selftest-result.json"), JsonSerializer.Serialize(result, JsonOptions));
        Application.Current.Exit();
    }

    public static async Task<SelfTestResult> RunAsync(Window? window, SelfTestOptions options, CancellationToken cancellation)
    {
        return options.Scenario switch
        {
            "hello" when window is not null => await HelloAsync(window, options, cancellation),
            "welcome" => await WelcomeScenario.RunAsync(options, cancellation),
            "toast" => await ToastScenario.RunAsync(options, cancellation),
            _ => Result(options, null, "unknown scenario " + options.Scenario, []),
        };
    }

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
    /// when that comes out blank. The attempts are returned for the result file.
    /// </summary>
    internal static async Task<(SelfTestShot? Best, List<SelfTestShot> Attempts)> CaptureAsync(Window window, string folder, string name)
    {
        var attempts = new List<SelfTestShot>();
        var printWindow = await ScreenCapture.WindowAsync(WindowNative.GetWindowHandle(window), Path.Combine(folder, name + "-printwindow.png"));
        attempts.Add(new SelfTestShot(name + "-printwindow", printWindow.Method, printWindow.Path, printWindow.DistinctColours, printWindow.Error));

        var xaml = await CaptureXamlAsync((FrameworkElement)window.Content, Path.Combine(folder, name + "-xaml.png"), name + "-xaml");
        attempts.Add(xaml);

        // <name>.png is the first attempt that is not blank.
        var best = printWindow.Succeeded
            ? new SelfTestShot(name, printWindow.Method, printWindow.Path, printWindow.DistinctColours, null)
            : xaml.DistinctColours > 1 ? xaml with { Name = name } : null;
        if (best?.File is { } source)
        {
            File.Copy(source, Path.Combine(folder, name + ".png"), overwrite: true);
        }

        return (best, attempts);
    }

    private static async Task<SelfTestResult> HelloAsync(Window window, SelfTestOptions options, CancellationToken cancellation)
    {
        await WaitForFrameAsync(window, cancellation);
        var (best, attempts) = await CaptureAsync(window, options.OutputFolder, "hello");
        return Result(options, best?.File is null ? null : "hello.png", best is null ? "no capture method produced a non-blank image" : null, attempts);
    }

    private static async Task<SelfTestShot> CaptureXamlAsync(FrameworkElement root, string path, string name)
    {
        const string method = "RenderTargetBitmap";
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(root);
            var buffer = await bitmap.GetPixelsAsync();
            var pixels = new byte[buffer.Length];
            using (var reader = DataReader.FromBuffer(buffer))
            {
                reader.ReadBytes(pixels);
            }

            await PngWriter.WriteAsync(pixels, bitmap.PixelWidth, bitmap.PixelHeight, path);
            return new SelfTestShot(name, method, path, PixelAnalysis.CountDistinctColours(pixels, 1000), null);
        }
        catch (Exception ex) when (ex is COMException or IOException or ArgumentException or InvalidOperationException)
        {
            return new SelfTestShot(name, method, null, 0, ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static SelfTestResult Result(SelfTestOptions options, string? screenshot, string? error, IReadOnlyList<SelfTestShot> attempts, IReadOnlyList<string>? checks = null)
    {
        string? package = null;
        try
        {
            package = Windows.ApplicationModel.Package.Current.Id.FullName;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // No package identity: reported as null so the run script can fail on it.
        }

        return new SelfTestResult(
            Ok: error is null && screenshot is not null,
            Scenario: options.Scenario,
            Error: error,
            Architecture: RuntimeInformation.ProcessArchitecture.ToString().ToUpper(CultureInfo.InvariantCulture),
            PackageFullName: package,
            ScreenshotFile: screenshot,
            Attempts: attempts,
            Checks: checks);
    }
}
#endif
