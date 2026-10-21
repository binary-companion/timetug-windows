using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// The Tug Rules pane (the Mac's <c>TugRulesPane</c>): the Enable Tug switch with its hero text, the lead time, the two requirements with
/// their warnings and the Test button. Every value is the engine's last published setting; an edit goes out as one <c>settings.apply</c>
/// patch with only that field, and when the engine refuses it or does not answer, the control shows the engine's value again (the
/// property is raised so the page re-reads it) and a message says the change was not saved. Nothing here decides what fires.
/// </summary>
public sealed class TugRulesViewModel : ObservableObject, IDisposable
{
    private readonly IEngineClient _engine;
    private readonly ISettingsClient _settings;
    private readonly IUiDispatcher _ui;
    private readonly ILocalizer _text;
    private readonly IDisposable _subscription;
    private EngineSettingsView _view = EngineSettingsView.Defaults;
    private bool _disposed;

    public TugRulesViewModel(IEngineClient engine, ISettingsClient settings, IUiDispatcher ui, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(text);
        _engine = engine;
        _settings = settings;
        _ui = ui;
        _text = text;
        LeadTimeOptions = Settings.LeadTimeOptions.Make(_view.LeadTime, text);
        _subscription = engine.State.Subscribe(new ActionObserver<AppState>(state => OnUi(() => Apply(state.Settings))));
    }

    /// <summary>The search catalog's ids for the controls (and the two warnings, which a page can mark the same way).</summary>
    public static class Ids
    {
        public const string EnableTug = "enable-tug";
        public const string LeadTime = "lead-time";
        public const string VideoLink = "video-link";
        public const string RequireAttendees = "require-attendees";
        public const string TestTug = "test-takeover";
        public const string LinkWarning = "video-link-warning";
        public const string AttendeesWarning = "require-attendees-warning";
    }

    /// <summary>The Enable Tug switch. While off no tug fires; the agenda is unaffected.</summary>
    public bool Enabled => _view.Enabled;

    /// <summary>The picker's choices: the Mac's list, plus the saved value when it is not in it.</summary>
    public IReadOnlyList<LeadTimeOption> LeadTimeOptions
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public LeadTimeOption SelectedLeadTime => LeadTimeOptions.First(o => o.Minutes == Settings.LeadTimeOptions.MinutesOf(_view.LeadTime));

    public bool RequireConferenceLink => _view.RequireConferenceLink;

    public bool RequireOtherAttendees => _view.RequireOtherAttendees;

    public string HeroTitle => _text.GetString("TugRules_HeroTitle");

    public string HeroSubtitle => _text.GetString(_view.Enabled ? "TugRules_HeroOn" : "TugRules_HeroOff");

    public string LeadTimeCaption => _text.GetString("TugRules_LeadTimeCaption");

    public bool ShowLinkWarning => _view.RequireConferenceLink;

    public bool ShowAttendeesWarning => _view.RequireOtherAttendees;

    public string LinkWarning => _text.GetString("TugRules_WarningNoLink");

    public string AttendeesWarning => _text.GetString("TugRules_WarningNoAttendees");

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

    public Task SetEnabledAsync(bool value) => ApplyAsync(p => p with { Enabled = value });

    public Task SetLeadMinutesAsync(int minutes) => ApplyAsync(p => p with { LeadTime = TimeSpan.FromMinutes(Math.Max(0, minutes)) });

    public Task SetRequireConferenceLinkAsync(bool value) => ApplyAsync(p => p with { RequireConferenceLink = value });

    public Task SetRequireOtherAttendeesAsync(bool value) => ApplyAsync(p => p with { RequireOtherAttendees = value });

    /// <summary>Shows the engine's sample tug. It does not depend on Enable Tug and changes no setting.</summary>
    public Task TestTugAsync() => _engine.TestTugAsync(_text.GetString("TestTugTitle"), _text.GetString("TestTugCalendar")).AsTask();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
    }

    private async Task ApplyAsync(Func<EngineSettingsPatch, EngineSettingsPatch> edit)
    {
        var result = await _settings.ApplyAsync(edit).ConfigureAwait(false);
        OnUi(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (result.IsApplied)
            {
                Message = null;
                return;
            }

            // The control already moved under the person's hand; raising every value makes the page read the engine's again.
            Message = _text.GetString("Settings_ChangeNotSaved");
            RaiseControls();
        });
    }

    private void Apply(EngineSettingsView settings)
    {
        if (_disposed)
        {
            return;
        }

        var before = _view;
        _view = settings;
        if (before.LeadTime != settings.LeadTime)
        {
            LeadTimeOptions = Settings.LeadTimeOptions.Make(settings.LeadTime, _text);
            OnPropertyChanged(nameof(SelectedLeadTime));
        }

        if (before.Enabled != settings.Enabled)
        {
            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(HeroSubtitle));
        }

        if (before.RequireConferenceLink != settings.RequireConferenceLink)
        {
            OnPropertyChanged(nameof(RequireConferenceLink));
            OnPropertyChanged(nameof(ShowLinkWarning));
        }

        if (before.RequireOtherAttendees != settings.RequireOtherAttendees)
        {
            OnPropertyChanged(nameof(RequireOtherAttendees));
            OnPropertyChanged(nameof(ShowAttendeesWarning));
        }
    }

    private void RaiseControls()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(SelectedLeadTime));
        OnPropertyChanged(nameof(RequireConferenceLink));
        OnPropertyChanged(nameof(RequireOtherAttendees));
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
}
