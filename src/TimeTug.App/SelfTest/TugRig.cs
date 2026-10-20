#if SELFTEST
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TimeTug.App.Tug;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest;

/// <summary>
/// What the fake-based tug scenarios share: a <see cref="TugController"/> over the fakes of <c>FakeOsState</c> and a scripted engine
/// client, drawing real tug windows through <see cref="TugWindowHost"/>, on two monitors unless asked otherwise. A scenario asks for a
/// request to be shown, answers it the way a person would, and checks what the engine was told.
/// </summary>
internal sealed class TugRig : IDisposable
{
    public const string Link = "https://meet.example.test/selftest-room";

    public static readonly IReadOnlyList<TimeSpan> Snoozes = [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(600)];

    private readonly CancellationToken _cancellation;

    // Closing the last window of a WinUI app ends it. The real app keeps its own windows alive; this stands in for them.
    private readonly Window _anchor = new();
    private readonly AccountFormService _forms;

    private TugRig(ScenarioRun run, int monitorCount, CancellationToken cancellation)
    {
        Run = run;
        _cancellation = cancellation;
        var queue = DispatcherQueue.GetForCurrentThread();
        Text = new ResourceLoaderLocalizer();
        _forms = new AccountFormService(queue, Text);
        Engine = new ScriptedEngineClient(_forms);
        (Main, Second) = FakeMonitors.Layout();
        if (monitorCount > 1)
        {
            Monitors.Set(Main, Second);
        }
        else
        {
            Monitors.Set(Main);
        }

        Controller = new TugController(Engine, Session, Notifications, Monitors, Toasts, Launcher, Diagnostics, Time);
        Host = new TugWindowHost(Controller, new DispatcherQueueDispatcher(queue), Settings, Text, Time, new SecondTicker(Time));
        Host.Start();
    }

    public ScenarioRun Run { get; }

    public AccountFormService Forms => _forms;

    public TimeProvider Time { get; } = TimeProvider.System;

    public ResourceLoaderLocalizer Text { get; }

    public ScriptedEngineClient Engine { get; }

    public FakeSession Session { get; } = new();

    public FakeNotifications Notifications { get; } = new();

    public FakeMonitors Monitors { get; } = new();

    public FakeToasts Toasts { get; } = new();

    public FakeLauncher Launcher { get; } = new();

    public FakeUiSettings Settings { get; } = new();

    public RecordingDiagnostics Diagnostics { get; } = new();

    public TugController Controller { get; }

    public TugWindowHost Host { get; }

    public MonitorInfo Main { get; }

    public MonitorInfo Second { get; }

    /// <summary>Every command that answers a request (join, snooze, dismiss, closed), in order; the "presented" reports are not among them.</summary>
    public IReadOnlyList<string> Terminal => TerminalCommands.Of(Engine.Sent);

    /// <summary>A rig on two monitors.</summary>
    public static TugRig Create(SelfTestOptions options, CancellationToken cancellation) => Create(options, 2, cancellation);

    public static TugRig Create(SelfTestOptions options, int monitorCount, CancellationToken cancellation) =>
        new(new ScenarioRun(options), monitorCount, cancellation);

    public bool Check(bool condition, string name) => Run.Check(condition, name);

    public async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 6000)
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

    public Task Settle(int milliseconds = 1200) => Task.Delay(milliseconds, _cancellation);

    /// <summary>A takeover request that starts <paramref name="startsIn"/> from now (negative: it has started).</summary>
    public TakeoverRequest Request(string id, string title, TimeSpan startsIn, string? link = Link, int others = 5, string? calendar = "Work")
    {
        var start = Time.GetUtcNow() + startsIn;
        return new TakeoverRequest(id, title, start, start.AddMinutes(30), false, startsIn <= TimeSpan.Zero, link, Snoozes, calendar, others);
    }

    /// <summary>Hands the request to the controller as the engine would and waits for a window on every monitor.</summary>
    public async Task<bool> PresentAsync(TakeoverRequest request)
    {
        await Controller.OnTakeoverDueAsync(request);
        return await WaitFor(() => Host.WindowCount == Monitors.Monitors.Count && Host.Primary is not null);
    }

    public Task<bool> WaitHiddenAsync() => WaitFor(() => Host.WindowCount == 0);

    /// <summary>
    /// Another window of this app comes to the front, so the tug loses the keyboard the way it does when Windows gives it to
    /// something else. True when the tug noticed and shows "Click anywhere to respond".
    /// </summary>
    public async Task<bool> TakeKeyboardAwayAsync()
    {
        _anchor.Activate();
        return await WaitFor(() => Host.Primary?.ViewModel.ShowClickToRespond == true, 3000);
    }

    /// <summary>Puts the stand-in window out of sight again.</summary>
    public void HideAnchor() => _anchor.AppWindow.Hide();

    /// <summary>With <c>--hold</c>, keeps the tug on screen for a real screen capture.</summary>
    public async Task HoldAsync()
    {
        if (Run.Options.HoldSeconds > 0)
        {
            await File.WriteAllTextAsync(Path.Combine(Run.Folder, "holding.txt"), "holding", _cancellation);
            await Task.Delay(TimeSpan.FromSeconds(Run.Options.HoldSeconds), _cancellation);
        }
    }

    /// <summary>The checks every tug scenario ends with: nothing was recorded as an error, then the result.</summary>
    public SelfTestResult Finish()
    {
        Run.Check(Diagnostics.Entries.All(e => e.Level != "error"), "nothing was recorded as an error: " + string.Join("; ", Diagnostics.Entries.Where(e => e.Level == "error").Select(e => e.Message)));
        return Run.Finish();
    }

    public void Dispose()
    {
        Host.Dispose();
        Controller.Dispose();
        Engine.Dispose();
        _forms.Dispose();
        GC.KeepAlive(_anchor);
    }
}

/// <summary>Picks the commands that end a request out of what the engine was sent.</summary>
internal static class TerminalCommands
{
    public static IReadOnlyList<string> Of(IEnumerable<string> sent) =>
        [.. sent.Where(s => !s.StartsWith("presented:", StringComparison.Ordinal))];
}
#endif
