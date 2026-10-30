#if SELFTEST
using System.Globalization;
using TimeTug.App.Chip;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest;

/// <summary>A minute ticker a scenario ticks itself, so the countdown moves when the scenario moves its clock.</summary>
internal sealed class ManualMinuteTicker : IMinuteTicker
{
    private Action? _tick;

    public IDisposable Start(Action tick)
    {
        _tick = tick;
        return new Stop(this);
    }

    public void Tick() => _tick?.Invoke();

    private sealed class Stop(ManualMinuteTicker owner) : IDisposable
    {
        public void Dispose() => owner._tick = null;
    }
}

/// <summary>A poll ticker a scenario ticks itself (the real one fires every two seconds).</summary>
internal sealed class ManualPollTicker : IPollTicker
{
    private Action? _tick;

    public int Started { get; private set; }

    public bool Running => _tick is not null;

    public IDisposable Start(TimeSpan interval, Action tick)
    {
        Started++;
        _tick = tick;
        return new Stop(this);
    }

    public void Tick() => _tick?.Invoke();

    private sealed class Stop(ManualPollTicker owner) : IDisposable
    {
        public void Dispose() => owner._tick = null;
    }
}

/// <summary>
/// What the chip scenarios share: the flyout rig (scripted engine, standing clock — Friday 16 October 2026, 10:20 UTC —, real monitors, the real flyout
/// host and window, a stand-in for the tray icon near the bottom-right corner) plus the real <see cref="ChipViewModel"/>, <see cref="ChipHost"/> and
/// <see cref="ChipWindow"/> over it. The minute and the poll ticks are the scenario's own; the actions are recorded (the flyout's toggle is the real one).
/// </summary>
internal sealed class ChipRig : IDisposable
{
    private readonly bool _ownsFlyout;

    private ChipRig(FlyoutRig flyout, bool ownsFlyout)
    {
        Flyout = flyout;
        _ownsFlyout = ownsFlyout;
        var tug = flyout.Tug;
        Ui = new DispatcherQueueDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        Model = new ChipViewModel(tug.Engine, flyout.Store, flyout.Clock, Minutes, tug.Text, CultureInfo.GetCultureInfo("en-US"), tug.Diagnostics);
        Windows = new ChipWindowFactory(tug.Settings, flyout.Store, tug.Text, Ui);
        Actions = new ChipActions(
            flyout.Host.Toggle,
            eventId =>
            {
                Joined.Add(eventId);
                return ValueTask.FromResult(true);
            },
            () => SettingsOpened++);
        Host = new ChipHost(Model, Windows, Actions, flyout.Presence, tug.Notifications, Poll, flyout.Monitors, flyout.Anchor, flyout.Store, Ui, tug.Diagnostics);
    }

    public FlyoutRig Flyout { get; }

    public TugRig Tug => Flyout.Tug;

    public ScenarioRun Run => Flyout.Run;

    public IUiDispatcher Ui { get; }

    public ManualMinuteTicker Minutes { get; } = new();

    public ManualPollTicker Poll { get; } = new();

    public ChipViewModel Model { get; }

    public ChipWindowFactory Windows { get; }

    public ChipActions Actions { get; }

    public ChipHost Host { get; }

    public List<string> Joined { get; } = [];

    public int SettingsOpened { get; private set; }

    /// <summary>The store the host, the flyout and the window follow.</summary>
    public MemoryPresentationSettingsStore Store => Flyout.Store;

    /// <summary>The chip's window while the chip exists.</summary>
    public ChipWindow? Window => Host.HasWindow ? Windows.Last : null;

    public static ChipRig Create(SelfTestOptions options, CancellationToken cancellation) => new(FlyoutRig.Create(options, cancellation), ownsFlyout: true);

    /// <summary>A chip over a flyout rig that someone else owns (the accessibility scenario).</summary>
    public static ChipRig Over(FlyoutRig flyout) => new(flyout, ownsFlyout: false);

    public bool Check(bool condition, string name) => Run.Check(condition, name);

    /// <summary>Sets the chip's mode (and lead minutes) in the store, as the Appearance pane does.</summary>
    public void Use(ChipMode mode, int lead = 15) => Store.Save(Store.Load() with { ChipMode = mode, ChipLeadMinutes = lead });

    /// <summary>The state with one timed meeting that starts <paramref name="startMinutes"/> from the standing clock (negative: it began).</summary>
    public AppState StateWith(int startMinutes, int lengthMinutes = 30, string title = "Planning review", string? join = null, string id = "chip-1", string? color = "#2E7AF5") =>
        Flyout.State(
        [
            Flyout.Item(
                id, title, startMinutes, lengthMinutes, startMinutes <= 0 ? AgendaPhase.Current : AgendaPhase.Upcoming, color: color, join: join, next: startMinutes > 0),
        ]);

    /// <summary>Moves the standing clock and ticks the minute ticker, as a minute passing does.</summary>
    public void Advance(TimeSpan by)
    {
        Flyout.Clock.Advance(by);
        Minutes.Tick();
    }

    /// <summary>Waits until the window is shown (and has had a frame).</summary>
    public async Task<ChipWindow?> WaitForWindowAsync(int milliseconds = 6000)
    {
        if (!await Tug.WaitFor(() => Host.IsShown && Windows.Last is not null, milliseconds))
        {
            return null;
        }

        var window = Windows.Last!;
        await Tug.Settle(700);
        return window;
    }

    public void Dispose()
    {
        Host.Dispose();
        Model.Dispose();
        if (_ownsFlyout)
        {
            Flyout.Dispose();
        }
    }
}
#endif
