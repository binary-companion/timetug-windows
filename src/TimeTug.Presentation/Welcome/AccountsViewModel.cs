using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;
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
    private string? _windowsAccountEmail;
    private bool _windowsAccountRequested;
    private IWindowsAccountSource? _windowsAccounts;
    private string? _explainedError;
    private string? _brokeredReconnect;
    private bool _brokeredReconnectSawWorking;
    private bool _windowsStopping;

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

    /// <summary>
    /// Where the Windows dialog choice is handed to the broker host (set by the app's composition). Without one the brokered cards still add the account,
    /// and the host opens the Windows account dialog.
    /// </summary>
    public IBrokerSignInIntent? BrokerIntents { get; set; }

    /// <summary>The Windows account's email for the card text "Use name@example.com" (set by the app's composition when the broker is available).</summary>
    public IWindowsAccountSource? WindowsAccounts
    {
        get => _windowsAccounts;
        set
        {
            _windowsAccounts = value;
            _windowsAccountRequested = false;
            RequestWindowsAccount();
        }
    }

    /// <summary>The first-run window sets this: the Windows account card is pre-selected (accent and focus) when the engine offers it.</summary>
    public bool PreselectBrokered
    {
        get;
        set
        {
            field = value;
            foreach (var item in Providers)
            {
                item.IsPreselected = value && item.Id == BrokeredKinds.Brokered && item.IsConfigured;
            }
        }
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

    /// <summary>The tag on a connected account that signs in through Windows.</summary>
    public string OnThisPcText => _text.GetString("Accounts_OnThisPc");

    /// <summary>How to stop TimeTug reading a Microsoft account's calendars, which Windows (not TimeTug) holds the consent for.</summary>
    public string WithdrawConsentText => _text.GetString("Accounts_WithdrawConsent");

    /// <summary>True while at least one connected account signs in through Windows (the pane shows <see cref="WithdrawConsentText"/>).</summary>
    public bool ShowWithdrawConsent => ConnectedAccounts.Any(a => a.IsOnThisPc);

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
        : WaitingOnWindows ? _text.GetString("WaitingWindows")
        : _state?.Activity?.WaitingStyle == "inApp" ? _text.GetString("WaitingInApp") : _text.GetString("WaitingBrowser");

    // The engine says "browser" for the brokered kind (its waiting style cannot know better), so the text follows the kind that was asked for.
    private bool WaitingOnWindows => _attempt?.KindId == BrokeredKinds.Brokered || _brokeredReconnect is not null || _windowsStopping;

    public string NextMeetingText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    /// <summary>True once the first engine state has arrived.</summary>
    public bool HasState => _state is not null;

    public Task ConnectAsync(string kindId) => kindId switch
    {
        BrokeredKinds.PickItem => ConnectBrokeredAsync(BrokerIntent.PickAccount),
        BrokeredKinds.Brokered => ConnectBrokeredAsync(BrokerIntent.WindowsAccount),
        _ => StartAsync(kindId, kindId),
    };

    /// <summary>
    /// "Microsoft account on this PC" (the Windows account) or "Another Microsoft account" (the account picker): arms the person's choice for the broker
    /// host, then asks the engine to add the brokered kind. The engine's command carries only the kind, so the intent travels beside it.
    /// </summary>
    public async Task ConnectBrokeredAsync(BrokerIntent intent)
    {
        var itemId = intent == BrokerIntent.PickAccount ? BrokeredKinds.PickItem : BrokeredKinds.Brokered;
        if (Providers.FirstOrDefault(p => p.Id == itemId) is not { IsConfigured: true } || IsBusy)
        {
            return;
        }

        BrokerIntents?.Arm(intent);
        await StartAsync(itemId, BrokeredKinds.Brokered);
    }

    private async Task StartAsync(string providerId, string kindId)
    {
        var provider = Providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is not { IsConfigured: true } || IsBusy)
        {
            return;
        }

        ClearErrors();
        var baseline = (_state?.Accounts ?? []).Select(a => a.ConnectionId).ToHashSet(StringComparer.Ordinal);
        var attempt = _attempt = new Attempt(providerId, kindId, baseline, _state?.Activity?.ErrorMessage);
        Refresh();
        try
        {
            await _engine.AddAccountAsync(kindId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Fail(attempt, null);
        }
    }

    /// <summary>Stops a sign-in in progress (the browser one or the form one). Shows no error.</summary>
    public async Task CancelAsync()
    {
        if (_attempt is null && _state?.Activity?.IsWorking != true)
        {
            return;
        }

        // A Windows sign-in being cancelled is still "Waiting for Windows…" until the engine says it is done.
        _windowsStopping = _attempt?.KindId == BrokeredKinds.Brokered || _brokeredReconnect is not null;
        EndAttempt();
        _brokeredReconnect = null;
        Refresh();
        await _engine.CancelAuthorizationAsync();
    }

    /// <summary>
    /// "Sign in again": asks the engine to run its sign-in for the account. Only an account the pane shows as needing it is accepted (an expired
    /// sign-in or a build failure), and not while another sign-in runs, so a stale click sends nothing.
    /// </summary>
    public async Task ReauthorizeAsync(string connectionId)
    {
        if (IsBusy || ConnectedAccounts.FirstOrDefault(a => a.ConnectionId == connectionId) is not { CanSignInAgain: true } account)
        {
            return;
        }

        if (account.KindId == BrokeredKinds.Brokered)
        {
            // A picker attempt earlier must not decide this one: reconnect always opens the dialog for the account that needs the person.
            BrokerIntents?.Arm(BrokerIntent.WindowsAccount);
            _brokeredReconnect = connectionId;
            _brokeredReconnectSawWorking = false;
        }

        try
        {
            await _engine.ReconnectAccountAsync(connectionId);
        }
        catch
        {
            _brokeredReconnect = null;
            throw;
        }
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
        _explainedError = null;
        foreach (var provider in Providers)
        {
            provider.Error = null;
            provider.ErrorDetail = null;
            provider.OffersBrowserFallback = false;
        }
    }

    /// <summary>
    /// Asks the Windows account source for the account Windows is signed in with and puts it on the card ("Use name@example.com"). Never throws: a broker that
    /// cannot say leaves the card as "Microsoft account on this PC".
    /// </summary>
    public async Task RefreshWindowsAccountAsync()
    {
        if (_windowsAccounts is not { } source)
        {
            return;
        }

        string? email = null;
        try
        {
            email = await source.GetEmailAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Naming the account is a courtesy (and this task is not awaited): whatever goes wrong, the card keeps its plain text.
        }

        OnUi(() =>
        {
            _windowsAccountEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
            if (Providers.FirstOrDefault(p => p.Id == BrokeredKinds.Brokered) is { } card)
            {
                card.Name = BrokeredCardName();
                card.AutomationName = card.IsConfigured ? _text.Format("ConnectProviderAutomation", card.Name) : card.AutomationName;
            }
        });
    }

    private void RequestWindowsAccount()
    {
        if (_windowsAccounts is null || _windowsAccountRequested || !Providers.Any(p => p.Id == BrokeredKinds.Brokered && p.IsConfigured))
        {
            return;
        }

        _windowsAccountRequested = true;
        _ = RefreshWindowsAccountAsync();
    }

    private string BrokeredCardName() =>
        _windowsAccountEmail is { } email ? _text.Format("Provider_microsoft-brokered-named", email) : _text.GetString("Provider_microsoft-brokered");

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
        EvaluateBrokeredReconnect(state);
        Refresh();
    }

    // "Waiting for Windows…" follows a brokered reconnect until the engine is done with it: it worked (the account no longer needs the person) or it stopped.
    private void EvaluateBrokeredReconnect(AppState state)
    {
        if (state.Activity?.IsWorking != true)
        {
            _windowsStopping = false;
        }

        if (_brokeredReconnect is not { } connectionId)
        {
            return;
        }

        if (state.Activity?.IsWorking == true)
        {
            _brokeredReconnectSawWorking = true;
        }
        else if (_brokeredReconnectSawWorking || !string.IsNullOrEmpty(state.Activity?.ErrorMessage)
            || state.Accounts.FirstOrDefault(a => a.ConnectionId == connectionId)?.Status != "authExpired")
        {
            _brokeredReconnect = null;
        }
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
                EndAttempt();
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
                // The engine stopped without an account or a message: the person closed the form, the browser tab or the Windows dialog.
                EndAttempt();
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

        EndAttempt();
        if (Providers.FirstOrDefault(p => p.Id == attempt.ProviderId) is { } provider)
        {
            if (attempt.KindId == BrokeredKinds.Brokered)
            {
                ExplainBrokeredFailure(provider, detail);
            }
            else
            {
                provider.Error = _text.Format("ConnectFailed", provider.Name);
                provider.ErrorDetail = detail;
            }
        }

        Refresh();
    }

    // The Windows dialog could not be used, or the organization has to approve TimeTug, or something else failed: the card says which, and in every case the
    // browser sign-in is one press away (the person may also want another account).
    private void ExplainBrokeredFailure(ProviderItem provider, string? detail)
    {
        if (detail?.Contains(WindowsUnavailableEngineText, StringComparison.OrdinalIgnoreCase) == true)
        {
            provider.Error = _text.GetString("Accounts_WindowsUnavailable");
            provider.ErrorDetail = null;
            _explainedError = detail;
        }
        else
        {
            // The engine's own words ("Sign-in failed: admin approval is required.") sit under the usual message.
            provider.Error = _text.Format("ConnectFailed", provider.Name);
            provider.ErrorDetail = detail;
        }

        if (Providers.FirstOrDefault(p => p.Id == BrokeredKinds.Browser) is { IsConfigured: true })
        {
            provider.BrowserFallbackText = _text.GetString("Accounts_UseBrowserInstead");
            provider.BrowserFallbackAutomationName = _text.GetString("Accounts_UseBrowserInsteadAutomation");
            provider.BrowserFallbackCommand = new AsyncRelayCommand(() => ConnectAsync(BrokeredKinds.Browser));
            provider.OffersBrowserFallback = true;
        }
    }

    // What the library's brokered kind says (via the engine) when the host answers "unavailable": "Sign-in failed: Windows sign-in is not available."
    private const string WindowsUnavailableEngineText = "Windows sign-in is not available";

    /// <summary>Ends the sign-in attempt in progress; a brokered one also puts the Windows account intent back, so a picker choice never outlives its attempt.</summary>
    private void EndAttempt()
    {
        var ended = _attempt;
        _attempt = null;
        if (ended?.KindId == BrokeredKinds.Brokered)
        {
            BrokerIntents?.Arm(BrokerIntent.WindowsAccount);
        }
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
        var shown = engineError is null || engineError == _attempt?.BaselineError || engineError == _explainedError || Providers.Any(p => p.ErrorDetail == engineError);
        ErrorText = shown ? null : engineError;
    }

    private void RebuildProviders(AppState state)
    {
        var kinds = state.AvailableKinds.Where(k => k.Authorization != "system").ToList();
        var brokered = kinds.FirstOrDefault(k => k.Id == BrokeredKinds.Brokered);
        var cards = new List<(AccountKindInfo Kind, string ItemId)>();
        if (brokered is not null)
        {
            // "Microsoft account on this PC" first, then (when it can be used) the account picker, then the rest in the engine's order.
            cards.Add((brokered, brokered.Id));
            if (brokered.IsConfigured)
            {
                cards.Add((brokered, BrokeredKinds.PickItem));
            }
        }

        cards.AddRange(kinds.Where(k => k.Id != BrokeredKinds.Brokered).Select(k => (k, k.Id)));
        var same = cards.Count == Providers.Count
            && cards.Zip(Providers).All(pair => pair.First.ItemId == pair.Second.Id && pair.First.Kind.IsConfigured == pair.Second.IsConfigured);
        if (same)
        {
            return;
        }

        Providers.Clear();
        foreach (var (kind, itemId) in cards)
        {
            var viaWindows = kind.Id == BrokeredKinds.Brokered;
            var name = itemId switch
            {
                BrokeredKinds.Brokered => BrokeredCardName(),
                BrokeredKinds.PickItem => _text.GetString("Provider_microsoft-brokered-pick"),
                BrokeredKinds.Browser when brokered is not null => _text.GetString("Provider_microsoft-browser"),
                _ => KindName(kind.Id, kind.DisplayName),
            };
            var opensBrowser = kind.Authorization == "oauth" && !viaWindows;
            var hint = !kind.IsConfigured
                ? _text.Format("ProviderUnavailable", name)
                : viaWindows ? _text.GetString("Provider_microsoft-brokered-hint")
                : opensBrowser ? _text.GetString("ProviderOpensBrowser") : _text.GetString("ProviderAsksDetails");
            var automation = kind.IsConfigured ? _text.Format("ConnectProviderAutomation", name) : _text.Format("Accounts_UnavailableAutomation", name);
            BrokerIntent? intent = !viaWindows ? null : itemId == BrokeredKinds.PickItem ? BrokerIntent.PickAccount : BrokerIntent.WindowsAccount;
            var item = new ProviderItem(itemId, name, kind.IsConfigured, opensBrowser, hint, automation, intent)
            {
                IsEnabled = kind.IsConfigured && !IsBusy,
                IsPreselected = PreselectBrokered && itemId == BrokeredKinds.Brokered && kind.IsConfigured,
            };
            var id = itemId;
            item.ConnectCommand = new AsyncRelayCommand(() => ConnectAsync(id));
            Providers.Add(item);
        }

        RequestWindowsAccount();
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
            var summary = _text.Format("AccountSummary", RowKindName(state, account.KindId, account.KindId), account.DisplayName, count);
            var problem = account.Status == "ok" ? null : _text.TryGetString("Status_" + account.Status);
            Accounts.Add(new AccountRow(account.ConnectionId, summary, problem));
        }
    }

    private void RebuildConnected(AppState state)
    {
        var rows = new List<ConnectedAccount>();
        foreach (var account in state.Accounts)
        {
            var kindName = RowKindName(state, account.KindId, state.AvailableKinds.FirstOrDefault(k => k.Id == account.KindId)?.DisplayName ?? account.KindId);
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
                _text.Format("Accounts_RowAutomation", kindName, account.DisplayName, statusText)
                    + (account.KindId == BrokeredKinds.Brokered ? ", " + _text.GetString("Accounts_OnThisPc") : string.Empty),
                _text.Format("Accounts_SignInAgainAutomation", account.DisplayName),
                _text.Format("Accounts_ActionsAutomation", account.DisplayName),
                _text.Format("Accounts_RemoveAutomation", account.DisplayName))
            {
                IsOnThisPc = account.KindId == BrokeredKinds.Brokered,
            });
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
        OnPropertyChanged(nameof(ShowWithdrawConsent));
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

    // A connected row names the provider ("Microsoft"); the "On this PC" tag says how it signs in, so the brokered kind is not called "Microsoft account on this PC" there.
    private string RowKindName(AppState state, string id, string fallback) => id == BrokeredKinds.Brokered
        ? state.AvailableKinds.FirstOrDefault(k => k.Id == id)?.DisplayName ?? "Microsoft"
        : KindName(id, fallback);

    private sealed class Attempt(string providerId, string kindId, HashSet<string> baseline, string? baselineError)
    {
        /// <summary>The card the person pressed (shows the failure); for "Another Microsoft account" this is not the engine's kind.</summary>
        public string ProviderId { get; } = providerId;

        /// <summary>The engine's account kind that was added.</summary>
        public string KindId { get; } = kindId;

        public HashSet<string> Baseline { get; } = baseline;

        public string? BaselineError { get; } = baselineError;

        public bool SawWorking { get; set; }
    }
}
