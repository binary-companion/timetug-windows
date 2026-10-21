#if SELFTEST
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;

namespace TimeTug.App.SelfTest;

/// <summary>
/// A scripted stand-in for the engine in self-test scenarios: the scenario pushes states, the commands the app sends are
/// recorded. Adding the kind named in <see cref="ShowFormFor"/> opens the real credential form through the form service.
/// </summary>
internal sealed class ScriptedEngineClient(AccountFormService forms) : IEngineClient, IDisposable
{
    private readonly Channel<AppState> _state = new(replay: true);
    private readonly Channel<EngineDiagnosticInfo> _diagnostics = new(replay: false);
    private readonly Channel<TakeoverRequest> _due = new(replay: false);
    private readonly Channel<string> _withdrawn = new(replay: false);
    private CancellationTokenSource? _form;

    public List<string> Sent { get; } = [];

    public string? ShowFormFor { get; set; }

    public IObservable<AppState> State => _state;

    public IObservable<TakeoverRequest> TakeoversDue => _due;

    public IObservable<string> TakeoversWithdrawn => _withdrawn;

    public IObservable<EngineDiagnosticInfo> Diagnostics => _diagnostics;

    public void Push(AppState state) => _state.Emit(state);

    public ValueTask StartAsync() => ValueTask.CompletedTask;

    public ValueTask AddAccountAsync(string kindId)
    {
        Sent.Add("add:" + kindId);
        if (kindId == ShowFormFor)
        {
            Push(Scenes.Empty(new AccountActivity(true, null, "inApp")));
            _form = new CancellationTokenSource();
            _ = ShowFormAsync(_form.Token);
        }
        else
        {
            Push(Scenes.Empty(new AccountActivity(true, null, "browser")));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CancelAuthorizationAsync()
    {
        Sent.Add("cancel");
        _form?.Cancel();
        Push(Scenes.Empty(new AccountActivity(false, null, "browser")));
        return ValueTask.CompletedTask;
    }

    public ValueTask TestTugAsync(string title, string calendarTitle)
    {
        Sent.Add("testTug");
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAccountAsync(string connectionId) => Record("remove:" + connectionId);

    public ValueTask ReconnectAccountAsync(string connectionId) => Record("reconnect:" + connectionId);

    public ValueTask TakeoverPresentedAsync(string eventId, bool onScreen) => Record("presented:" + eventId);

    public ValueTask TakeoverClosedAsync(string eventId) => Record("closed:" + eventId);

    public ValueTask JoinAsync(string eventId) => Record("join:" + eventId);

    public ValueTask SnoozeAsync(string eventId, TimeSpan duration) => Record($"snooze:{eventId}:{(int)duration.TotalSeconds}");

    public ValueTask DismissAsync(string eventId) => Record("dismiss:" + eventId);

    public ValueTask SetEnabledAsync(bool enabled) => Record("enabled:" + enabled);

    public ValueTask ApplySettingsAsync(TimeTug.Presentation.Settings.EngineSettingsPatch patch, string? commandId = null) => Record("settings");

    public ValueTask ForgetCorrectionsAsync() => Record("forget");

    public ValueTask SystemEventAsync(string kind, string? timeZone = null) => Record("system:" + kind);

    public void Dispose() => _form?.Dispose();

    private async Task ShowFormAsync(CancellationToken cancellation)
    {
        var request = new AccountFormRequest(
            "Sign in to iCloud",
            new AccountFormHelp("Use an app-specific password from your Apple Account, not your Apple Account password."),
            [new AccountFormField("username", "Apple Account", false), new AccountFormField("password", "App-specific password", true)],
            new Dictionary<string, string>(),
            null);
        try
        {
            await forms.ShowAsync(request, cancellation);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the scenario: the dialog closed.
        }
    }

    private ValueTask Record(string command)
    {
        Sent.Add(command);
        return ValueTask.CompletedTask;
    }

    private sealed class Channel<T>(bool replay) : IObservable<T>
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

            return new Subscription(() => _observers.Remove(observer));
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
    }

    private sealed class Subscription(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}

/// <summary>The states the welcome scenario walks through.</summary>
internal static class Scenes
{
    // Microsoft is left unconfigured on purpose: CI builds embed no OAuth client ids, so this is what the owner sees there too.
    public static readonly IReadOnlyList<AccountKindInfo> Kinds =
    [
        new("google", "Google", true, "oauth"),
        new("microsoft", "Microsoft", false, "oauth"),
        new("icloud", "iCloud", true, "password"),
        new("caldav", "Other (CalDAV)", true, "password"),
    ];

    public static AppState Empty(AccountActivity? activity = null) =>
        new([], Kinds, [], null, null, false, true, activity ?? new AccountActivity(false, null, "browser"));

    public static AppState Connected(DateTimeOffset now) =>
        new(
            [new AccountInfo("conn-1", "google", "you@example.com", "ok", 3)],
            Kinds,
            [],
            new NextMeetingInfo("Design review", now.AddMinutes(150), true),
            null,
            false,
            true,
            new AccountActivity(false, null, "browser"));
}
#endif
