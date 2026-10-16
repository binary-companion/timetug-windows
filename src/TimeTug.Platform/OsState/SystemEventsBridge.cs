using System.Threading.Channels;
using TimeTug.Engine;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Platform.OsState;

/// <summary>
/// Turns Windows notifications into the engine's system events: power resume to <c>wake</c>, <c>WM_TIMECHANGE</c> to
/// <c>clockChanged</c>, a changed time zone to <c>timeZoneChanged</c> (with the IANA id), the start of the next local
/// day to <c>dayChanged</c>, network access returning to <c>networkReturned</c> and <c>WM_DISPLAYCHANGE</c> to
/// <c>displayChanged</c>. Session lock and unlock are app-local and send nothing.
/// <para>
/// The midnight timer is a substitute for a notification Windows does not have (it is rearmed after each firing and
/// when the clock, time zone or power state changes); it is not takeover scheduling, which belongs to the engine.
/// </para>
/// </summary>
public sealed class SystemEventsBridge : IDisposable
{
    private readonly IEngineClient _engine;
    private readonly IWindowMessageSource _messages;
    private readonly INetworkAvailability _network;
    private readonly TimeProvider _time;
    private readonly IDiagnostics _diagnostics;
    private readonly Func<string?> _readTimeZone;
    private readonly object _gate = new();
    private readonly Channel<(string Kind, string? TimeZone)> _outbox = Channel.CreateUnbounded<(string, string?)>(new UnboundedChannelOptions { SingleReader = true });
    private string? _lastTimeZone;
    private ITimer? _midnight;
    private DateOnly _midnightFor;
    private bool _disposed;

    /// <param name="readTimeZone">The current IANA time zone id, or null when it cannot be determined; defaults to Windows' zone converted to IANA.</param>
    public SystemEventsBridge(
        IEngineClient engine,
        IWindowMessageSource messages,
        INetworkAvailability network,
        TimeProvider time,
        IDiagnostics diagnostics,
        Func<string?>? readTimeZone = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _engine = engine;
        _messages = messages;
        _network = network;
        _time = time;
        _diagnostics = diagnostics;
        _readTimeZone = readTimeZone ?? ReadWindowsTimeZoneAsIana;
        _lastTimeZone = _readTimeZone();
        _ = Task.Run(SendLoopAsync);
        _messages.MessageReceived += OnMessage;
        _network.AvailabilityChanged += OnNetwork;
        ArmMidnight();
    }

    public void Dispose()
    {
        _messages.MessageReceived -= OnMessage;
        _network.AvailabilityChanged -= OnNetwork;
        lock (_gate)
        {
            _disposed = true;
            _midnight?.Dispose();
            _midnight = null;
        }

        _outbox.Writer.TryComplete();
    }

    /// <summary>The IANA id of the zone Windows is set to now (TimeZoneInfo is re-read from the OS), or null when there is none.</summary>
    public static string? ReadWindowsTimeZoneAsIana()
    {
        TimeZoneInfo.ClearCachedData();
        return ToIana(TimeZoneInfo.Local.Id);
    }

    /// <summary>Converts a Windows zone id ("W. Europe Standard Time") to its IANA id; an id that is already IANA is returned unchanged; null when unknown.</summary>
    public static string? ToIana(string zoneId)
    {
        ArgumentNullException.ThrowIfNull(zoneId);
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zoneId, out var iana))
        {
            return iana;
        }

        return zoneId.Contains('/', StringComparison.Ordinal) ? zoneId : null;
    }

    private void OnMessage(WindowMessage message)
    {
        switch (message.Id)
        {
            case WindowMessage.PowerBroadcast when message.WParam == WindowMessage.PowerResumeAutomatic:
                Send(SystemEventKind.Wake);
                ArmMidnight();
                break;
            case WindowMessage.TimeChange:
                Send(SystemEventKind.ClockChanged);
                CheckTimeZone();
                ArmMidnight();
                break;
            case WindowMessage.SettingChange when message.Text is null or "" or "TimeZoneInformation" or "intl":
                CheckTimeZone();
                break;
            case WindowMessage.DisplayChange:
                Send(SystemEventKind.DisplayChanged);
                break;
            default:
                break;
        }
    }

    private void OnNetwork(bool available)
    {
        if (available)
        {
            Send(SystemEventKind.NetworkReturned);
        }
    }

    private void CheckTimeZone()
    {
        var zone = _readTimeZone();
        string? previous;
        lock (_gate)
        {
            previous = _lastTimeZone;
            if (zone is null || zone == previous)
            {
                return;
            }

            _lastTimeZone = zone;
        }

        Send(SystemEventKind.TimeZoneChanged, zone);
        ArmMidnight();
    }

    /// <summary>Arms one timer for the next local midnight, replacing any earlier one.</summary>
    private void ArmMidnight()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _midnight?.Dispose();
            var now = _time.GetLocalNow();
            _midnightFor = DateOnly.FromDateTime(now.Date).AddDays(1);
            _midnight = _time.CreateTimer(OnMidnight, null, UntilMidnight(now, _midnightFor), Timeout.InfiniteTimeSpan);
        }
    }

    private void OnMidnight(object? state)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // A timer can fire a moment early; wait out the remainder rather than announcing the day twice.
            var now = _time.GetLocalNow();
            if (DateOnly.FromDateTime(now.Date) < _midnightFor)
            {
                _midnight?.Change(UntilMidnight(now, _midnightFor), Timeout.InfiniteTimeSpan);
                return;
            }
        }

        Send(SystemEventKind.DayChanged);
        ArmMidnight();
    }

    /// <summary>Time from <paramref name="now"/> to 00:00 local on <paramref name="day"/> (or 01:00 where that hour does not exist), at least one second.</summary>
    private TimeSpan UntilMidnight(DateTimeOffset now, DateOnly day)
    {
        var zone = _time.LocalTimeZone;
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        var target = new DateTimeOffset(local, zone.GetUtcOffset(local));
        var wait = target - now;
        return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
    }

    /// <summary>Queues an event; one sender delivers them in order, so handlers never block or throw and events never swap places.</summary>
    private void Send(string kind, string? timeZone = null) => _outbox.Writer.TryWrite((kind, timeZone));

    private async Task SendLoopAsync()
    {
        await foreach (var (kind, timeZone) in _outbox.Reader.ReadAllAsync())
        {
            try
            {
                await _engine.SystemEventAsync(kind, timeZone);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _diagnostics.Record("error", "system", $"the engine did not accept system event {kind} ({e.GetType().Name})");
            }
        }
    }
}
