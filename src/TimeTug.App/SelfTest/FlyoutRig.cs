#if SELFTEST
using System.Globalization;
using Microsoft.UI.Xaml;
using TimeTug.App.Flyout;
using TimeTug.Platform.Flyout;
using TimeTug.Platform.OsState;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;

namespace TimeTug.App.SelfTest;

/// <summary>
/// What the flyout scenarios share: the tug rig (scripted engine, real <see cref="TimeTug.Presentation.Tug.TugController"/>, fakes) plus
/// the real <see cref="FlyoutViewModel"/> on a standing clock (Friday 16 October 2026, 10:20 UTC, so a screenshot reads the same
/// everywhere), the real <see cref="FlyoutHost"/> and window, and the real monitors and their work areas. The tray icon is stood in
/// for by a fixed rectangle near the bottom-right corner of the main monitor (the anchor scenario uses the real tray icon).
/// </summary>
internal sealed class FlyoutRig : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 16, 10, 20, 0, TimeSpan.Zero);

    private readonly bool _ownsTug;

    private FlyoutRig(TugRig tug, bool ownsTug, ITrayAnchor? hostAnchor, TimeTug.Presentation.Settings.MemoryPresentationSettingsStore? store = null)
    {
        FlyoutWindow.IgnoreSystemDeactivation = true;
        Tug = tug;
        _ownsTug = ownsTug;
        Monitors = new StaticMonitors(MonitorService.Enumerate());
        var main = Monitors.Primary;
        Anchor = new FixedAnchor(new PixelRect(main.X + main.Width - 90, main.Y + main.Height - 40, 24, 24));
        Model = new FlyoutViewModel(tug.Engine, tug.Launcher, Clock, new MinuteTicker(TimeProvider.System), tug.Text, CultureInfo.GetCultureInfo("en-US"));
        var ui = new DispatcherQueueDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        Factory = new FlyoutSurfaceFactory(Model, ui, tug.Text, tug.Settings);
        Presence = new TimeTug.Presentation.Flyout.TugPresence(tug.Controller);
        Store = store ?? new TimeTug.Presentation.Settings.MemoryPresentationSettingsStore();
        Host = new FlyoutHost(Factory, Presence, tug.Settings, hostAnchor ?? Anchor, Monitors, ui, TimeProvider.System, tug.Diagnostics, Store);
    }

    public TugRig Tug { get; }

    public ScenarioRun Run => Tug.Run;

    public ManualClock Clock { get; } = new(Start);

    public StaticMonitors Monitors { get; }

    public FixedAnchor Anchor { get; }

    public FlyoutViewModel Model { get; }

    public FlyoutSurfaceFactory Factory { get; }

    public TimeTug.Presentation.Flyout.TugPresence Presence { get; }

    public FlyoutHost Host { get; }

    /// <summary>The presentation settings the host follows (the Appearance pane's choices).</summary>
    public TimeTug.Presentation.Settings.MemoryPresentationSettingsStore Store { get; }

    /// <summary>The flyout window while it is open.</summary>
    public FlyoutWindow? Window => Host.IsOpen ? Factory.Last?.Window : null;

    public DateTimeOffset Now => Clock.GetUtcNow();

    public static FlyoutRig Create(SelfTestOptions options, CancellationToken cancellation, ITrayAnchor? anchor = null) =>
        new(TugRig.Create(options, 1, cancellation), ownsTug: true, anchor);

    /// <summary>A flyout on a tug rig that someone else owns (the accessibility scenario).</summary>
    public static FlyoutRig Over(TugRig tug, TimeTug.Presentation.Settings.MemoryPresentationSettingsStore? store = null) => new(tug, ownsTug: false, null, store);

    public bool Check(bool condition, string name) => Tug.Check(condition, name);

    /// <summary>Opens the flyout with Toggle and waits for it to be laid out; returns its window (null when it did not open).</summary>
    public async Task<FlyoutWindow?> OpenAsync()
    {
        if (!Host.Toggle() || Window is not { } window)
        {
            return null;
        }

        try
        {
            // A window that never reports loaded must not hang the scenario; the checks that follow say what is wrong with it.
            await SelfTestRunner.WaitForFrameAsync(window, CancellationToken.None, 700).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            Run.Info("the flyout window did not report loaded within 10 seconds");
        }

        return window;
    }

    // ---- states -------------------------------------------------------------------------------------------------

    public AppState State(IReadOnlyList<AgendaItem> agenda, IReadOnlyList<SourceProblem>? problems = null) =>
        Scenes.Connected(Now) with { Agenda = agenda, Problems = problems ?? [], LeadTime = TimeSpan.FromSeconds(60) };

    /// <summary>A timed item that starts <paramref name="startMinutes"/> from the standing clock (negative: earlier).</summary>
    public AgendaItem Item(
        string id,
        string title,
        int startMinutes,
        int lengthMinutes,
        AgendaPhase phase,
        string? calendar = "Work",
        string? color = "#2E7AF5",
        string? join = null,
        int others = 0,
        bool next = false) =>
        new(id, title, Now.AddMinutes(startMinutes), Now.AddMinutes(startMinutes + lengthMinutes), false, phase, calendar, color,
            join is null ? null : new Uri(join), others, false, null)
        {
            IsNext = next,
        };

    public static AgendaItem AllDay(string id, string title, string? calendar = "Personal", string? color = "#34A853") =>
        new(id, title, new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero), true,
            AgendaPhase.Upcoming, calendar, color, null, 0, false, null)
        {
            StartDate = new DateOnly(2026, 10, 16),
            EndDate = new DateOnly(2026, 10, 17),
        };

    /// <summary>The day the screenshots show: a holiday, a finished stand-up, the meeting in progress, the next meeting, a later one and a merged one.</summary>
    public IReadOnlyList<AgendaItem> Day() =>
    [
        AllDay("holiday", "Company holiday"),
        Item("standup", "Team stand-up", -80, 30, AgendaPhase.Past, color: "#9334E6", calendar: "Team"),
        Item("review", "Design review", -20, 50, AgendaPhase.Current, join: "https://zoom.us/j/123456789", others: 5),
        Item("planning", "Quarterly planning with the platform and growth teams", 70, 60, AgendaPhase.Upcoming, color: "#E8710A", join: "https://meet.google.com/abc-defg-hij", others: 8, next: true),
        Item("learn", "Lunch and learn", 160, 60, AgendaPhase.Upcoming, calendar: "Team", color: "#9334E6"),
        Item("winddown", "Wind down", 370, 60, AgendaPhase.Upcoming, color: "#D93025", join: "https://example.test/room") with
        {
            IsMerged = true,
            MergedEventCount = 3,
            MergeKind = "inference",
            MergeEngineName = "Apple Intelligence",
        },
    ];

    /// <summary>A day with a merged card (calendars and members) and a pair of look-alike events that can be merged by hand.</summary>
    public IReadOnlyList<AgendaItem> MergeDay()
    {
        var work = new MergeCalendar("g/work", "Work", "me@example.com");
        var team = new MergeCalendar("g/team", "Team Sync", "me@example.com");
        MergeMember Member(string id, int start, int length, MergeCalendar calendar) =>
            new(id, Now.AddMinutes(start), Now.AddMinutes(start + length), new ValueList<MergeCalendar>([calendar]));
        return
        [
            Item("review", "Design review", -10, 50, AgendaPhase.Current, join: "https://zoom.us/j/123456789"),
            Item("budget", "Budget sync", 60, 40, AgendaPhase.Upcoming, color: "#0B8043", next: true) with
            {
                IsMerged = true,
                MergedEventCount = 2,
                MergeKind = "userConfirmed",
                Members = [Member("budget sync|1|2", 60, 30, work), Member("budget sync|1|3", 60, 40, team)],
            },
            Item("offsite", "Offsite prep", 120, 45, AgendaPhase.Upcoming, color: "#E8710A") with
            {
                Candidates = [new MergeCandidate("travel", "Travel planning", Now.AddMinutes(135), Now.AddMinutes(150), false, "Team Sync")],
            },
            Item("travel", "Travel planning", 135, 15, AgendaPhase.Upcoming, calendar: "Team Sync", color: "#0B8043") with
            {
                Candidates = [new MergeCandidate("offsite", "Offsite prep", Now.AddMinutes(120), Now.AddMinutes(165), false, "Work")],
            },
        ];
    }

    /// <summary>A long day: enough cards that the list has to scroll.</summary>
    public IReadOnlyList<AgendaItem> LongDay() =>
    [
        .. Day(),
        .. Enumerable.Range(0, 8).Select(i => Item("more-" + i, "Meeting " + (i + 1), 430 + (i * 40), 30, AgendaPhase.Upcoming, calendar: "Work")),
    ];

    public void Dispose()
    {
        Host.Dispose();
        Presence.Dispose();
        Model.Dispose();
        if (_ownsTug)
        {
            Tug.Dispose();
        }
    }
}

/// <summary>A tray anchor that always answers the same rectangle.</summary>
internal sealed class FixedAnchor(PixelRect rect) : ITrayAnchor
{
    public PixelRect Rect { get; } = rect;

    public AnchorRect? TryGetAnchor() => new(Rect, AnchorSource.Icon);

    public AnchorRect? Peek() => TryGetAnchor();

    public void Refresh()
    {
    }

    public void Invalidate()
    {
    }

    public event Action? Changed
    {
        add { }
        remove { }
    }
}

/// <summary>The real monitors, read once.</summary>
internal sealed class StaticMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitorService
{
    public IReadOnlyList<MonitorInfo> Monitors { get; } = monitors;

    public MonitorInfo Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors[0];

    public event Action? Changed
    {
        add { }
        remove { }
    }
}
#endif
