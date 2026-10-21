using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Settings;

public enum CalendarSectionIcon
{
    /// <summary>The operating system's own calendar source.</summary>
    Calendar,

    /// <summary>A connected account: the provider's own icon (<see cref="CalendarSectionView.KindId"/>).</summary>
    Provider,

    /// <summary>A source that is no longer connected.</summary>
    Unknown,
}

public sealed record CalendarRowView(
    string Key, string Title, string? ColorHex, bool IsShown, bool Tugs, bool TugEnabled, string ShowAutomationName, string TugAutomationName,
    string ShowToolTip, string TugToolTip, string HighlightId);

public sealed record CalendarGroupView(string SourceId, string AccountName, string Label, bool AllTug, string AllAutomationName, IReadOnlyList<CalendarRowView> Rows);

public sealed record CalendarSectionView(
    string SourceId, string Title, string Subtitle, string SubtitleText, CalendarSectionIcon Icon, string KindId, IReadOnlyList<CalendarGroupView> Groups);

/// <summary>
/// The Calendars pane (the Mac's <c>CalendarsPane</c>): per account group an "All" Tug switch, per calendar a "show in list" checkbox and a
/// Tug switch (disabled while the calendar is hidden), the subscribed and system calendars apart, and the Options (skip all-day events,
/// duplicate finding with on-device intelligence, forgetting what it learned). Every value is the engine's last published setting; an edit
/// is one <c>settings.apply</c> patch whose key lists are built by <see cref="CalendarKeyEdits"/>. A refused patch makes the page rebuild
/// from the engine's values and shows a message. Nothing here decides what tugs.
/// </summary>
public sealed class CalendarsViewModel : ObservableObject, IDisposable
{
    private readonly IEngineClient _engine;
    private readonly ISettingsClient _settings;
    private readonly IUiDispatcher _ui;
    private readonly ILocalizer _text;
    private readonly bool _inferenceSupported;
    private readonly IDisposable _subscription;
    private EngineSettingsView _view = EngineSettingsView.Defaults;
    private IReadOnlyList<CalendarGroup> _groups = [];
    private IReadOnlyList<CalendarEntry> _system = [];
    private InferenceAvailability _inference = InferenceAvailability.Disabled;
    private string? _inferenceReason;
    private string? _inferenceEngine;
    private string _signature = string.Empty;
    private bool _disposed;

    /// <param name="inferenceSupported">
    /// False when this build of the app registers no on-device text generator (Windows 1.0). The engine only reports why inference cannot run
    /// once the switch is on, so without this the switch would be offered and then take itself away; false keeps it off and disabled with the
    /// engine's "no model" words from the start and persists nothing from it.
    /// </param>
    public CalendarsViewModel(IEngineClient engine, ISettingsClient settings, IUiDispatcher ui, ILocalizer text, bool inferenceSupported = true)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(text);
        _engine = engine;
        _settings = settings;
        _ui = ui;
        _text = text;
        _inferenceSupported = inferenceSupported;
        Sections = [];
        SystemRows = [];
        _subscription = engine.State.Subscribe(new ActionObserver<AppState>(state => OnUi(() => Apply(state))));
    }

    /// <summary>The search catalog's ids (a calendar's is the prefix plus its key).</summary>
    public static class Ids
    {
        public const string SkipAllDay = "skip-all-day";
        public const string Inference = "dedup-inference";
        public const string CalendarPrefix = "calendar:";
    }

    public string IntroText => _text.GetString("Calendars_Intro");

    public string ColumnTugCalendars => _text.GetString("Calendars_ColumnTugCalendars");

    public string ColumnTug => _text.GetString("Calendars_ColumnTug");

    /// <summary>The sections in the Mac's order, each with its account groups and their calendars.</summary>
    public IReadOnlyList<CalendarSectionView> Sections
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The subscribed and system-style calendars shown in the disclosure.</summary>
    public IReadOnlyList<CalendarRowView> SystemRows
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SystemCount));
            }
        }
    }

    public int SystemCount => SystemRows.Count;

    public string SystemTitle => _text.GetString("Calendars_SystemTitle");

    /// <summary>Whether the disclosure is open. It opens by itself when a calendar the person just hid would otherwise vanish into it.</summary>
    public bool SystemExpanded
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>True when the engine lists no calendar at all.</summary>
    public bool IsEmpty => Sections.Count == 0 && SystemRows.Count == 0;

    public string EmptyText => _text.GetString("Calendars_Empty");

    public string EmptyLinkText => _text.GetString("Calendars_EmptyLink");

    public string OptionsTitle => _text.GetString("Calendars_Options");

    public bool SkipAllDay => _view.SkipAllDayEvents;

    public string BetaText => _text.GetString("Calendars_Beta");

    public string BetaAutomationName => _text.GetString("Calendars_BetaAutomation");

    private InferenceAvailability Effective => _inferenceSupported ? _inference : InferenceAvailability.NoEngine;

    /// <summary>Whether the switch can be used: the engine does not say on-device intelligence cannot run here.</summary>
    public bool InferenceSwitchEnabled => Effective.CanRunHere();

    /// <summary>The switch shows what actually runs: off while it cannot, whatever the engine has stored.</summary>
    public bool InferenceIsOn => InferenceSwitchEnabled && _view.InferenceEnabled;

    /// <summary>The engine's status in words (the Mac's <c>InferenceStatusText</c>), or null when there is nothing to say.</summary>
    public string? InferenceStatusText => Effective switch
    {
        InferenceAvailability.NoEngine => _text.GetString("Calendars_InferenceNoEngine"),
        InferenceAvailability.Unavailable => _text.Format("Calendars_InferenceUnavailable", _inferenceReason ?? string.Empty),
        InferenceAvailability.NotOnDevice => _text.GetString("Calendars_InferenceNotOnDevice"),
        InferenceAvailability.Active => _text.Format("Calendars_InferenceActive", _inferenceEngine ?? string.Empty),
        _ => null,
    };

    public string InferenceExplanation => _text.GetString("Calendars_InferenceExplanation");

    public string ForgetText => _text.GetString("Calendars_Forget");

    /// <summary>The "Forget learned corrections" button shows while the feature is on and can run (the Mac shows it while the switch is on).</summary>
    public bool ShowForget => InferenceIsOn;

    /// <summary>True while the confirmation question is open.</summary>
    public bool ConfirmForget
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Why the last change was not saved, or null.</summary>
    public string? Message
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => Message is not null;

    /// <summary>The Tug switch of one calendar. A calendar the engine does not list is ignored.</summary>
    public Task SetCalendarTugAsync(string key, bool on) =>
        Lists(key) ? ApplyAsync(p => CalendarKeyEdits.SetTug(p, [key], on)) : Task.CompletedTask;

    /// <summary>The "show in list" checkbox of one calendar.</summary>
    public async Task SetCalendarShownAsync(string key, bool shown)
    {
        var entry = Find(key);
        if (entry is null)
        {
            return;
        }

        var opensSystem = !shown && entry.IsShown && entry.Kind is { Length: > 0 } kind && kind != "standard";
        if (await ApplyAsync(p => CalendarKeyEdits.SetShown(p, key, shown)) && opensSystem)
        {
            SystemExpanded = true;
        }
    }

    /// <summary>The "All" switch of an account group: every calendar of the group at once, in one patch.</summary>
    public Task SetGroupTugAsync(string sourceId, string accountName, bool on)
    {
        var group = _groups.FirstOrDefault(g => g.SourceId == sourceId && g.AccountName == accountName);
        return group is null ? Task.CompletedTask : ApplyAsync(p => CalendarKeyEdits.SetTug(p, group.Calendars.Select(c => c.Key), on));
    }

    public Task SetSkipAllDayAsync(bool value) => ApplyAsync(p => p with { SkipAllDayEvents = value });

    /// <summary>The inference switch. While the switch is disabled nothing is sent, so nothing is persisted from it.</summary>
    public Task SetInferenceAsync(bool value) => InferenceSwitchEnabled ? ApplyAsync(p => p with { InferenceEnabled = value }) : Task.CompletedTask;

    /// <summary>Opens the confirmation question; nothing is forgotten yet.</summary>
    public void BeginForget() => ConfirmForget = true;

    public void CancelForget() => ConfirmForget = false;

    /// <summary>Forgets what the duplicate finder learned (<c>dedup.forget</c>), once the question was confirmed.</summary>
    public async Task ForgetLearnedCorrectionsAsync()
    {
        if (!ConfirmForget)
        {
            return;
        }

        ConfirmForget = false;
        await _settings.ForgetLearnedCorrectionsAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
    }

    private bool Lists(string key) => Find(key) is not null;

    private CalendarEntry? Find(string key) =>
        _groups.SelectMany(g => g.Calendars).Concat(_system).FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));

    /// <summary>Sends one patch. On a refusal the page is told to rebuild from the engine's values and a message is shown. True when the engine took it.</summary>
    private async Task<bool> ApplyAsync(Func<EngineSettingsPatch, EngineSettingsPatch> edit)
    {
        var result = await _settings.ApplyAsync(edit).ConfigureAwait(false);
        var applied = result.IsApplied;
        OnUi(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (applied)
            {
                Message = null;
                return;
            }

            Message = _text.GetString("Settings_ChangeNotSaved");
            Rebuild(force: true);
            OnPropertyChanged(nameof(SkipAllDay));
            OnPropertyChanged(nameof(InferenceIsOn));
        });
        return applied;
    }

    private void Apply(AppState state)
    {
        if (_disposed)
        {
            return;
        }

        var before = (_view.SkipAllDayEvents, _view.InferenceEnabled, Effective, _inferenceReason, _inferenceEngine);
        _view = state.Settings;
        _groups = state.CalendarGroups;
        _system = state.SystemCalendars;
        _inference = state.Inference;
        _inferenceReason = state.InferenceReason;
        _inferenceEngine = state.InferenceEngineName;
        Rebuild(force: false);
        if (before.SkipAllDayEvents != _view.SkipAllDayEvents)
        {
            OnPropertyChanged(nameof(SkipAllDay));
        }

        if (before != (_view.SkipAllDayEvents, _view.InferenceEnabled, Effective, _inferenceReason, _inferenceEngine))
        {
            OnPropertyChanged(nameof(InferenceIsOn));
            OnPropertyChanged(nameof(InferenceSwitchEnabled));
            OnPropertyChanged(nameof(InferenceStatusText));
            OnPropertyChanged(nameof(ShowForget));
        }
    }

    /// <summary>Builds the rows from the engine's last state; raises the lists only when something they show changed (or when forced).</summary>
    private void Rebuild(bool force)
    {
        var sections = BuildSections();
        var system = _system.Select(Row).ToList();
        var signature = string.Join('\n', sections.SelectMany(s => s.Groups.Select(g => $"{s.SourceId}|{s.Title}|{s.Subtitle}|{g.AccountName}|{g.AllTug}|" + string.Join(',', g.Rows.Select(r => $"{r.Key}:{r.Title}:{r.ColorHex}:{r.IsShown}:{r.Tugs}")))))
            + "\n--\n" + string.Join(',', system.Select(r => $"{r.Key}:{r.Title}:{r.ColorHex}:{r.IsShown}:{r.Tugs}"));
        if (!force && signature == _signature)
        {
            return;
        }

        _signature = signature;
        Sections = sections;
        SystemRows = system;
        if (force)
        {
            OnPropertyChanged(nameof(Sections));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private List<CalendarSectionView> BuildSections()
    {
        var sections = new List<CalendarSectionView>();
        foreach (var bySource in _groups.GroupBy(g => g.SourceId))
        {
            var first = bySource.First();
            var named = first.ShowsGroupName;
            var groups = bySource.Select(g => new CalendarGroupView(
                g.SourceId,
                g.AccountName,
                named ? g.AccountName : _text.GetString("Calendars_AllCalendars"),
                g.Calendars.Count > 0 && g.Calendars.All(c => c.Tugs),
                _text.Format("Calendars_AllTugAutomation", g.AccountName),
                [.. g.Calendars.Select(Row)])).ToList();
            var subtitle = first.Subtitle ?? string.Empty;
            sections.Add(new CalendarSectionView(
                first.SourceId,
                first.Title,
                subtitle,
                "· " + subtitle,
                first.Origin switch
                {
                    CalendarOrigin.System => CalendarSectionIcon.Calendar,
                    CalendarOrigin.Account => CalendarSectionIcon.Provider,
                    _ => CalendarSectionIcon.Unknown,
                },
                first.KindId,
                groups));
        }

        return sections;
    }

    private CalendarRowView Row(CalendarEntry calendar) => new(
        calendar.Key,
        calendar.Title,
        calendar.ColorHex,
        calendar.IsShown,
        calendar.Tugs,
        TugEnabled: calendar.IsShown,
        _text.Format("Calendars_ShowAutomation", calendar.Title),
        _text.Format("Calendars_TugAutomation", calendar.Title),
        _text.GetString(calendar.IsShown ? "Calendars_HideToolTip" : "Calendars_ShowToolTip"),
        calendar.IsShown ? string.Empty : _text.GetString("Calendars_TugNeedsShownToolTip"),
        Ids.CalendarPrefix + calendar.Key);

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
}
