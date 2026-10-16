#if SELFTEST
using System.Drawing;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TimeTug.App.Tray;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tray;

namespace TimeTug.App.SelfTest;

/// <summary>
/// Scenario <c>tray</c>: a <see cref="TrayViewModel"/> over fakes and the app's real strings, driven through several engine
/// states. It asserts the menu rows, the icon file, the tooltip and what a left click and each menu command do, builds the
/// real flyout from the rows, loads every tray icon file at every size from the package, and saves a screenshot of the menu.
/// </summary>
internal static class TrayScenario
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        Directory.CreateDirectory(options.OutputFolder);
        var checks = new List<string>();
        var failures = new List<string>();
        var attempts = new List<SelfTestShot>();
        void Check(bool condition, string description)
        {
            checks.Add((condition ? "ok: " : "FAIL: ") + description);
            if (!condition)
            {
                failures.Add(description);
            }
        }

        var queue = DispatcherQueue.GetForCurrentThread();
        var text = new ResourceLoaderLocalizer();
        var anchor = new Window();
        using var forms = new AccountFormService(queue, text);
        using var engine = new ScriptedEngineClient(forms);
        var time = new FixedTime(Noon);
        var attention = new FakeAttention();
        var settings = new FakeUiSettings { TaskbarUsesLightTheme = false };
        var ticker = new ManualTicker();
        var host = new CountingHost();
        using var model = new TrayViewModel(engine, attention, settings, text, host, time, ticker, System.Globalization.CultureInfo.GetCultureInfo("en-US"));

        string[] Rows() => [.. model.Current.Rows.Where(r => r.Kind != TrayRowKind.Separator).Select(r => r.Text)];

        // ---- before the engine has spoken ----------------------------------------------------------------------------
        Check(Rows().SequenceEqual(["Starting…", "Enable Tug", "Test tug", "Settings…", "Quit TimeTug"]), "before the first state: " + string.Join(" | ", Rows()));
        Check(model.Current.Rows.Single(r => r.Command == TrayCommand.Quit).IsEnabled, "Quit works before the first state");

        // ---- a first run: no accounts ---------------------------------------------------------------------------------
        engine.Push(Scenes.Empty());
        Check(Rows().SequenceEqual(["No upcoming meetings", "Enable Tug", "Test tug", "Settings…", "Quit TimeTug"]), "no accounts: " + string.Join(" | ", Rows()));
        Check(model.Current.Icon.FileName == "tray-dark.ico" && model.Current.Icon.State == TrayIconState.Idle, "idle icon on a dark taskbar: " + model.Current.Icon.FileName);
        Check(model.Current.Tooltip == "TimeTug", "tooltip with no meeting: " + model.Current.Tooltip);
        model.PrimaryClick();
        Check(host.Welcomes == 1, "a left click with no accounts opens the welcome window");

        // ---- connected, the next meeting is far off --------------------------------------------------------------------
        engine.Push(Scenes.Connected(Noon));
        Check(Rows()[0] == "Next: Design review · 2:30 PM", "next meeting row: " + Rows()[0]);
        Check(model.Current.Rows[0].Kind == TrayRowKind.Information && !model.Current.Rows[0].IsEnabled, "the Next row is information only");
        Check(model.Current.Icon.State == TrayIconState.Idle, "a meeting two and a half hours away leaves the icon idle");
        Check(model.Current.Tooltip == "Design review in 2 hr 30 min", "tooltip: " + model.Current.Tooltip);
        model.PrimaryClick();
        Check(host.Welcomes == 1, "a left click with accounts does nothing yet");
        Check(model.Current.Rows.Single(r => r.Command == TrayCommand.Settings) is { IsEnabled: false }, "Settings is a disabled placeholder");

        // ---- the engine says a takeover is near; the clock moves on ---------------------------------------------------------
        var meeting = new EventSummary("evt-1", "Design review", Noon.AddMinutes(150), Noon.AddMinutes(180), false, true);
        AppState Near(DateTimeOffset start, bool enabled = true) => Scenes.Connected(Noon) with
        {
            Events = [meeting with { Start = start, End = start.AddMinutes(30) }],
            NextMeeting = new NextMeetingInfo("Design review", start, true),
            NextTakeover = new NextTakeoverInfo("evt-1", start.AddMinutes(-1)),
            Enabled = enabled,
        };

        engine.Push(Near(Noon.AddMinutes(25)));
        Check(model.Current.Icon.State == TrayIconState.Idle && Rows()[0].StartsWith("Next:", StringComparison.Ordinal), "25 minutes ahead: idle, " + Rows()[0]);
        time.Advance(TimeSpan.FromMinutes(17));
        ticker.Tick();
        Check(model.Current.Icon.State == TrayIconState.Soon && model.Current.Icon.FileName == "tray-color.ico", "8 minutes ahead: soon, " + model.Current.Icon.FileName);
        Check(Rows()[0] == "Join Design review", "a link that is near turns the row into Join: " + Rows()[0]);
        Check(model.Current.Tooltip == "Design review in 8 min", "tooltip counts down: " + model.Current.Tooltip);
        var (menuShot, menuAttempts) = await ShowMenuAsync(model.Current, options, "tray-menu", cancellation);
        attempts.AddRange(menuAttempts);
        Check(menuShot is not null, "tray-menu.png (the menu with Join, soon icon state) is not blank");

        // ---- Enable Tug round trip and the taskbar theme -----------------------------------------------------------------
        await model.InvokeAsync(TrayCommand.EnableTug);
        Check(engine.Sent.Contains("enabled:False"), "Enable Tug sends settings.apply with the opposite value");
        engine.Push(Near(Noon.AddMinutes(25), enabled: false) with { NextTakeover = null });
        Check(!model.Current.Rows.Single(r => r.Command == TrayCommand.EnableTug).IsChecked, "the check mark follows the engine's answer");
        Check(model.Current.Tooltip.EndsWith("· Tug is off", StringComparison.Ordinal), "tooltip says the Tug is off: " + model.Current.Tooltip);
        settings.TaskbarUsesLightTheme = true;
        settings.Raise();
        Check(model.Current.Icon.FileName == "tray-light.ico", "a light taskbar gets the light icon even though the app theme is unchanged: " + model.Current.Icon.FileName);

        // ---- attention -----------------------------------------------------------------------------------------------------
        attention.NeedsAttention = true;
        Check(model.Current.Icon.State == TrayIconState.Attention && model.Current.Icon.FileName == "tray-attention.ico", "attention icon when a tug is held: " + model.Current.Icon.FileName);
        Check(model.Current.Tooltip == "TimeTug: a meeting needs you", "attention tooltip: " + model.Current.Tooltip);
        attention.NeedsAttention = false;

        // ---- commands ------------------------------------------------------------------------------------------------------
        await model.InvokeAsync(TrayCommand.TestTug);
        Check(host.TestTugs == 1, "Test tug asks the app for the test tug");
        await model.InvokeAsync(TrayCommand.Quit);
        await model.InvokeAsync(TrayCommand.Quit);
        Check(host.Quits == 1, "Quit calls the application shutdown once");

        // ---- the real menu built from the rows --------------------------------------------------------------------------------
        var flyout = TrayMenu.Build(model.Current, _ => new RelayCommand(() => { }));
        Check(flyout.Items.Count == model.Current.Rows.Count, $"the flyout has one item per row ({flyout.Items.Count})");
        Check(flyout.Items.OfType<MenuFlyoutSeparator>().Count() == 3, "three separators group the rows");
        Check(flyout.Items.OfType<ToggleMenuFlyoutItem>().Single().Text == "Enable Tug", "Enable Tug is a toggle item");
        Check(flyout.Items.OfType<MenuFlyoutItem>().Where(i => i is not ToggleMenuFlyoutItem).First() is { IsEnabled: false }, "the Next item is not selectable");

        // ---- every icon file at every size loads from the package ---------------------------------------------------------------
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray");
        foreach (var file in new[] { "tray-light.ico", "tray-dark.ico", "tray-color.ico", "tray-attention.ico" })
        {
            foreach (var size in new[] { 16, 20, 24, 32 })
            {
                try
                {
                    using var icon = new Icon(Path.Combine(folder, file), size, size);
                    Check(icon.Width == size && icon.Height == size, $"{file} has a {size}px picture (got {icon.Width}x{icon.Height})");
                }
                catch (Exception e) when (e is IOException or ArgumentException)
                {
                    Check(false, $"{file} at {size}px did not load: {e.GetType().Name}");
                }
            }
        }

        anchor.Close();
        return SelfTestRunner.Result(options, menuShot is null ? null : "tray-menu.png", failures.Count == 0 ? null : string.Join("; ", failures), attempts, checks);
    }

    /// <summary>
    /// Draws the rows of <paramref name="view"/> as a menu in a small window and captures it. Windows draws the real tray menu in
    /// its own popup window, which a window capture does not include, so this is a picture of the rows (text, check mark,
    /// unavailable rows greyed, separators); the real menu is checked on a screen capture.
    /// </summary>
    private static async Task<(SelfTestShot? Best, List<SelfTestShot> Attempts)> ShowMenuAsync(TrayView view, SelfTestOptions options, string name, CancellationToken cancellation)
    {
        var panel = new StackPanel { Width = 300, Padding = new Thickness(4), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) };
        foreach (var row in view.Rows)
        {
            if (row.Kind == TrayRowKind.Separator)
            {
                panel.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray) });
                continue;
            }

            var line = new Grid { Padding = new Thickness(8, 6, 8, 6), ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(24) }, new ColumnDefinition() } };
            var foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(row.IsEnabled ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.Gray);
            line.Children.Add(new TextBlock { Text = row.IsChecked ? "\u2713" : string.Empty, Foreground = foreground });
            var label = new TextBlock { Text = row.Text, Foreground = foreground };
            Grid.SetColumn(label, 1);
            line.Children.Add(label);
            panel.Children.Add(line);
        }

        var window = new Window { Title = "TimeTug tray menu", Content = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke), Children = { panel } } };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(420, 420));
        window.Activate();
        await SelfTestRunner.WaitForFrameAsync(window, cancellation, 800);
        var result = await SelfTestRunner.CaptureAsync(window, options.OutputFolder, name);
        window.Close();
        return result;
    }

    private sealed class FixedTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeAttention : ITugAttention
    {
        private bool _needs;

        public bool NeedsAttention
        {
            get => _needs;
            set
            {
                _needs = value;
                AttentionChanged?.Invoke();
            }
        }

        public event Action? AttentionChanged;
    }

    private sealed class ManualTicker : IMinuteTicker
    {
        private Action? _tick;

        public IDisposable Start(Action tick)
        {
            _tick = tick;
            return new Stop(this);
        }

        public void Tick() => _tick?.Invoke();

        private sealed class Stop(ManualTicker owner) : IDisposable
        {
            public void Dispose() => owner._tick = null;
        }
    }

    private sealed class CountingHost : ITrayHost
    {
        public int Welcomes { get; private set; }

        public int TestTugs { get; private set; }

        public int Quits { get; private set; }

        public void ShowWelcome() => Welcomes++;

        public ValueTask TestTugAsync()
        {
            TestTugs++;
            return ValueTask.CompletedTask;
        }

        public ValueTask QuitAsync()
        {
            Quits++;
            return ValueTask.CompletedTask;
        }
    }
}
#endif
