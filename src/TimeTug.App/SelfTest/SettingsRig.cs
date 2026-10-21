#if SELFTEST
using Microsoft.UI.Dispatching;
using TimeTug.App.Settings;
using TimeTug.Platform.OsState;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest;

/// <summary>
/// The real Settings window and its host over a tug rig's scripted engine, fake UI settings and an in-memory presentation store, on the
/// real monitors (the accessibility scenario). The settings-shell scenario uses the whole app shell instead.
/// </summary>
internal sealed class SettingsRig : IDisposable
{
    private SettingsRig(TugRig tug)
    {
        Tug = tug;
        var queue = DispatcherQueue.GetForCurrentThread();
        Text = new SettingsText(tug.Text);
        Store = new MemoryPresentationSettingsStore();
        Factory = new SettingsWindowFactory(Text, tug.Text, Store, new StaticMonitors(MonitorService.Enumerate()), tug.Settings,
            new SettingsPaneModels(() => new TugRulesViewModel(tug.Engine, new SettingsClient(tug.Engine), new DispatcherQueueDispatcher(queue), tug.Text)));
        var dispatcher = new DispatcherQueueDispatcher(queue);
        Host = new SettingsHost(
            Factory, () => new SettingsShellViewModel(tug.Engine, Text, tug.Settings, dispatcher, TimeProvider.System), dispatcher, tug.Diagnostics);
    }

    public TugRig Tug { get; }

    public SettingsText Text { get; }

    public MemoryPresentationSettingsStore Store { get; }

    public SettingsWindowFactory Factory { get; }

    public SettingsHost Host { get; }

    /// <summary>The Settings window while it is open.</summary>
    public SettingsWindow? Window => Host.IsOpen ? Factory.Last?.Window : null;

    public static SettingsRig Over(TugRig tug) => new(tug);

    /// <summary>Opens the Settings window and waits for it to be laid out; null when it did not open.</summary>
    public async Task<SettingsWindow?> OpenAsync(SettingsPane? pane = null, string? highlightId = null)
    {
        Host.Show(pane, highlightId);
        if (Window is not { } window)
        {
            return null;
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

    public void Dispose()
    {
        Host.Dispose();
        Tug.Settings.AnimationsEnabled = true;
    }
}
#endif
