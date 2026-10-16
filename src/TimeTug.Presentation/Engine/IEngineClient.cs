namespace TimeTug.Presentation.Engine;

/// <summary>The app's view of the shared engine: state out, commands in. It never decides a rule.</summary>
public interface IEngineClient
{
    /// <summary>Latest-wins; a new subscriber gets the latest state at once (nothing before the first state).</summary>
    IObservable<AppState> State { get; }

    IObservable<TakeoverRequest> TakeoversDue { get; }

    /// <summary>Event ids whose takeover no longer applies.</summary>
    IObservable<string> TakeoversWithdrawn { get; }

    /// <summary>Engine diagnostics (never calendar content), for features that need to explain a failure.</summary>
    IObservable<EngineDiagnosticInfo> Diagnostics { get; }

    ValueTask StartAsync();

    ValueTask AddAccountAsync(string kindId);

    ValueTask RemoveAccountAsync(string connectionId);

    ValueTask ReconnectAccountAsync(string connectionId);

    ValueTask CancelAuthorizationAsync();

    ValueTask TakeoverPresentedAsync(string eventId, bool onScreen);

    ValueTask TakeoverClosedAsync(string eventId);

    ValueTask JoinAsync(string eventId);

    ValueTask SnoozeAsync(string eventId, TimeSpan duration);

    ValueTask DismissAsync(string eventId);

    ValueTask TestTugAsync(string title, string calendarTitle);

    /// <summary>The Enable Tug switch: a <c>settings.apply</c> patch carrying only <c>takeover.enabled</c>.</summary>
    ValueTask SetEnabledAsync(bool enabled);

    /// <summary><paramref name="kind"/> is one of the <c>TimeTug.Engine.SystemEventKind</c> constants.</summary>
    ValueTask SystemEventAsync(string kind, string? timeZone = null);
}
