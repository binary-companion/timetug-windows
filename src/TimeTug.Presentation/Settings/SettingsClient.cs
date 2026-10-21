using System.Globalization;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// Sends settings patches to the engine one at a time and reports how each came out. The engine answers a patch it takes with a newer
/// state, and one it refuses with a diagnostic that carries the id of the command; a patch the engine never answers is given up after
/// <c>answerTimeout</c>. Taking edits one at a time is what keeps two quick edits to the same list (the tug calendars) from losing the first:
/// the second edit runs against the state that already includes the first.
/// </summary>
public sealed class SettingsClient : ISettingsClient, IDisposable
{
    /// <summary>How long a patch waits for the engine's answer by default.</summary>
    public static readonly TimeSpan DefaultAnswerTimeout = TimeSpan.FromSeconds(5);

    private readonly IEngineClient _engine;
    private readonly TimeProvider _time;
    private readonly TimeSpan _answerTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _diagnosticSubscription;
    private AppState? _latest;
    private InFlight? _inFlight;
    private int _counter;
    private int _disposed;

    public SettingsClient(IEngineClient engine, TimeProvider? time = null, TimeSpan? answerTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _time = time ?? TimeProvider.System;
        _answerTimeout = answerTimeout ?? DefaultAnswerTimeout;
        _stateSubscription = engine.State.Subscribe(new ActionObserver<AppState>(OnState));
        _diagnosticSubscription = engine.Diagnostics.Subscribe(new ActionObserver<EngineDiagnosticInfo>(OnDiagnostic));
    }

    public async ValueTask<SettingsResult> ApplyAsync(Func<EngineSettingsPatch, EngineSettingsPatch> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
            EngineSettingsView current;
            lock (_sync)
            {
                current = _latest?.Settings ?? EngineSettingsView.Defaults;
            }

            var patch = edit(new EngineSettingsPatch { Current = current }) with { Current = current };
            if (patch.IsEmpty || patch.ChangesNothing)
            {
                return SettingsResult.Applied;
            }

            var inFlight = new InFlight("settings-" + Interlocked.Increment(ref _counter).ToString(CultureInfo.InvariantCulture));
            lock (_sync)
            {
                inFlight.Baseline = _latest?.Revision ?? 0;
                _inFlight = inFlight;
            }

            try
            {
                await _engine.ApplySettingsAsync(patch, inFlight.Id).ConfigureAwait(false);
                return await WaitForAnswerAsync(inFlight).ConfigureAwait(false);
            }
            finally
            {
                lock (_sync)
                {
                    _inFlight = null;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask ForgetLearnedCorrectionsAsync() => _engine.ForgetCorrectionsAsync();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _stateSubscription.Dispose();
        _diagnosticSubscription.Dispose();
        lock (_sync)
        {
            _inFlight?.Answer.TrySetResult(SettingsResult.Rejected("The app was closing."));
        }
    }

    private async Task<SettingsResult> WaitForAnswerAsync(InFlight inFlight)
    {
        using var stopWaiting = new CancellationTokenSource();
        var timeout = Task.Delay(_answerTimeout, _time, stopWaiting.Token);
        var first = await Task.WhenAny(inFlight.Answer.Task, timeout).ConfigureAwait(false);
        if (first == inFlight.Answer.Task)
        {
            await stopWaiting.CancelAsync().ConfigureAwait(false);
            return await inFlight.Answer.Task.ConfigureAwait(false);
        }

        return SettingsResult.Rejected("The engine did not answer in time.");
    }

    /// <summary>A newer state is the engine's answer that it took the patch.</summary>
    private void OnState(AppState state)
    {
        lock (_sync)
        {
            _latest = state;
            if (_inFlight is { } inFlight && state.Revision > inFlight.Baseline)
            {
                inFlight.Answer.TrySetResult(SettingsResult.Applied);
            }
        }
    }

    /// <summary>A diagnostic that carries the id of the patch in flight is the engine refusing it.</summary>
    private void OnDiagnostic(EngineDiagnosticInfo info)
    {
        lock (_sync)
        {
            if (_inFlight is { } inFlight && info.CommandId == inFlight.Id)
            {
                inFlight.Answer.TrySetResult(SettingsResult.Rejected(info.Message));
            }
        }
    }

    private sealed class InFlight(string id)
    {
        public string Id { get; } = id;

        /// <summary>The revision of the newest state when the patch went out; the answer is a state newer than this.</summary>
        public ulong Baseline { get; set; }

        public TaskCompletionSource<SettingsResult> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
