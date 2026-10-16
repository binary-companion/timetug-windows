using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Welcome;

/// <summary>
/// The accounts half of the welcome window, reusable by the Settings pane later: what can be connected, what is, and one
/// sign-in at a time with its progress, cancel and failure. It only mirrors the engine's state and sends commands.
/// </summary>
public sealed class AccountsViewModel : ObservableObject, IDisposable
{
    private readonly IEngineClient _engine;
    private readonly IUiDispatcher _ui;
    private readonly ILocalizer _text;
    private readonly TimeProvider _clock;
    private readonly List<IDisposable> _subscriptions = [];
    private AppState? _state;
    private Attempt? _attempt;

    public AccountsViewModel(IEngineClient engine, IUiDispatcher ui, ILocalizer text, TimeProvider? clock = null)
    {
        _engine = engine;
        _ui = ui;
        _text = text;
        _clock = clock ?? TimeProvider.System;
        CancelCommand = new AsyncRelayCommand(CancelAsync);
        TestTugCommand = new AsyncRelayCommand(TestTugAsync);
        _subscriptions.Add(engine.State.Subscribe(new ActionObserver<AppState>(state => OnUi(() => Apply(state)))));
        _subscriptions.Add(engine.Diagnostics.Subscribe(new ActionObserver<EngineDiagnosticInfo>(info => OnUi(() => Diagnosed(info)))));
    }

    /// <summary>Raised on the UI thread when the account being added shows up in the engine's state.</summary>
    public event EventHandler? Connected;

    public ObservableCollection<ProviderItem> Providers { get; } = [];

    public ObservableCollection<AccountRow> Accounts { get; } = [];

    public IAsyncRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand TestTugCommand { get; }

    /// <summary>True while a sign-in runs (ours, or any other account work the engine reports).</summary>
    public bool IsBusy
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(WaitingText));
            }
        }
    }

    /// <summary>"Waiting for your browser…" or "Signing in…", empty when idle.</summary>
    public string WaitingText => !IsBusy
        ? ""
        : _state?.Activity?.WaitingStyle == "inApp" ? _text.GetString("WaitingInApp") : _text.GetString("WaitingBrowser");

    public string NextMeetingText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    /// <summary>True once the first engine state has arrived.</summary>
    public bool HasState => _state is not null;

    public async Task ConnectAsync(string kindId)
    {
        var provider = Providers.FirstOrDefault(p => p.Id == kindId);
        if (provider is not { IsConfigured: true } || IsBusy)
        {
            return;
        }

        foreach (var other in Providers)
        {
            other.Error = null;
            other.ErrorDetail = null;
        }

        var baseline = (_state?.Accounts ?? []).Select(a => a.ConnectionId).ToHashSet(StringComparer.Ordinal);
        _attempt = new Attempt(kindId, baseline, _state?.Activity?.ErrorMessage);
        Refresh();
        try
        {
            await _engine.AddAccountAsync(kindId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Fail(_attempt, null);
        }
    }

    /// <summary>Stops a sign-in in progress (the browser one or the form one). Shows no error.</summary>
    public async Task CancelAsync()
    {
        if (_attempt is null && _state?.Activity?.IsWorking != true)
        {
            return;
        }

        _attempt = null;
        Refresh();
        await _engine.CancelAuthorizationAsync();
    }

    public Task TestTugAsync() => _engine.TestTugAsync(_text.GetString("TestTugTitle"), _text.GetString("TestTugCalendar")).AsTask();

    /// <summary>Clears every provider's error (used when going back to the list).</summary>
    public void ClearErrors()
    {
        foreach (var provider in Providers)
        {
            provider.Error = null;
            provider.ErrorDetail = null;
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void OnUi(Action action)
    {
        if (_ui.HasThreadAccess)
        {
            action();
        }
        else
        {
            _ui.Post(action);
        }
    }

    private void Apply(AppState state)
    {
        _state = state;
        OnPropertyChanged(nameof(HasState));
        RebuildProviders(state);
        RebuildAccounts(state);
        NextMeetingText = DescribeNextMeeting(state);
        Evaluate(state);
        Refresh();
    }

    private void Evaluate(AppState state)
    {
        if (_attempt is not { } attempt)
        {
            return;
        }

        var activity = state.Activity;
        if (activity?.IsWorking == true)
        {
            attempt.SawWorking = true;
        }

        if (state.Accounts.FirstOrDefault(a => !attempt.Baseline.Contains(a.ConnectionId)) is { } added)
        {
            if (added.Status == "failing")
            {
                Fail(attempt, null);
            }
            else
            {
                _attempt = null;
                Connected?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (activity is { IsWorking: false })
        {
            if (!string.IsNullOrEmpty(activity.ErrorMessage) && (attempt.SawWorking || activity.ErrorMessage != attempt.BaselineError))
            {
                Fail(attempt, activity.ErrorMessage);
            }
            else if (attempt.SawWorking)
            {
                // The engine stopped without an account or a message: the person closed the form or the browser tab.
                _attempt = null;
            }
        }
    }

    private void Diagnosed(EngineDiagnosticInfo info)
    {
        if (_attempt is { } attempt
            && info.Category == "accounts"
            && info.Level is "error" or "warning"
            && info.Message.Contains(attempt.KindId, StringComparison.OrdinalIgnoreCase))
        {
            Fail(attempt, null);
            Refresh();
        }
    }

    private void Fail(Attempt attempt, string? detail)
    {
        if (!ReferenceEquals(_attempt, attempt))
        {
            return;
        }

        _attempt = null;
        if (Providers.FirstOrDefault(p => p.Id == attempt.KindId) is { } provider)
        {
            provider.Error = _text.Format("ConnectFailed", provider.Name);
            provider.ErrorDetail = detail;
        }

        Refresh();
    }

    private void Refresh()
    {
        IsBusy = _attempt is not null || _state?.Activity?.IsWorking == true;
        foreach (var provider in Providers)
        {
            provider.IsEnabled = provider.IsConfigured && !IsBusy;
        }
    }

    private void RebuildProviders(AppState state)
    {
        var kinds = state.AvailableKinds.Where(k => k.Authorization != "system").ToList();
        var same = kinds.Count == Providers.Count
            && kinds.Zip(Providers).All(pair => pair.First.Id == pair.Second.Id && pair.First.IsConfigured == pair.Second.IsConfigured);
        if (same)
        {
            return;
        }

        Providers.Clear();
        foreach (var kind in kinds)
        {
            var name = KindName(kind.Id, kind.DisplayName);
            var opensBrowser = kind.Authorization == "oauth";
            var hint = !kind.IsConfigured
                ? _text.Format("ProviderUnavailable", name)
                : opensBrowser ? _text.GetString("ProviderOpensBrowser") : _text.GetString("ProviderAsksDetails");
            var item = new ProviderItem(kind.Id, name, kind.IsConfigured, opensBrowser, hint, _text.Format("ConnectProviderAutomation", name))
            {
                IsEnabled = kind.IsConfigured && !IsBusy,
            };
            var id = kind.Id;
            item.ConnectCommand = new AsyncRelayCommand(() => ConnectAsync(id));
            Providers.Add(item);
        }
    }

    private void RebuildAccounts(AppState state)
    {
        Accounts.Clear();
        foreach (var account in state.Accounts)
        {
            var count = account.CalendarCount switch
            {
                0 => _text.GetString("CalendarsNone"),
                1 => _text.GetString("CalendarsOne"),
                var n => _text.Format("CalendarsMany", n),
            };
            var summary = _text.Format("AccountSummary", KindName(account.KindId, account.KindId), account.DisplayName, count);
            var problem = account.Status == "ok" ? null : _text.TryGetString("Status_" + account.Status);
            Accounts.Add(new AccountRow(account.ConnectionId, summary, problem));
        }
    }

    private string DescribeNextMeeting(AppState state)
    {
        if (state.NextMeeting is not { } next)
        {
            return _text.GetString("NextNone");
        }

        var zone = _clock.LocalTimeZone;
        var start = TimeZoneInfo.ConvertTime(next.Start, zone);
        var today = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), zone).Date;
        var time = start.ToString("t", CultureInfo.CurrentCulture);
        return start.Date == today
            ? _text.Format("NextToday", next.Title, time)
            : _text.Format("NextLater", next.Title, start.ToString("dddd", CultureInfo.CurrentCulture), time);
    }

    private string KindName(string id, string fallback) => _text.TryGetString("Provider_" + id) ?? fallback;

    private sealed class Attempt(string kindId, HashSet<string> baseline, string? baselineError)
    {
        public string KindId { get; } = kindId;

        public HashSet<string> Baseline { get; } = baseline;

        public string? BaselineError { get; } = baselineError;

        public bool SawWorking { get; set; }
    }
}
