#if SELFTEST
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using TimeTug.App.Composition;
using TimeTug.App.Tug;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tug;
using Pkg = TimeTug.Engine;

namespace TimeTug.App.SelfTest;

/// <summary>
/// What the scenarios that need time to pass share: the real composition root over the real native engine on its manual test
/// clock, one Google account whose calendar answers come from recorded responses (<see cref="ScriptedCalendarHost"/>), and fakes for
/// the OS state (lock and unlock, monitors, notification state, toasts, the link launcher). The scenario's own <see cref="ManualClock"/>
/// is the app's clock; <see cref="AdvanceAsync"/> moves it and the engine's together, so what the engine fires and what the window
/// computes agree. Nothing here decides a rule: the engine decides when the takeover is due and the controller what to do with it.
/// </summary>
internal sealed class EngineRig : IAsyncDisposable
{
    /// <summary>Where both clocks start. The recorded meeting "Design review" starts at 13:16 and is due at 13:15.</summary>
    public static readonly DateTimeOffset ClockStart = new(2026, 10, 16, 13, 0, 0, TimeSpan.Zero);

    public const string EventsUrl = "https://www.googleapis.com/calendar/v3/calendars/me%40example.com/events";

    /// <summary>The id the engine gives the recorded meeting.</summary>
    public const string MeetingId = "google-conn-1/evt-design-review/1792156560";

    private readonly CancellationToken _cancellation;
    private readonly object _gate = new();
    private AppState? _latest;
    private IDisposable? _subscription;
    private AppShell? _shell;
    private string _calendarList = "google-calendar-list.json";

    private EngineRig(ScenarioRun run, CancellationToken cancellation)
    {
        Run = run;
        _cancellation = cancellation;
    }

    public ScenarioRun Run { get; }

    /// <summary>The running app (null before the rig started).</summary>
    public AppShell Shell => _shell ?? throw new InvalidOperationException("The rig has not started.");

    public ManualClock Clock { get; } = new(ClockStart);

    public ScriptedCalendarHost Host { get; } = new();

    public FakeSession Session { get; } = new();

    public FakeNotifications Notifications { get; } = new();

    public FakeMonitors Monitors { get; } = new();

    public FakeToasts Toasts { get; } = new();

    public FakeLauncher Launcher { get; } = new();

    public FakeUiSettings Settings { get; } = new();

    public RecordingDiagnostics Diagnostics { get; } = new();

    public RecordingEngineClient Client { get; private set; } = null!;

    public TugController Controller { get; private set; } = null!;

    public TugWindowHost Windows { get; private set; } = null!;

    public Pkg.IEngine Engine { get; private set; } = null!;

    /// <summary>The latest state the engine published (null before the first).</summary>
    public AppState? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>The commands that answer a takeover, in order (see <see cref="TerminalCommands"/>).</summary>
    public IReadOnlyList<string> Terminal => TerminalCommands.Of(Client.Sent);

    /// <summary>Starts the app with the recorded calendar and waits until the engine has published the meeting.</summary>
    /// <param name="calendarList">The recorded calendar-list answer (the single calendar by default). Every other calendar it names has no events.</param>
    public static async Task<EngineRig> StartAsync(SelfTestOptions options, CancellationToken cancellation, string calendarList = "google-calendar-list.json")
    {
        var rig = new EngineRig(new ScenarioRun(options), cancellation) { _calendarList = calendarList };
        await rig.StartCoreAsync();
        return rig;
    }

    public bool Check(bool condition, string name) => Run.Check(condition, name);

    public async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 10000)
    {
        for (var waited = 0; waited < milliseconds; waited += 100)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100, _cancellation);
        }

        return condition();
    }

    /// <summary>
    /// Opens the Settings window on a pane and sizes it to most of the primary display's work area (the rig's monitors carry no scale, so the
    /// window's own placement would make it too small to read in a screenshot).
    /// </summary>
    public async Task<TimeTug.App.Settings.SettingsWindow?> OpenSettingsAsync(SettingsPane pane)
    {
        var factory = Shell.Services.GetRequiredService<TimeTug.App.Settings.SettingsWindowFactory>();
        Shell.ShowSettings(pane);
        if (!await WaitFor(() => Shell.Settings is { IsOpen: true } && factory.Last is not null))
        {
            return null;
        }

        var window = factory.Last!.Window;
        var work = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        var width = Math.Min(1100, work.Width - 40);
        var height = Math.Min(860, work.Height - 40);
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + 20, work.Y + 20, width, height));
        await SelfTestRunner.WaitForFrameAsync(window, _cancellation, 900);
        return window;
    }

    public Task Settle(int milliseconds = 1200) => Task.Delay(milliseconds, _cancellation);

    /// <summary>Moves the app's clock and then the engine's by the same amount (the engine fires anything that has become due).</summary>
    public async Task AdvanceAsync(TimeSpan by)
    {
        Clock.Advance(by);
        await Engine.SendAsync(new Pkg.EngineCommand.AdvanceTestClock(by.TotalSeconds));
    }

    /// <summary>Asks the engine to fetch the calendars again.</summary>
    public ValueTask RefreshAsync() => Engine.SendAsync(new Pkg.EngineCommand.Refresh());

    /// <summary>Answers the calendar's events request with another recorded response from now on.</summary>
    public void AnswerEventsWith(string file) => Host.Answers.Answer(EventsUrl, file);

    public Task<bool> WaitHiddenAsync() => WaitFor(() => Windows.WindowCount == 0);

    public SelfTestResult Finish()
    {
        foreach (var entry in Diagnostics.Entries)
        {
            Run.Info($"app {entry.Level} {entry.Category}: {entry.Message}");
        }

        lock (Host.Diagnostics)
        {
            foreach (var d in Host.Diagnostics)
            {
                Run.Info($"engine {d.Level} {d.Category}: {d.Message}");
            }
        }

        Run.Info("credential calls: " + string.Join(", ", Host.Secrets.Calls));
        foreach (var request in Host.Answers.Requests)
        {
            Run.Info("http " + request);
        }

        Run.Check(Diagnostics.Entries.All(e => e.Level != "error"), "nothing was recorded as an error: " + string.Join("; ", Diagnostics.Entries.Where(e => e.Level == "error").Select(e => e.Category + ": " + e.Message)));
        lock (Host.Diagnostics)
        {
            Run.Check(Host.Diagnostics.All(d => d.Level != "error"), "the engine reported no error: " + string.Join("; ", Host.Diagnostics.Where(d => d.Level == "error").Select(d => d.Category + ": " + d.Message)));
        }

        return Run.Finish();
    }

    public async ValueTask DisposeAsync()
    {
        _subscription?.Dispose();
        if (_shell is not null)
        {
            await _shell.QuitAsync(exitProcess: false).WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
    }

    private async Task StartCoreAsync()
    {
        var folder = Path.Combine(Run.Folder, "engine-data");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        SelfTestData.CopyTo("engine-accounts.json", folder, "accounts.json");
        SelfTestData.CopyTo("engine-settings.json", folder, "settings.json");

        Host.Secrets.Seed("conn-1", "refresh_token", "test-refresh-token");
        Host.Answers.Answer("https://oauth2.googleapis.com/token", "google-token.json", "POST");
        Host.Answers.Answer("https://www.googleapis.com/calendar/v3/users/me/calendarList", _calendarList);
        Host.Answers.Answer("https://www.googleapis.com/calendar/v3/calendars/", "google-events-empty.json");
        Host.Answers.Answer(EventsUrl, "google-events-simple.json");

        var (main, second) = FakeMonitors.Layout();
        Monitors.Set(main, second);

        _shell = new AppShell(
            DispatcherQueue.GetForCurrentThread(),
            Diagnostics,
            services =>
            {
                var clients = new Dictionary<string, Pkg.OAuthClient> { ["google"] = new("test-client", "test-secret") };
                services.AddSingleton(new EngineStartup(folder, Testing: true, clients, ClockStart));
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<Pkg.IEngineHost>(Host);
                services.AddSingleton<ISessionState>(Session);
                services.AddSingleton<INotificationState>(Notifications);
                services.AddSingleton<IMonitorService>(Monitors);
                services.AddSingleton<IUiSettings>(Settings);
                services.AddSingleton<IToastService>(Toasts);
                services.AddSingleton<IUrlLauncher>(Launcher);
                // The Settings window remembers its place in memory, not in the package's real settings.
                services.AddSingleton<IPresentationSettingsStore>(new MemoryPresentationSettingsStore());
                services.AddSingleton<IEngineClient>(sp => new RecordingEngineClient(sp.GetRequiredService<EngineClient>()));
            },
            withTray: false);

        await _shell.RunAsync().WaitAsync(TimeSpan.FromSeconds(60), _cancellation);
        Run.Check(!_shell.Failed, "the app started with the real engine" + (_shell.FailureReason is null ? string.Empty : ": " + _shell.FailureReason));
        if (_shell.Failed)
        {
            throw new InvalidOperationException("the app did not start: " + _shell.FailureReason);
        }

        var services = _shell.Services;
        Client = (RecordingEngineClient)services.GetRequiredService<IEngineClient>();
        Controller = services.GetRequiredService<TugController>();
        Windows = services.GetRequiredService<TugWindowHost>();
        Engine = services.GetRequiredService<Pkg.IEngine>();
        _subscription = Client.State.Subscribe(new Observer<AppState>(state =>
        {
            lock (_gate)
            {
                _latest = state;
            }
        }));

        Run.Info("state: " + (Latest is { } state
            ? $"{state.Accounts.Count} account(s) [{string.Join(", ", state.Accounts.Select(a => a.ConnectionId + "=" + a.Status + "/" + a.CalendarCount + " calendars"))}], {state.Events.Count} event(s), kinds [{string.Join(", ", state.AvailableKinds.Select(k => k.Id + (k.IsConfigured ? "+" : "-")))}]"
            : "none"));
        var armed = Run.Check(await WaitFor(() => Latest is { NextTakeover: not null }), "the engine fetched the recorded calendar and armed a takeover for its meeting");
        var meeting = Latest is { Events.Count: > 0 } ? Latest.Events[0] : null;
        var known = Run.Check(meeting is { Title: "Design review" } && meeting.Id == MeetingId, "the meeting is Design review: " + meeting?.Id);
        var due = Run.Check(Latest?.NextTakeover?.FireAt == ClockStart.AddMinutes(15), "it is due at 13:15, a minute before it starts: " + Latest?.NextTakeover?.FireAt);
        Ready = armed && known && due;
    }

    /// <summary>True when the app started and the engine has armed the recorded meeting; a scenario stops at once when it is not.</summary>
    public bool Ready { get; private set; }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => onNext(value);
    }
}
#endif
