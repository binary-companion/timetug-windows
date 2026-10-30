using TimeTug.Platform.OsState;
using TimeTug.Presentation.Engine;

namespace TimeTug.Platform.Tests.OsState;

internal sealed class FakeMessageSource : IWindowMessageSource
{
    public nint Handle => 0;

    public event Action<WindowMessage>? MessageReceived;

    public int Subscribers => MessageReceived?.GetInvocationList().Length ?? 0;

    public void Raise(uint id, nuint wParam = 0, nint lParam = 0, string? text = null) =>
        MessageReceived?.Invoke(new WindowMessage(id, wParam, lParam, text));
}

internal sealed class FakeNetwork : INetworkAvailability
{
    public event Action<bool>? AvailabilityChanged;

    public void Raise(bool available) => AvailabilityChanged?.Invoke(available);
}

/// <summary>An engine client that records system events; everything else is not needed here.</summary>
internal sealed class RecordingEngineClient : IEngineClient
{
    private readonly List<string> _events = [];

    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public bool Fail { get; set; }

    /// <summary>When set, the engine reports a cancellation instead of failing.</summary>
    public bool Cancel { get; set; }

    /// <summary>What <see cref="State"/> returns; a test that needs the state stream sets it.</summary>
    public IObservable<AppState>? StateSource { get; set; }

    public IObservable<AppState> State => StateSource ?? throw new NotSupportedException();

    public IObservable<TakeoverRequest> TakeoversDue => throw new NotSupportedException();

    public IObservable<string> TakeoversWithdrawn => throw new NotSupportedException();

    public IObservable<EngineDiagnosticInfo> Diagnostics => throw new NotSupportedException();

    public ValueTask MergeEventsAsync(string eventId, string otherEventId, string? commandId = null) => throw new NotSupportedException();

    public ValueTask UnmergeEventAsync(string eventId, string? commandId = null) => throw new NotSupportedException();

    public ValueTask SeparateMembersAsync(string eventId, IReadOnlyList<string> memberIds, string? commandId = null) => throw new NotSupportedException();

    public ValueTask SystemEventAsync(string kind, string? timeZone = null)
    {
        if (Cancel)
        {
            throw new OperationCanceledException();
        }

        if (Fail)
        {
            throw new InvalidOperationException("engine gone");
        }

        lock (_events)
        {
            _events.Add(timeZone is null ? kind : kind + ":" + timeZone);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync() => throw new NotSupportedException();

    public ValueTask AddAccountAsync(string kindId) => throw new NotSupportedException();

    public ValueTask RemoveAccountAsync(string connectionId) => throw new NotSupportedException();

    public ValueTask ReconnectAccountAsync(string connectionId) => throw new NotSupportedException();

    public ValueTask CancelAuthorizationAsync() => throw new NotSupportedException();

    public ValueTask TakeoverPresentedAsync(string eventId, bool onScreen) => throw new NotSupportedException();

    public ValueTask TakeoverClosedAsync(string eventId) => throw new NotSupportedException();

    public ValueTask JoinAsync(string eventId) => throw new NotSupportedException();

    public ValueTask SnoozeAsync(string eventId, TimeSpan duration) => throw new NotSupportedException();

    public ValueTask DismissAsync(string eventId) => throw new NotSupportedException();

    public ValueTask TestTugAsync(string title, string calendarTitle) => throw new NotSupportedException();

    public ValueTask SetEnabledAsync(bool enabled) => throw new NotSupportedException();

    public ValueTask ApplySettingsAsync(TimeTug.Presentation.Settings.EngineSettingsPatch patch, string? commandId = null) => throw new NotSupportedException();

    public ValueTask ForgetCorrectionsAsync() => throw new NotSupportedException();
}

internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string because = "condition", int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Timed out waiting for " + because);
            }

            await Task.Delay(10);
        }
    }
}
