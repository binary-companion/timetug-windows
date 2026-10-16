using System.Globalization;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Tray;

/// <summary>
/// What the notification-area icon shows and does: the menu rows, the icon state, the tooltip and the left click. It reads the
/// engine's state (the next meeting, whether a takeover is pending, whether the Tug is on) and the tug controller's attention
/// flag; it decides no meeting rule. The one thing it measures is "near": the engine's next takeover is for a meeting that
/// starts within <see cref="SoonWindow"/> (the Mac's menu-bar icon uses the same ten minutes), re-read every minute.
/// <para>
/// <see cref="Changed"/> is raised from whichever thread the engine, the controller or the minute ticker used, and only when what
/// the tray shows differs; the window layer posts to the UI thread and must not block.
/// </para>
/// </summary>
public sealed class TrayViewModel : IDisposable
{
    /// <summary>How far ahead of its start the icon turns colour and the Next row offers Join (the Mac's value).</summary>
    public static readonly TimeSpan SoonWindow = TimeSpan.FromMinutes(10);

    /// <summary>The notification area shows at most 127 characters of tooltip.</summary>
    private const int MaxTooltip = 127;

    private const int MaxMenuTitle = 60;
    private const string Ellipsis = "…";

    private readonly IEngineClient _engine;
    private readonly ITugAttention _attention;
    private readonly IUiSettings _ui;
    private readonly ILocalizer _text;
    private readonly ITrayHost _host;
    private readonly TimeProvider _time;
    private readonly CultureInfo _culture;
    private readonly object _gate = new();
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _ticker;
    private readonly Action _onUiSettingsChanged;
    private readonly Action _onAttentionChanged;
    private AppState? _state;
    private TrayView _last;
    private bool _disposed;
    private int _quit;

    public TrayViewModel(
        IEngineClient engine,
        ITugAttention attention,
        IUiSettings ui,
        ILocalizer text,
        ITrayHost host,
        TimeProvider time,
        IMinuteTicker ticker,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(attention);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(ticker);
        _engine = engine;
        _attention = attention;
        _ui = ui;
        _text = text;
        _host = host;
        _time = time;
        _culture = culture ?? CultureInfo.CurrentCulture;
        _last = Build();
        _onUiSettingsChanged = Refresh;
        _onAttentionChanged = Refresh;
        _stateSubscription = engine.State.Subscribe(new StateObserver(OnState));
        attention.AttentionChanged += _onAttentionChanged;
        ui.Changed += _onUiSettingsChanged;
        _ticker = ticker.Start(Refresh);
    }

    /// <summary>Raised when what the tray shows differs from before.</summary>
    public event Action? Changed;

    /// <summary>What the tray shows now (computed against the current time).</summary>
    public TrayView Current
    {
        get
        {
            lock (_gate)
            {
                return Build();
            }
        }
    }

    /// <summary>Runs the command behind a menu row. Settings and the information row do nothing.</summary>
    public async ValueTask InvokeAsync(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.EnableTug:
                bool? enabled;
                lock (_gate)
                {
                    enabled = _state?.Enabled;
                }

                // The checkmark follows the engine's answer, so nothing changes here until a state arrives.
                if (enabled is { } on)
                {
                    await _engine.SetEnabledAsync(!on);
                }

                break;
            case TrayCommand.TestTug:
                await _host.TestTugAsync();
                break;
            case TrayCommand.Quit:
                if (Interlocked.Exchange(ref _quit, 1) == 0)
                {
                    await _host.QuitAsync();
                }

                break;
        }
    }

    /// <summary>A left click on the icon. Until the flyout exists (slice 2) it opens the welcome window when no account is connected.</summary>
    public void PrimaryClick()
    {
        bool noAccounts;
        lock (_gate)
        {
            noAccounts = _state is { Accounts.Count: 0 };
        }

        if (noAccounts)
        {
            _host.ShowWelcome();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stateSubscription.Dispose();
        _ticker.Dispose();
        _attention.AttentionChanged -= _onAttentionChanged;
        _ui.Changed -= _onUiSettingsChanged;
    }

    private void OnState(AppState state)
    {
        lock (_gate)
        {
            _state = state;
        }

        Refresh();
    }

    private void Refresh()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var view = Build();
            if (view.SameAs(_last))
            {
                return;
            }

            _last = view;
        }

        Changed?.Invoke();
    }

    // ---- building what the tray shows --------------------------------------------------------------------------

    private TrayView Build()
    {
        var now = _time.GetUtcNow();
        var (near, offersJoin) = Nearness(_state, now);
        var attention = _attention.NeedsAttention;
        var icon = new TrayIconView(
            attention ? TrayIconState.Attention : near ? TrayIconState.Soon : TrayIconState.Idle,
            _ui.TaskbarUsesLightTheme);
        return new TrayView(BuildRows(_state, offersJoin), icon, BuildTooltip(_state, now, attention));
    }

    /// <summary>
    /// Whether the engine has a takeover pending for a meeting that starts within the window (or has started), and whether that
    /// meeting is the one the Next row names and has a join link, so the row may say Join.
    /// </summary>
    private static (bool Near, bool OffersJoin) Nearness(AppState? state, DateTimeOffset now)
    {
        if (state is not { NextTakeover: { } takeover })
        {
            return (false, false);
        }

        var scheduled = state.Events.FirstOrDefault(e => e.Id == takeover.EventId);
        var start = scheduled?.Start ?? takeover.FireAt;
        if (start - now > SoonWindow)
        {
            return (false, false);
        }

        var sameMeeting = state.NextMeeting is { } next && scheduled is not null && next.Title == scheduled.Title && next.Start == scheduled.Start;
        return (true, sameMeeting && state.NextMeeting!.HasJoinLink);
    }

    private TrayRow[] BuildRows(AppState? state, bool offersJoin)
    {
        var started = state is not null;
        return
        [
            new TrayRow(TrayCommand.Next, TrayRowKind.Information, NextText(state, offersJoin), false),
            TrayRow.Separator,
            new TrayRow(TrayCommand.EnableTug, TrayRowKind.Toggle, _text.GetString("Tray_EnableTug"), started, state?.Enabled ?? false),
            new TrayRow(TrayCommand.TestTug, TrayRowKind.Command, _text.GetString("Tray_TestTug"), started),
            TrayRow.Separator,
            new TrayRow(TrayCommand.Settings, TrayRowKind.Command, _text.GetString("Tray_Settings"), false),
            TrayRow.Separator,
            new TrayRow(TrayCommand.Quit, TrayRowKind.Command, _text.GetString("Tray_Quit"), true),
        ];
    }

    private string NextText(AppState? state, bool offersJoin)
    {
        if (state is null)
        {
            return _text.GetString("Tray_Starting");
        }

        if (state.NextMeeting is not { } next)
        {
            return _text.GetString("Tray_NoUpcoming");
        }

        var title = Shorten(next.Title, MaxMenuTitle);
        if (offersJoin)
        {
            return string.Format(_culture, _text.GetString("Tray_Join"), title);
        }

        return string.Format(_culture, _text.GetString("Tray_Next"), title, FormatTime(next.Start));
    }

    private string FormatTime(DateTimeOffset start)
    {
        var local = TimeZoneInfo.ConvertTime(start, _time.LocalTimeZone);
        // Newer ICU data writes a narrow no-break space before AM/PM; the menu uses an ordinary space.
        return local.ToString("t", _culture).Replace(' ', ' ').Replace(' ', ' ');
    }

    private string BuildTooltip(AppState? state, DateTimeOffset now, bool attention)
    {
        if (attention)
        {
            return _text.GetString("Tray_Tooltip_Attention");
        }

        var off = state is { Enabled: false };
        if (state?.NextMeeting is not { } next)
        {
            return off ? string.Format(_culture, _text.GetString("Tray_Tooltip_TugOff"), _text.GetString("Tray_Tooltip_Default")) : _text.GetString("Tray_Tooltip_Default");
        }

        string Compose(string title)
        {
            var line = WhenText(title, next.Start - now);
            return off ? string.Format(_culture, _text.GetString("Tray_Tooltip_TugOff"), line) : line;
        }

        var tooltip = Compose(next.Title);
        if (tooltip.Length <= MaxTooltip)
        {
            return tooltip;
        }

        // Shorten only the title, so the time stays readable.
        var title = next.Title;
        var excess = tooltip.Length - MaxTooltip + Ellipsis.Length;
        title = title[..Math.Max(1, title.Length - excess)] + Ellipsis;
        return Compose(title);
    }

    private string WhenText(string title, TimeSpan until)
    {
        if (until <= TimeSpan.Zero)
        {
            return string.Format(_culture, _text.GetString(until > -TimeSpan.FromMinutes(1) ? "Tray_Tooltip_StartsNow" : "Tray_Tooltip_Started"), title);
        }

        var minutes = (int)Math.Ceiling(until.TotalMinutes);
        if (minutes < 60)
        {
            return string.Format(_culture, _text.GetString("Tray_Tooltip_InMinutes"), title, minutes);
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0
            ? string.Format(_culture, _text.GetString("Tray_Tooltip_InHours"), title, hours)
            : string.Format(_culture, _text.GetString("Tray_Tooltip_InHoursMinutes"), title, hours, rest);
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + Ellipsis;

    private sealed class StateObserver(Action<AppState> onNext) : IObserver<AppState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(AppState value) => onNext(value);
    }
}
