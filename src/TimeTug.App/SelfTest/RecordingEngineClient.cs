#if SELFTEST
using System.Collections.Concurrent;
using TimeTug.Presentation.Engine;

namespace TimeTug.App.SelfTest;

/// <summary>
/// Passes everything to the real engine client and writes down the commands that answer a takeover, in the same words as the
/// scripted client (<c>presented:id</c>, <c>join:id</c>, <c>snooze:id:300</c>, <c>dismiss:id</c>, <c>closed:id</c>), and the merge commands (<c>merge:a+b</c>, <c>unmerge:a</c>, <c>separate:a:member</c>), so a scenario that
/// runs the real engine can still say "exactly one terminal command".
/// </summary>
internal sealed class RecordingEngineClient(IEngineClient inner) : IEngineClient
{
    private readonly ConcurrentQueue<string> _sent = new();

    public IReadOnlyList<string> Sent => [.. _sent];

    public IObservable<AppState> State => inner.State;

    public IObservable<TakeoverRequest> TakeoversDue => inner.TakeoversDue;

    public IObservable<string> TakeoversWithdrawn => inner.TakeoversWithdrawn;

    public IObservable<EngineDiagnosticInfo> Diagnostics => inner.Diagnostics;

    public ValueTask StartAsync() => inner.StartAsync();

    public ValueTask AddAccountAsync(string kindId) => inner.AddAccountAsync(kindId);

    public ValueTask RemoveAccountAsync(string connectionId) => inner.RemoveAccountAsync(connectionId);

    public ValueTask ReconnectAccountAsync(string connectionId) => inner.ReconnectAccountAsync(connectionId);

    public ValueTask CancelAuthorizationAsync() => inner.CancelAuthorizationAsync();

    public ValueTask TakeoverPresentedAsync(string eventId, bool onScreen)
    {
        _sent.Enqueue("presented:" + eventId);
        return inner.TakeoverPresentedAsync(eventId, onScreen);
    }

    public ValueTask TakeoverClosedAsync(string eventId)
    {
        _sent.Enqueue("closed:" + eventId);
        return inner.TakeoverClosedAsync(eventId);
    }

    public ValueTask JoinAsync(string eventId)
    {
        _sent.Enqueue("join:" + eventId);
        return inner.JoinAsync(eventId);
    }

    public ValueTask SnoozeAsync(string eventId, TimeSpan duration)
    {
        _sent.Enqueue($"snooze:{eventId}:{(int)duration.TotalSeconds}");
        return inner.SnoozeAsync(eventId, duration);
    }

    public ValueTask DismissAsync(string eventId)
    {
        _sent.Enqueue("dismiss:" + eventId);
        return inner.DismissAsync(eventId);
    }

    public ValueTask TestTugAsync(string title, string calendarTitle) => inner.TestTugAsync(title, calendarTitle);

    public ValueTask SetEnabledAsync(bool enabled) => inner.SetEnabledAsync(enabled);

    public ValueTask ApplySettingsAsync(TimeTug.Presentation.Settings.EngineSettingsPatch patch, string? commandId = null) => inner.ApplySettingsAsync(patch, commandId);

    public ValueTask ForgetCorrectionsAsync() => inner.ForgetCorrectionsAsync();

    public ValueTask MergeEventsAsync(string eventId, string otherEventId, string? commandId = null)
    {
        _sent.Enqueue($"merge:{eventId}+{otherEventId}");
        return inner.MergeEventsAsync(eventId, otherEventId, commandId);
    }

    public ValueTask UnmergeEventAsync(string eventId, string? commandId = null)
    {
        _sent.Enqueue("unmerge:" + eventId);
        return inner.UnmergeEventAsync(eventId, commandId);
    }

    public ValueTask SeparateMembersAsync(string eventId, IReadOnlyList<string> memberIds, string? commandId = null)
    {
        _sent.Enqueue($"separate:{eventId}:{string.Join(",", memberIds)}");
        return inner.SeparateMembersAsync(eventId, memberIds, commandId);
    }

    public ValueTask SystemEventAsync(string kind, string? timeZone = null) => inner.SystemEventAsync(kind, timeZone);
}
#endif
