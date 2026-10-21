using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Welcome;

/// <summary>
/// The accounts half of the welcome window and the Settings window's Accounts pane: what can be connected, what is (with the engine's status in
/// words, signing in again and removing with a question first), and one sign-in at a time with its progress, cancel and failure. It only
/// mirrors the engine's state and sends commands; removing an account is the engine's (it also removes the account's stored credentials).
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

    /// <summary>The search catalog's ids for the Accounts pane (an account's highlight id is the prefix plus its connection id).</summary>
    public static class Ids
    {
        public const string Accounts = "accounts";
        public const string Diagnostics = "diagnostics";
        public const string AccountPrefix = "account:";
    }

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

    /// <summary>The connected accounts as the Settings pane lists them, in the engine's order.</summary>
    public ObservableCollection<ConnectedAccount> ConnectedAccounts { get; } = [];

    /// <summary>True once the engine has spoken and it lists no account (the pane shows its empty row).</summary>
    public bool IsEmpty => HasState && ConnectedAccounts.Count == 0;

    public string IntroText => _text.GetString("Accounts_Intro");

    public string ConnectedTitle => _text.GetString("Accounts_ConnectedTitle");

    public string AddTitle => _text.GetString("Accounts_AddTitle");

    public string EmptyText => _text.GetString("Accounts_Empty");

    public string SignInAgainText => _text.GetString("Accounts_SignInAgain");

    public string RemoveText => _text.GetString("Accounts_Remove");

    public string CancelText => _text.GetString("Accounts_Cancel");

    public string CancelAutomationName => _text.GetString("Accounts_CancelAutomation");

    public string AddButtonText => _text.GetString("Accounts_Add");

    public string UnavailableBadgeText => _text.GetString("Accounts_Unavailable");

    /// <summary>The Mac's warning under the tiles; null when every kind the engine lists can be added.</summary>
    public string? UnavailableNotice
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The engine's own error text when no provider shows it beside itself (for example a failed "Sign in again"); null otherwise.</summary>
    public string? ErrorText
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The account the person asked to remove and has not yet confirmed; null when no question is open.</summary>
    public ConnectedAccount? PendingRemoval
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(RemovalTitle));
            }
        }
    }

    public string RemovalTitle => _text.Format("Accounts_RemoveTitle", PendingRemoval?.Name ?? "");

    public string RemovalBody => _text.GetString("Accounts_RemoveBody");

    public string RemovalConfirmText => _text.GetString("Accounts_RemoveConfirm");

    public string RemovalCancelText => _text.GetString("Accounts_RemoveCancel");

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

    /// <summary>
    /// "Sign in again": asks the engine to run its sign-in for the account. Only an account the pane shows as needing it is accepted (an expired
    /// sign-in or a build failure), and not while another sign-in runs, so a stale click sends nothing.
    /// </summary>
    public async Task ReauthorizeAsync(string connectionId)
    {
        if (IsBusy || ConnectedAccounts.FirstOrDefault(a => a.ConnectionId == connectionId) is not { CanSignInAgain: true })
        {
            return;
        }

        await _engine.ReconnectAccountAsync(connectionId);
    }

    /// <summary>Opens the question "Remove this account?" for a listed account (not while a sign-in runs).</summary>
    public void BeginRemove(string connectionId)
    {
        if (IsBusy || ConnectedAccounts.FirstOrDefault(a => a.ConnectionId == connectionId) is not { } account)
        {
            return;
        }

        PendingRemoval = account;
    }

    public void CancelRemove() => PendingRemoval = null;

    /// <summary>Removes the account the question was about. The list changes when the engine says it did, not before.</summary>
    public async Task ConfirmRemoveAsync()
    {
        if (PendingRemoval is not { } pending)
        {
            return;
        }

        PendingRemoval = null;
        if (ConnectedAccounts.Any(a => a.ConnectionId == pending.ConnectionId))
        {
            await _engine.RemoveAccountAsync(pending.ConnectionId);
        }
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
        RebuildConnected(state);
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

        UnavailableNotice = Providers.Any(p => !p.IsConfigured) ? _text.GetString("Accounts_UnavailableNotice") : null;
        var engineError = _state?.Activity?.ErrorMessage;
        var shown = engineError is null || engineError == _attempt?.BaselineError || Providers.Any(p => p.ErrorDetail == engineError);
        ErrorText = shown ? null : engineError;
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
            var automation = kind.IsConfigured ? _text.Format("ConnectProviderAutomation", name) : _text.Format("Accounts_UnavailableAutomation", name);
            var item = new ProviderItem(kind.Id, name, kind.IsConfigured, opensBrowser, hint, automation)
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

    private void RebuildConnected(AppState state)
    {
        var rows = new List<ConnectedAccount>();
        foreach (var account in state.Accounts)
        {
            var kindName = KindName(account.KindId, state.AvailableKinds.FirstOrDefault(k => k.Id == account.KindId)?.DisplayName ?? account.KindId);
            var needsAttention = account.BuildFailure || account.Status == "authExpired";
            var statusText = account.BuildFailure ? _text.GetString("Status_buildFailure")
                : !account.StatusKnown ? _text.GetString("Status_connecting")
                : account.Status == "ok" ? _text.GetString("Status_ok")
                : _text.TryGetString("Status_" + account.Status) ?? _text.GetString("Status_failing");
            var notice = account.UnreadableRecurrences switch
            {
                <= 0 => null,
                1 => _text.GetString("Accounts_NoticeOne"),
                var n => _text.Format("Accounts_NoticeMany", n),
            };
            rows.Add(new ConnectedAccount(
                account.ConnectionId, account.KindId, kindName, account.DisplayName, statusText, needsAttention, needsAttention, notice,
                _text.Format("Accounts_RowAutomation", kindName, account.DisplayName, statusText),
                _text.Format("Accounts_SignInAgainAutomation", account.DisplayName),
                _text.Format("Accounts_ActionsAutomation", account.DisplayName),
                _text.Format("Accounts_RemoveAutomation", account.DisplayName)));
        }

        // The engine publishes often; the list (and whoever shows it) only changes when an account's row does.
        if (!rows.SequenceEqual(ConnectedAccounts))
        {
            ConnectedAccounts.Clear();
            foreach (var row in rows)
            {
                ConnectedAccounts.Add(row);
            }

            OnPropertyChanged(nameof(ConnectedAccounts));
        }

        OnPropertyChanged(nameof(IsEmpty));
        if (PendingRemoval is { } pending && rows.All(a => a.ConnectionId != pending.ConnectionId))
        {
            PendingRemoval = null;
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
