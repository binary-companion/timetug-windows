using System.Xml.Linq;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Tests.Welcome;

/// <summary>A scripted IEngineClient: tests push states and read the commands the view model sent.</summary>
internal sealed class FakeEngineClient : IEngineClient
{
    private readonly FakeSubject<AppState> _state = new(replay: true);
    private readonly FakeSubject<EngineDiagnosticInfo> _diagnostics = new(replay: false);

    public List<string> Sent { get; } = [];

    public List<(string Title, string Calendar)> TestTugs { get; } = [];

    public IObservable<AppState> State => _state;

    public IObservable<TakeoverRequest> TakeoversDue => new FakeSubject<TakeoverRequest>(false);

    public IObservable<string> TakeoversWithdrawn => new FakeSubject<string>(false);

    public IObservable<EngineDiagnosticInfo> Diagnostics => _diagnostics;

    public void Push(AppState state) => _state.Emit(state);

    public void Push(EngineDiagnosticInfo info) => _diagnostics.Emit(info);

    public ValueTask StartAsync() => ValueTask.CompletedTask;

    public ValueTask AddAccountAsync(string kindId) => Record("add:" + kindId);

    public ValueTask RemoveAccountAsync(string connectionId) => Record("remove:" + connectionId);

    public ValueTask ReconnectAccountAsync(string connectionId) => Record("reconnect:" + connectionId);

    public ValueTask CancelAuthorizationAsync() => Record("cancel");

    public ValueTask TakeoverPresentedAsync(string eventId, bool onScreen) => Record("presented:" + eventId);

    public ValueTask TakeoverClosedAsync(string eventId) => Record("closed:" + eventId);

    public ValueTask JoinAsync(string eventId) => Record("join:" + eventId);

    public ValueTask SnoozeAsync(string eventId, TimeSpan duration) => Record("snooze:" + eventId);

    public ValueTask DismissAsync(string eventId) => Record("dismiss:" + eventId);

    public ValueTask TestTugAsync(string title, string calendarTitle)
    {
        TestTugs.Add((title, calendarTitle));
        return Record("testTug");
    }

    public ValueTask SetEnabledAsync(bool enabled) => Record("enabled:" + enabled);

    /// <summary>Every settings patch sent, with the command id it carried.</summary>
    public List<(TimeTug.Presentation.Settings.EngineSettingsPatch Patch, string? Id)> Patches { get; } = [];

    /// <summary>Runs after a patch is recorded (a test publishes the engine's answer from here).</summary>
    public Action<TimeTug.Presentation.Settings.EngineSettingsPatch, string?>? OnApplySettings { get; set; }

    public ValueTask ApplySettingsAsync(TimeTug.Presentation.Settings.EngineSettingsPatch patch, string? commandId = null)
    {
        Patches.Add((patch, commandId));
        OnApplySettings?.Invoke(patch, commandId);
        return Record("settings");
    }

    public ValueTask ForgetCorrectionsAsync() => Record("forget");

    public ValueTask SystemEventAsync(string kind, string? timeZone = null) => Record("system:" + kind);

    private ValueTask Record(string command)
    {
        Sent.Add(command);
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeSubject<T>(bool replay) : IObservable<T>
{
    private readonly List<IObserver<T>> _observers = [];
    private T? _latest;
    private bool _has;

    public IDisposable Subscribe(IObserver<T> observer)
    {
        _observers.Add(observer);
        if (replay && _has)
        {
            observer.OnNext(_latest!);
        }

        return new Unsubscribe(() => _observers.Remove(observer));
    }

    public void Emit(T value)
    {
        _latest = value;
        _has = true;
        foreach (var observer in _observers.ToArray())
        {
            observer.OnNext(value);
        }
    }

    private sealed class Unsubscribe(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool HasThreadAccess => true;

    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}

/// <summary>Reads the real Strings/en-US/Resources.resw, so a test fails when a string the view model needs is missing.</summary>
internal sealed class ReswLocalizer : ILocalizer
{
    private readonly Dictionary<string, string> _strings;

    public ReswLocalizer()
    {
        var path = Path.Combine(FindRoot(), "src", "TimeTug.App", "Strings", "en-US", "Resources.resw");
        _strings = XDocument.Load(path).Descendants("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!, StringComparer.Ordinal);
    }

    public string GetString(string key) => _strings.TryGetValue(key, out var value) ? value : throw new KeyNotFoundException("Missing string: " + key);

    public string? TryGetString(string key) => _strings.GetValueOrDefault(key);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TimeTug.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("TimeTug.sln not found above " + AppContext.BaseDirectory);
    }
}

internal static class States
{
    public static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlyList<AccountKindInfo> Kinds =
    [
        new("google", "Google", true, "oauth"),
        new("microsoft", "Microsoft", true, "oauth"),
        new("icloud", "iCloud", true, "password"),
        new("caldav", "Other (CalDAV)", true, "password"),
    ];

    public static AppState Empty(IReadOnlyList<AccountKindInfo>? kinds = null, AccountActivity? activity = null) =>
        new([], kinds ?? Kinds, [], null, null, false, true, activity ?? new AccountActivity(false, null, "browser"));

    public static AppState With(
        IReadOnlyList<AccountInfo> accounts,
        NextMeetingInfo? next = null,
        AccountActivity? activity = null,
        IReadOnlyList<AccountKindInfo>? kinds = null) =>
        new(accounts, kinds ?? Kinds, [], next, null, false, true, activity ?? new AccountActivity(false, null, "browser"));

    public static AccountInfo Google(string status = "ok", int calendars = 3) => new("conn-1", "google", "you@example.com", status, calendars);
}
