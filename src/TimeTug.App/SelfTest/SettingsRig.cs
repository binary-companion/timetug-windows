#if SELFTEST
using Microsoft.UI.Dispatching;
using TimeTug.App.Settings;
using TimeTug.Platform.Clipboard;
using TimeTug.Platform.Diagnostics;
using TimeTug.Platform.OsState;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Diagnostics;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest;

/// <summary>
/// The real Settings window and its host over a tug rig's scripted engine, fake UI settings and an in-memory presentation store, on the
/// real monitors (the accessibility scenario). The settings-shell scenario uses the whole app shell instead.
/// </summary>
internal sealed class SettingsRig : IDisposable
{
    private readonly SettingsClient _client;

    private SettingsRig(TugRig tug)
    {
        Tug = tug;
        var queue = DispatcherQueue.GetForCurrentThread();
        Text = new SettingsText(tug.Text);
        Store = new MemoryPresentationSettingsStore();
        var dispatcher = new DispatcherQueueDispatcher(queue);
        _client = new SettingsClient(tug.Engine);
        LogFolder = Path.Combine(tug.Run.Folder, "logs");
        Log = new RollingFileLog(LogFolder);
        Clipboard = new RecordingClipboard();
        Factory = new SettingsWindowFactory(Text, tug.Text, Store, new StaticMonitors(MonitorService.Enumerate()), tug.Settings,
            new SettingsPaneModels(
                () => new TugRulesViewModel(tug.Engine, _client, dispatcher, tug.Text),
                () => new CalendarsViewModel(tug.Engine, _client, dispatcher, tug.Text, inferenceSupported: false),
                () => new AccountsViewModel(tug.Engine, dispatcher, tug.Text, TimeProvider.System),
                () => new DiagnosticsViewModel(tug.Engine, new DiagnosticsReader(LogFolder), Clipboard, dispatcher, tug.Text, TimeProvider.System, "1.0.0.0", "10.0.26100.0 (Test)")));
        Host = new SettingsHost(
            Factory, () => new SettingsShellViewModel(tug.Engine, Text, tug.Settings, dispatcher, TimeProvider.System), dispatcher, tug.Diagnostics);
    }

    public TugRig Tug { get; }

    public SettingsText Text { get; }

    public MemoryPresentationSettingsStore Store { get; }

    public SettingsWindowFactory Factory { get; }

    public SettingsHost Host { get; }

    /// <summary>The folder of the log Copy Diagnostics reads, and a writer for it (the app's own rolling log).</summary>
    public string LogFolder { get; }

    public RollingFileLog Log { get; }

    /// <summary>Takes what Copy Diagnostics copies (and tries the real clipboard too, to note whether it works on this machine).</summary>
    public RecordingClipboard Clipboard { get; }

    /// <summary>The Settings window while it is open.</summary>
    public SettingsWindow? Window => Host.IsOpen ? Factory.Last?.Window : null;

    public static SettingsRig Over(TugRig tug) => new(tug);

    /// <summary>Opens the Settings window and waits for it to be laid out; null when it did not open. <paramref name="large"/> sizes it to most of the work area.</summary>
    public async Task<SettingsWindow?> OpenAsync(SettingsPane? pane = null, string? highlightId = null, bool large = false)
    {
        Host.Show(pane, highlightId);
        if (Window is not { } window)
        {
            return null;
        }

        if (large)
        {
            var work = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + 20, work.Y + 20, Math.Min(1700, work.Width - 40), Math.Min(1500, work.Height - 40)));
        }

        await SelfTestRunner.WaitForFrameAsync(window, CancellationToken.None, 900).WaitAsync(TimeSpan.FromSeconds(10));
        return window;
    }

    public static AppState State(DateTimeOffset now, bool tugRequirements = false) => Scenes.Connected(now) with
    {
        Settings = EngineSettingsView.Defaults with { RequireConferenceLink = tugRequirements, RequireOtherAttendees = tugRequirements },
        CalendarGroups =
        [
            new CalendarGroup("you@example.com", "google-conn-1", "google", CalendarOrigin.Account, "you@example.com · connected directly", false,
            [
                new CalendarEntry("google-conn-1/work", "Work", "#2E7AF5", true, true, "standard", false, false, "you@example.com"),
                new CalendarEntry("google-conn-1/cafe", "Café Sync", "#34A853", true, false, "standard", false, false, "you@example.com"),
                new CalendarEntry("google-conn-1/family", "Family", "#E8710A", false, false, "standard", false, false, "you@example.com"),
            ]) { Title = "Google" },
        ],
        SystemCalendars = [new CalendarEntry("google-conn-1/holidays", "Holidays", null, false, false, "subscribed", true, true)],
    };

    /// <summary>Connected accounts in every status the pane can show, with the engine's kinds (Microsoft is not set up in this build) and one event title that must never reach a report.</summary>
    public static AppState AccountsState(DateTimeOffset now) => Scenes.Empty() with
    {
        Accounts =
        [
            new AccountInfo("conn-1", "google", "you@example.com", "ok", 3),
            new AccountInfo("conn-2", "microsoft", "work@example.com", "authExpired", 2),
            new AccountInfo("conn-3", "icloud", "family@example.com", "needsPermission", 1),
            new AccountInfo("conn-4", "caldav", "Team CalDAV", "failing", 4),
            new AccountInfo("conn-5", "caldav", "New CalDAV", "ok", 0) { StatusKnown = false },
            new AccountInfo("conn-6", "icalsub", "Team feed", "ok", 1) { UnreadableRecurrences = 3 },
            new AccountInfo("conn-7", "google", "old@example.com", "failing", 0) { BuildFailure = true },
        ],
        Events = [new EventSummary("evt-1", "Design review", now.AddHours(2), now.AddHours(3), false, true)],
        NextMeeting = new NextMeetingInfo("Design review", now.AddHours(2), true),
    };

    public void Dispose()
    {
        Host.Dispose();
        _client.Dispose();
        Tug.Settings.AnimationsEnabled = true;
    }
}
#endif
