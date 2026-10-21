using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Welcome;
using Pkg = TimeTug.Engine;

namespace TimeTug.Presentation.Tests.Settings;

/// <summary>
/// A scripted engine for the settings panes: it holds the settings and the calendars, takes a patch the way the engine does (a key list
/// replaces the stored list; nothing else is normalised), publishes a state with a rising revision, and can refuse or ignore a patch.
/// The pane view models and the real <see cref="SettingsClient"/> run over it.
/// </summary>
internal sealed class SettingsPaneRig : IDisposable
{
    private ulong _revision = 1;

    public SettingsPaneRig(EngineSettingsView? settings = null, IReadOnlyList<Pkg.CalendarInfo>? calendars = null, IReadOnlyList<AccountInfo>? accounts = null)
    {
        Settings = settings ?? EngineSettingsView.Defaults;
        Calendars = calendars ?? [];
        Accounts = accounts ?? [new AccountInfo("g1", "google", "me@gmail.com", "ok", 1)];
        Engine = new FakeEngineClient();
        Time = new FakeTimeProvider();
        Client = new SettingsClient(Engine, Time, TimeSpan.FromSeconds(5));
        Engine.OnApplySettings = (patch, id) =>
        {
            Received.Add(patch);
            switch (Mode)
            {
                case AnswerMode.Accept:
                    Accept(patch);
                    break;
                case AnswerMode.Reject:
                    Engine.Push(new EngineDiagnosticInfo("warning", "command", "refused") { CommandId = id });
                    break;
                case AnswerMode.Ignore:
                    break;
            }
        };
        Publish();
    }

    public enum AnswerMode
    {
        Accept,
        Reject,
        Ignore,
    }

    public FakeEngineClient Engine { get; }

    public FakeTimeProvider Time { get; }

    public SettingsClient Client { get; }

    public ReswLocalizer Text { get; } = new();

    public ImmediateDispatcher Ui { get; } = new();

    public AnswerMode Mode { get; set; }

    /// <summary>The settings the engine holds now.</summary>
    public EngineSettingsView Settings { get; private set; }

    public IReadOnlyList<Pkg.CalendarInfo> Calendars { get; private set; }

    public IReadOnlyList<AccountInfo> Accounts { get; }

    public InferenceAvailability Inference { get; set; } = InferenceAvailability.Disabled;

    public string? InferenceReason { get; set; }

    public string? InferenceEngine { get; set; }

    /// <summary>Every patch the engine received, in order (as the view model built it).</summary>
    public List<EngineSettingsPatch> Received { get; } = [];

    /// <summary>The engine takes the patch: it replaces the fields the patch sets, exactly, and publishes.</summary>
    public void Accept(EngineSettingsPatch patch)
    {
        Settings = Settings with
        {
            Enabled = patch.Enabled ?? Settings.Enabled,
            LeadTime = patch.LeadTime ?? Settings.LeadTime,
            RequireConferenceLink = patch.RequireConferenceLink ?? Settings.RequireConferenceLink,
            RequireOtherAttendees = patch.RequireOtherAttendees ?? Settings.RequireOtherAttendees,
            SkipAllDayEvents = patch.SkipAllDayEvents ?? Settings.SkipAllDayEvents,
            InferenceEnabled = patch.InferenceEnabled ?? Settings.InferenceEnabled,
            HiddenCalendarKeys = patch.HiddenCalendarKeys is null ? Settings.HiddenCalendarKeys : new HashSet<string>(patch.HiddenCalendarKeys, StringComparer.Ordinal),
            TakeoverCalendarKeys = patch.TakeoverCalendarKeys is null ? Settings.TakeoverCalendarKeys : new HashSet<string>(patch.TakeoverCalendarKeys, StringComparer.Ordinal),
        };
        _revision++;
        Publish();
    }

    /// <summary>The engine publishes new calendars (or a changed account list).</summary>
    public void SetCalendars(IReadOnlyList<Pkg.CalendarInfo> calendars)
    {
        Calendars = calendars;
        _revision++;
        Publish();
    }

    /// <summary>The engine publishes with other settings (as if changed from elsewhere).</summary>
    public void Set(EngineSettingsView settings)
    {
        Settings = settings;
        _revision++;
        Publish();
    }

    public void Publish() => Engine.Push(Snapshot());

    public AppState Snapshot()
    {
        var layout = CalendarSections.Make(Calendars, Accounts, Settings, Text);
        return new AppState(Accounts, States.Kinds, [], null, null, false, Settings.Enabled)
        {
            Revision = _revision,
            LeadTime = Settings.LeadTime,
            Settings = Settings,
            CalendarGroups = layout.Groups,
            SystemCalendars = layout.System,
            Inference = Inference,
            InferenceReason = InferenceReason,
            InferenceEngineName = InferenceEngine,
        };
    }

    public static Pkg.CalendarInfo Cal(string source, string id, string title, string? account = null, string kind = "standard", string? color = null) => new()
    {
        Key = $"{source}/{id}",
        SourceID = source,
        CalendarID = id,
        Title = title,
        AccountName = account,
        Kind = kind,
        ColorHex = color,
    };

    public void Dispose() => Client.Dispose();
}
