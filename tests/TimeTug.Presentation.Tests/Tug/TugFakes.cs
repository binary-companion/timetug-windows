using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tug;

namespace TimeTug.Presentation.Tests.Tug;

/// <summary>An engine client that records the takeover commands the controller sends, with their arguments.</summary>
internal sealed class TugEngineClient : IEngineClient
{
    public List<string> Commands { get; } = [];

    public List<bool> PresentedOnScreen { get; } = [];

    public List<TimeSpan> Snoozes { get; } = [];

    /// <summary>Raised after each takeover command so a test can model the engine.</summary>
    public Action<string>? OnCommand { get; set; }

    public IEnumerable<string> Terminal => Commands.Where(c => !c.StartsWith("presented:", StringComparison.Ordinal));

    public IObservable<AppState> State => throw new NotSupportedException();

    public IObservable<TakeoverRequest> TakeoversDue => throw new NotSupportedException();

    public IObservable<string> TakeoversWithdrawn => throw new NotSupportedException();

    public IObservable<EngineDiagnosticInfo> Diagnostics => throw new NotSupportedException();

    public ValueTask TakeoverPresentedAsync(string eventId, bool onScreen)
    {
        PresentedOnScreen.Add(onScreen);
        return Record("presented:" + eventId);
    }

    public ValueTask TakeoverClosedAsync(string eventId) => Record("closed:" + eventId);

    public ValueTask JoinAsync(string eventId) => Record("join:" + eventId);

    public ValueTask SnoozeAsync(string eventId, TimeSpan duration)
    {
        Snoozes.Add(duration);
        return Record("snooze:" + eventId);
    }

    public ValueTask DismissAsync(string eventId) => Record("dismiss:" + eventId);

    public ValueTask StartAsync() => throw new NotSupportedException();

    public ValueTask AddAccountAsync(string kindId) => throw new NotSupportedException();

    public ValueTask RemoveAccountAsync(string connectionId) => throw new NotSupportedException();

    public ValueTask ReconnectAccountAsync(string connectionId) => throw new NotSupportedException();

    public ValueTask CancelAuthorizationAsync() => throw new NotSupportedException();

    public ValueTask TestTugAsync(string title, string calendarTitle) => throw new NotSupportedException();

    public ValueTask SetEnabledAsync(bool enabled) => throw new NotSupportedException();

    public ValueTask ApplySettingsAsync(TimeTug.Presentation.Settings.EngineSettingsPatch patch, string? commandId = null) => throw new NotSupportedException();

    public ValueTask ForgetCorrectionsAsync() => throw new NotSupportedException();

    public ValueTask MergeEventsAsync(string eventId, string otherEventId, string? commandId = null) => throw new NotSupportedException();

    public ValueTask UnmergeEventAsync(string eventId, string? commandId = null) => throw new NotSupportedException();

    public ValueTask SeparateMembersAsync(string eventId, IReadOnlyList<string> memberIds, string? commandId = null) => throw new NotSupportedException();

    public ValueTask SystemEventAsync(string kind, string? timeZone = null) => throw new NotSupportedException();

    private ValueTask Record(string command)
    {
        Commands.Add(command);
        OnCommand?.Invoke(command);
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeSession : ISessionState
{
    public bool IsLocked { get; set; }

    public event Action? Locked;

    public event Action? Unlocked;

    public void Lock()
    {
        IsLocked = true;
        Locked?.Invoke();
    }

    public void Unlock()
    {
        IsLocked = false;
        Unlocked?.Invoke();
    }
}

internal sealed class FakeNotifications : INotificationState
{
    public bool CanShowFullScreen { get; set; } = true;

    public bool ToastsAllowed { get; set; } = true;

    public event Action? FullScreenEnded;

    public bool Watching { get; private set; }

    public void SetWatchingForFullScreenEnd(bool watching) => Watching = watching;

    public void EndFullScreen()
    {
        CanShowFullScreen = true;
        FullScreenEnded?.Invoke();
    }
}

internal sealed class FakeMonitors : IMonitorService
{
    public FakeMonitors()
    {
        Set(new MonitorInfo("m1", 0, 0, 1920, 1080, true), new MonitorInfo("m2", 1920, 0, 1920, 1080, false));
    }

    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = [];

    public MonitorInfo Primary => Monitors.First(m => m.IsPrimary);

    public event Action? Changed;

    public void Set(params MonitorInfo[] monitors) => Monitors = monitors;

    public void Raise() => Changed?.Invoke();
}

internal sealed class FakeToasts : IToastService
{
    public bool Accept { get; set; } = true;

    public bool Throw { get; set; }

    public int Shown { get; private set; }

    public ValueTask<bool> ShowAsync(TakeoverRequest request)
    {
        Shown++;
        return Throw ? throw new InvalidOperationException("toast failure") : ValueTask.FromResult(Accept);
    }
}

internal sealed class FakeLauncher : IUrlLauncher
{
    public bool Succeeds { get; set; } = true;

    public List<string> Opened { get; } = [];

    public ValueTask<bool> LaunchAsync(string url)
    {
        Opened.Add(url);
        return ValueTask.FromResult(Succeeds);
    }
}

internal sealed class RecordingTugDiagnostics : IDiagnostics
{
    public List<(string Level, string Category, string Message)> Entries { get; } = [];

    public void Record(string level, string category, string message) => Entries.Add((level, category, message));

    public bool Has(string text) => Entries.Any(e => e.Message.Contains(text, StringComparison.OrdinalIgnoreCase));
}

internal static class Requests
{
    public static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A 30 minute meeting starting one minute after <see cref="Noon"/>.</summary>
    public static TakeoverRequest Meeting(string id = "e1", string? joinUrl = "https://meet.example.test/abc") => new(
        id,
        "Design review",
        Noon.AddMinutes(1),
        Noon.AddMinutes(31),
        false,
        false,
        joinUrl,
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)],
        "Work",
        3);

    public static AppState StateWith(params TakeoverRequest[] meetings) => new(
        [],
        [],
        meetings.Select(m => new EventSummary(m.EventId, m.Title, m.Start, m.End, m.IsAllDay, m.JoinUrl is not null)).ToList(),
        null,
        null,
        false,
        true);
}
