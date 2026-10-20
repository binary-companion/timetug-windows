#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TimeTug.App.Tray;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tray;
using System.Runtime.InteropServices.WindowsRuntime;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tray-states</c>: the real <see cref="TrayIconService"/> (with the real renderer and the icon files from the
/// package) over a <see cref="TrayViewModel"/> driven through the plan of one meeting: idle, a light taskbar, soon with the digits 9,
/// 5 and 1, no digit in the last minute, attention and back. At each step it asserts the tooltip text and the icon pixels (the
/// digit is drawn, the edges of the icon are the plain puppy, a plan that did not change leaves the same picture), writes every
/// applied icon to a PNG in the output folder and takes two screenshots: the applied icons enlarged on a dark and a light
/// taskbar colour, and the contact sheet of every digit at every size.
/// </summary>
internal static class TrayStatesScenario
{
    public const string Name = "tray-states";

    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        void Check(bool condition, string description) => run.Check(condition, description);

        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // Closing the last window ends a WinUI app; this one stays open (never shown) while the picture windows come and go.
        var anchor = new Window();
        var text = new ResourceLoaderLocalizer();
        using var forms = new AccountFormService(queue, text);
        using var engine = new ScriptedEngineClient(forms);
        var time = new TrayScenario.FixedTime(Noon);
        var attention = new TrayScenario.FakeAttention();
        var settings = new FakeUiSettings { TaskbarUsesLightTheme = false };
        var ticker = new TrayScenario.ManualTicker();
        var diagnostics = new RecordingDiagnostics();
        using var model = new TrayViewModel(engine, attention, settings, text, new TrayScenario.CountingHost(), time, ticker, System.Globalization.CultureInfo.GetCultureInfo("en-US"));

        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray");
        var reference = new TrayIconRenderer(new TrayIconFiles(assets));
        var size = TrayIconRenderer.NearestSize(TrayIconMetrics.SmallIconSize());
        run.Info($"the notification area draws icons at {size} px on this machine");

        // The real service. On a machine without a taskbar (the hosted ARM64 runner) the icon is not created; the plan is still applied.
        using var service = new TrayIconService(model, new InlineDispatcher(), diagnostics);
        service.Start();
        var applied = new List<(string Label, TrayIconImage Image)>();

        void Expect(string label, TrayIconState state, int? digit, bool light, string tooltip)
        {
            var wanted = reference.Render(state, digit, light, size);
            var shown = service.AppliedIcon;
            Check(service.AppliedTooltip == tooltip, $"{label}: tooltip \"{service.AppliedTooltip}\" is \"{tooltip}\"");
            Check(shown is not null && shown.Width == size && shown.HasSamePixels(wanted), $"{label}: the icon is the {state} puppy{(digit is { } d ? $" with the digit {d}" : " without a digit")} at {size} px");
            if (shown is null)
            {
                return;
            }

            var plain = reference.Render(state, null, light, size);
            var changed = CountDifferences(shown, plain);
            Check(digit is null ? changed == 0 : changed > size, $"{label}: {changed} pixel(s) differ from the plain icon ({(digit is null ? "none expected" : "the digit")})");
            Check(EdgesAreUntouched(shown, plain), $"{label}: the edge of the icon is the plain puppy");
            File.WriteAllBytes(Path.Combine(run.Folder, $"icon-{applied.Count + 1:00}-{label}-{size}px.png"), shown.ToPng());
            applied.Add((label, shown));
        }

        AppState Near(DateTimeOffset start) => Scenes.Connected(Noon) with
        {
            Events = [new EventSummary("evt-1", "Design review", start, start.AddMinutes(30), false, true)],
            NextMeeting = new NextMeetingInfo("Design review", start, true),
            NextTakeover = new NextTakeoverInfo("evt-1", start.AddMinutes(-1)),
        };

        // ---- idle ------------------------------------------------------------------------------------------------------------------
        engine.Push(Scenes.Empty());
        Expect("idle-no-meeting", TrayIconState.Idle, null, false, "TimeTug");
        engine.Push(Scenes.Connected(Noon));
        Expect("idle-far", TrayIconState.Idle, null, false, "Design review in 2 hr 30 min");

        settings.TaskbarUsesLightTheme = true;
        settings.Raise();
        Expect("idle-light-taskbar", TrayIconState.Idle, null, true, "Design review in 2 hr 30 min");
        settings.TaskbarUsesLightTheme = false;
        settings.Raise();
        Expect("idle-dark-taskbar", TrayIconState.Idle, null, false, "Design review in 2 hr 30 min");

        // ---- soon: the meeting starts in 9:30, then the clock moves on a minute at a time ---------------------------------------------
        var start = Noon.AddSeconds((9 * 60) + 30);
        engine.Push(Near(start));
        Expect("soon-9", TrayIconState.Soon, 9, false, "Design review in 9 min");

        time.Advance(TimeSpan.FromMinutes(4));
        ticker.Tick();
        Expect("soon-5", TrayIconState.Soon, 5, false, "Design review in 5 min");

        var before = service.AppliedIcon;
        ticker.Tick();
        Check(ReferenceEquals(service.AppliedIcon, before), "a tick that changes nothing leaves the same icon picture in place");

        time.Advance(TimeSpan.FromMinutes(4));
        ticker.Tick();
        Expect("soon-1", TrayIconState.Soon, 1, false, "Design review in 1 min");

        time.Advance(TimeSpan.FromMinutes(1));
        ticker.Tick();
        Expect("soon-last-minute", TrayIconState.Soon, null, false, "Design review in 1 min");

        // ---- attention, and back ---------------------------------------------------------------------------------------------------------
        attention.NeedsAttention = true;
        Expect("attention", TrayIconState.Attention, null, false, "TimeTug: a meeting needs you");
        attention.NeedsAttention = false;
        Expect("soon-after-attention", TrayIconState.Soon, null, false, "Design review in 1 min");

        // ---- the meeting has started and nothing is next: the quiet icon again ----------------------------------------------------------
        engine.Push(Scenes.Connected(Noon));
        Expect("idle-again", TrayIconState.Idle, null, false, "Design review in 2 hr 21 min");

        // ---- the screenshots: the applied icons enlarged, and the contact sheet ---------------------------------------------------------------
        var sheet = TrayContactSheet.Build(reference);
        File.WriteAllBytes(Path.Combine(run.Folder, "contact-sheet.png"), sheet.ToPng());
        var sheetWindow = new Window
        {
            Title = "TimeTug tray digits",
            Content = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Gray), Children = { new Image { Stretch = Stretch.Uniform, Source = ToBitmap(sheet) } } },
        };
        sheetWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 900));
        sheetWindow.Activate();
        await SelfTestRunner.WaitForFrameAsync(sheetWindow, cancellation, 1500);
        await run.ShotAsync(sheetWindow, "contact-sheet");
        sheetWindow.Close();
        var strip = BuildStrip(applied.Select(a => a.Image).ToList(), zoom: 4);
        var stripWindow = new Window
        {
            Title = "TimeTug tray states",
            Content = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Gray), Children = { new Image { Stretch = Stretch.Uniform, Source = ToBitmap(strip) } } },
        };
        stripWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 480));
        stripWindow.Activate();
        await SelfTestRunner.WaitForFrameAsync(stripWindow, cancellation, 1500);
        await run.ShotAsync(stripWindow, "states-enlarged");
        stripWindow.Close();
        anchor.Close();

        Check(diagnostics.Entries.All(e => e.Category != "tray" || e.Level != "error" || e.Message.Contains("TryCreate", StringComparison.Ordinal) || e.Message.Contains("could not be created", StringComparison.Ordinal)),
            "no icon could be drawn wrongly (only a tray that could not be created may be recorded): " + string.Join("; ", diagnostics.Entries.Select(e => e.Message)));
        return run.Finish();
    }

    private static int CountDifferences(TrayIconImage a, TrayIconImage b)
    {
        var n = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                n += a.PixelAt(x, y) == b.PixelAt(x, y) ? 0 : 1;
            }
        }

        return n;
    }

    /// <summary>The digit stays in the middle: a margin of a sixth of the icon around it is the plain puppy.</summary>
    private static bool EdgesAreUntouched(TrayIconImage image, TrayIconImage plain)
    {
        var margin = image.Width / 6;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if ((x < margin || y < margin || x >= image.Width - margin || y >= image.Height - margin) && image.PixelAt(x, y) != plain.PixelAt(x, y))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Every applied icon, enlarged by whole pixels, in a row on a dark and in a row on a light taskbar colour.</summary>
    private static TrayIconImage BuildStrip(IReadOnlyList<TrayIconImage> icons, int zoom)
    {
        const int Gap = 8;
        var cell = (icons[0].Width * zoom) + Gap;
        var width = Gap + (icons.Count * cell);
        var band = cell + Gap;
        var rgba = new byte[width * 2 * band * 4];
        for (var y = 0; y < 2 * band; y++)
        {
            var light = y >= band;
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                rgba[i] = rgba[i + 1] = rgba[i + 2] = (byte)(light ? 243 : 32);
                rgba[i + 3] = 255;
            }
        }

        for (var n = 0; n < icons.Count; n++)
        {
            for (var row = 0; row < 2; row++)
            {
                var background = row == 1 ? 243 : 32;
                for (var py = 0; py < icons[n].Height * zoom; py++)
                {
                    for (var px = 0; px < icons[n].Width * zoom; px++)
                    {
                        var (r, g, b, a) = icons[n].PixelAt(px / zoom, py / zoom);
                        var i = ((((row * band) + Gap + py) * width) + Gap + (n * cell) + px) * 4;
                        rgba[i] = (byte)(((r * a) + (background * (255 - a))) / 255);
                        rgba[i + 1] = (byte)(((g * a) + (background * (255 - a))) / 255);
                        rgba[i + 2] = (byte)(((b * a) + (background * (255 - a))) / 255);
                    }
                }
            }
        }

        return new TrayIconImage(width, 2 * band, rgba);
    }

    private static WriteableBitmap ToBitmap(TrayIconImage image)
    {
        var bitmap = new WriteableBitmap(image.Width, image.Height);
        var bgra = new byte[image.Width * image.Height * 4];
        var rgba = image.Rgba;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            // Premultiplied BGRA, which is what the bitmap holds.
            var alpha = rgba[i + 3];
            bgra[i] = (byte)((rgba[i + 2] * alpha) / 255);
            bgra[i + 1] = (byte)((rgba[i + 1] * alpha) / 255);
            bgra[i + 2] = (byte)((rgba[i] * alpha) / 255);
            bgra[i + 3] = alpha;
        }

        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Write(bgra, 0, bgra.Length);
        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>Runs posted work at once: the scenario already runs on the UI thread.</summary>
    private sealed class InlineDispatcher : IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }
}
#endif
