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
    IReadOnlyList<SelfTestShot> Attempts);

/// <summary>Runs a named self-test scenario inside the real app and writes <c>selftest-result.json</c>. Exists only in SELFTEST builds.</summary>
internal static class SelfTestRunner
{
    private static readonly TimeSpan Overall = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task RunAndExitAsync(Window window, SelfTestOptions options)
    {
        SelfTestResult result;
        try
        {
            using var timeout = new CancellationTokenSource(Overall);
            result = await RunAsync(window, options, timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or COMException or IOException or InvalidOperationException)
        {
            result = Result(options, null, ex.GetType().Name + ": " + ex.Message, []);
        }

        Directory.CreateDirectory(options.OutputFolder);
        await File.WriteAllTextAsync(Path.Combine(options.OutputFolder, "selftest-result.json"), JsonSerializer.Serialize(result, JsonOptions));
        Application.Current.Exit();
    }

    public static async Task<SelfTestResult> RunAsync(Window window, SelfTestOptions options, CancellationToken cancellation)
    {
        if (options.Scenario != "hello")
        {
            return Result(options, null, "unknown scenario " + options.Scenario, []);
        }

        var root = (FrameworkElement)window.Content;
        if (!root.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            root.Loaded += (_, _) => loaded.TrySetResult();
            await loaded.Task.WaitAsync(cancellation);
        }

        // Give the compositor a moment to present the first frame.
        await Task.Delay(1500, cancellation);

        var attempts = new List<SelfTestShot>();
        var folder = options.OutputFolder;

        var printWindow = await ScreenCapture.WindowAsync(WindowNative.GetWindowHandle(window), Path.Combine(folder, "hello-printwindow.png"));
        attempts.Add(new SelfTestShot("hello-printwindow", printWindow.Method, printWindow.Path, printWindow.DistinctColours, printWindow.Error));

        var xaml = await CaptureXamlAsync(root, Path.Combine(folder, "hello-xaml.png"));
        attempts.Add(xaml);

        // hello.png is the first attempt that is not blank.
        var best = printWindow.Succeeded
            ? new SelfTestShot("hello", printWindow.Method, printWindow.Path, printWindow.DistinctColours, null)
            : xaml.DistinctColours > 1 ? xaml with { Name = "hello" } : null;
        if (best?.File is { } source)
        {
            File.Copy(source, Path.Combine(folder, "hello.png"), overwrite: true);
        }

        return Result(options, best?.File is null ? null : "hello.png", best is null ? "no capture method produced a non-blank image" : null, attempts);
    }

    private static async Task<SelfTestShot> CaptureXamlAsync(FrameworkElement root, string path)
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
            return new SelfTestShot("hello-xaml", method, path, PixelAnalysis.CountDistinctColours(pixels, 1000), null);
        }
        catch (Exception ex) when (ex is COMException or IOException or ArgumentException or InvalidOperationException)
        {
            return new SelfTestShot("hello-xaml", method, null, 0, ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static SelfTestResult Result(SelfTestOptions options, string? screenshot, string? error, IReadOnlyList<SelfTestShot> attempts)
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
            Attempts: attempts);
    }
}
#endif
