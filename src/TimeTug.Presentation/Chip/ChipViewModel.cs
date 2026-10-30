using System.Globalization;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Chip;

/// <summary>
/// What the countdown chip shows: the next (or current) timed meeting and how long until it starts, or nothing. The engine decides which
/// meetings exist, which one is next and what phase each is in; this class applies the person's display preference (<see cref="ChipMode"/> and
/// how many minutes ahead) to that with the clock, and words the result with the same rules as the flyout's rows (<see cref="FlyoutText"/>).
/// It is display arithmetic only: no scheduling, no rule about which meetings count.
/// <para>
/// The chip's meeting is the first timed meeting that is running (the engine's phase, or its start has passed), else the engine's next
/// meeting. All-day events never drive it. It is visible when the mode allows (Always: whenever there is such a meeting; Before meetings: when
/// it is running or starts within the chosen minutes) and the person has not hidden it until the next meeting.
/// </para>
/// <para>
/// It listens to the engine's state, the minute ticker and the settings itself and raises <see cref="Changed"/> only when the plan differs, from
/// whichever thread caused it. A clock set back or a time-zone change needs nothing special: the plan is recomputed from the engine's latest
/// state and the clock on every tick, and an announcement is never repeated (see <see cref="AnnouncementMinutes"/>).
/// </para>
/// </summary>
public sealed class ChipViewModel : IDisposable
{
    /// <summary>How many minutes before the start a screen reader is told, besides "now". Each is said once per meeting.</summary>
    public static IReadOnlyList<int> AnnouncementMinutes { get; } = [30, 15, 5, 1];

    private const string Category = "chip";

    /// <summary>The start is announced only if it is noticed within this long of it, so a meeting that began long ago is not announced when the app opens.</summary>
    private static readonly TimeSpan NowAnnouncementGrace = TimeSpan.FromMinutes(1);

    private readonly IPresentationSettingsStore _settings;
    private readonly TimeProvider _time;
    private readonly ILocalizer _text;
    private readonly CultureInfo _culture;
    private readonly FlyoutText _words;
    private readonly IDiagnostics? _diagnostics;
    private readonly object _gate = new();
    private readonly HashSet<(string EventId, int Band)> _announced = [];
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _ticker;
    private AppState? _state;
    private string? _hiddenEventId;
    private ChipPlan _plan = ChipPlan.Hidden;
    private bool _disposed;

    public ChipViewModel(
        IEngineClient engine,
        IPresentationSettingsStore settings,
        TimeProvider time,
        IMinuteTicker ticker,
        ILocalizer text,
        CultureInfo? culture = null,
        IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(ticker);
        ArgumentNullException.ThrowIfNull(text);
        _settings = settings;
        _time = time;
        _text = text;
        _culture = culture ?? CultureInfo.CurrentCulture;
        _words = new FlyoutText(text, _culture);
        _diagnostics = diagnostics;
        _plan = Build();
        _stateSubscription = engine.State.Subscribe(new StateObserver(OnState));
        settings.Changed += OnSettingsChanged;
        _ticker = ticker.Start(Refresh);
    }

    /// <summary>Raised when what the chip shows differs from before (a new announcement counts; the plan keeps its announcement until the next change); the window layer posts it to the UI thread and must not call back synchronously.</summary>
    public event Action? Changed;

    /// <summary>What the chip shows now.</summary>
    public ChipPlan Plan
    {
        get
        {
            lock (_gate)
            {
                return _plan;
            }
        }
    }

    /// <summary>
    /// "Hide until the next meeting": the chip goes away and comes back when a different meeting becomes the one it shows. Remembered for the
    /// run only. Does nothing when the chip is hidden.
    /// </summary>
    public void HideUntilNextMeeting()
    {
        lock (_gate)
        {
            if (_disposed || _plan.EventId is not { } id)
            {
                return;
            }

            _hiddenEventId = id;
        }

        Refresh();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stateSubscription.Dispose();
        _settings.Changed -= OnSettingsChanged;
        _ticker.Dispose();
    }

    private void OnState(AppState state)
    {
        lock (_gate)
        {
            _state = state;
        }

        Refresh();
    }

    private void OnSettingsChanged() => Refresh();

    private void Refresh()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var next = Build();
            if (next == _plan)
            {
                return;
            }

            // A plan that differs from the current one only in lacking the announcement is the same news: the current plan keeps its announcement
            // until something the window shows changes, so a consumer that has not looked yet (it runs on another thread) still finds it.
            if (next.Announcement is null && next == _plan with { Announcement = null })
            {
                return;
            }

            var visibilityChanged = next.Visible != _plan.Visible;
            _plan = next;
            if (visibilityChanged)
            {
                _diagnostics?.Record("info", Category, next.Visible ? "the chip became visible" : "the chip became hidden");
            }
        }

        Changed?.Invoke();
    }

    private ChipPlan Build()
    {
        var chosen = _settings.Load();
        if (chosen.ChipMode == ChipMode.Off || _state is not { } state)
        {
            return ChipPlan.Hidden;
        }

        var now = _time.GetUtcNow();
        var zone = _time.LocalTimeZone;
        var agenda = state.Agenda;
        PruneAnnounced(agenda);

        var live = agenda.Where(i => !i.IsAllDay && i.Phase != AgendaPhase.Past && i.End > now).ToList();
        var item = live.FirstOrDefault(i => IsRunning(i, now)) ?? live.FirstOrDefault(i => i.IsNext) ?? live.MinBy(i => i.Start);
        if (item is null)
        {
            return ChipPlan.Hidden;
        }

        var running = IsRunning(item, now);
        var untilStart = item.Start - now;
        if (chosen.ChipMode == ChipMode.BeforeMeetings && !running && untilStart > TimeSpan.FromMinutes(chosen.ChipLeadMinutes))
        {
            return ChipPlan.Hidden;
        }

        if (string.Equals(_hiddenEventId, item.EventId, StringComparison.Ordinal))
        {
            return ChipPlan.Hidden;
        }

        var minutes = running ? 0 : CeilingMinutes(untilStart);
        var countdown = running
            ? Format("Chip_Countdown_Running", _words.Clock(item.End, zone))
            : Format("Chip_Countdown_Upcoming", _words.Duration(TimeSpan.FromMinutes(minutes)));
        var automation = running
            ? Format("Chip_Automation_Running", item.Title, _words.Clock(item.End, zone))
            : Format("Chip_Automation_Upcoming", item.Title, _words.SpokenDuration(TimeSpan.FromMinutes(minutes)));
        return new ChipPlan(
            true,
            item.Title + _text.GetString("Flyout_MetaSeparator") + countdown,
            automation,
            item.CalendarColorHex,
            item.JoinUrl is not null,
            Announce(item, running, minutes, now))
        {
            Title = item.Title,
            Countdown = countdown,
            EventId = item.EventId,
        };
    }

    /// <summary>The engine's phase says it is in progress, or its start has passed (the engine's next state will say so too).</summary>
    private static bool IsRunning(AgendaItem item, DateTimeOffset now) => item.Phase == AgendaPhase.Current || item.Start <= now;

    /// <summary>Whole minutes to go, rounded up, never below 1: "in 1 min" until the last second, never "in 0 min".</summary>
    private static int CeilingMinutes(TimeSpan untilStart) => (int)Math.Max(1, Math.Ceiling(untilStart.TotalSeconds / 60.0));

    /// <summary>
    /// The sentence to say for the band the meeting is in (the smallest of 30, 15, 5 and 1 minute that the time to go fits in, or 0 for the start),
    /// the first time it is in it. Saying a band also marks every wider band as said, so a clock set back, or a state that jumps, never repeats
    /// one; a meeting first seen already inside a band is announced with its real minutes, and one that began long ago is not announced at all.
    /// </summary>
    private string? Announce(AgendaItem item, bool running, int minutes, DateTimeOffset now)
    {
        var band = running ? 0 : AnnouncementMinutes.Order().Cast<int?>().FirstOrDefault(m => minutes <= m);
        if (band is not { } found || _announced.Contains((item.EventId, found)))
        {
            return null;
        }

        foreach (var wider in AnnouncementMinutes.Append(0).Where(m => m >= found))
        {
            _announced.Add((item.EventId, wider));
        }

        if (running)
        {
            return now - item.Start <= NowAnnouncementGrace ? Format("Chip_Announce_Now", item.Title) : null;
        }

        return Format("Chip_Announce_Minutes", item.Title, _words.SpokenDuration(TimeSpan.FromMinutes(minutes)));
    }

    /// <summary>Forgets what was said about meetings that are no longer on the agenda, so the set stays as small as the day.</summary>
    private void PruneAnnounced(IReadOnlyList<AgendaItem> agenda)
    {
        if (_announced.Count > 0)
        {
            _announced.RemoveWhere(a => agenda.All(i => !string.Equals(i.EventId, a.EventId, StringComparison.Ordinal)));
        }
    }

    private string Format(string key, params object[] arguments) => string.Format(_culture, _text.GetString(key), arguments);

    private sealed class StateObserver(Action<AppState> onNext) : IObserver<AppState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(AppState value) => onNext(value);
    }
}
