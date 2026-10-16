using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Tug;

/// <summary>
/// Everything one tug window shows and does: the countdown wording, the details and hints lines, the Snooze menu, the
/// keyboard routing, the spoken announcement and the choice of palette and backdrop from the Windows settings.
/// <para>
/// The wording follows the Mac's <c>TakeoverText</c> (so both apps say the same things), but every string comes from
/// <c>Strings/en-US/Resources.resw</c>. The model never reads the OS clock: the countdown is computed from the
/// injected <see cref="TimeProvider"/> each time <see cref="Refresh"/> is called, and it decides nothing about when
/// anything happens (the engine does that). It is not thread-safe: call it from the UI thread.
/// </para>
/// </summary>
public sealed class TugViewModel : ObservableObject
{
    /// <summary>The last minute before the start is urgent, and "Starting now" shows for a minute after it.</summary>
    private static readonly TimeSpan UrgentThreshold = TimeSpan.FromSeconds(60);

    private readonly TakeoverRequest _request;
    private readonly IUiSettings _settings;
    private readonly ILocalizer _text;
    private readonly TimeProvider _time;
    private readonly ITugResponder _responder;
    private readonly CultureInfo _culture;
    private bool _pulse;

    public TugViewModel(TugPlan plan, TugMonitorRole role, IUiSettings settings, ILocalizer text, TimeProvider time, ITugResponder responder)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(responder);
        _request = plan.Request ?? throw new ArgumentException("A tug window needs a request to show.", nameof(plan));
        _settings = settings;
        _text = text;
        _time = time;
        _responder = responder;
        _culture = CultureInfo.CurrentCulture;
        IsPrimary = role == TugMonitorRole.Primary;

        SnoozeChoices = _request.SnoozeOptions.Select(d => new SnoozeChoice(d, SnoozeTitle(d), SnoozeKey(d))).ToList();
        Details = BuildDetails();
        Hints = BuildHints();
        SpokenHints = BuildSpokenHints();
        ShowClickToRespond = plan.ShowClickToRespond;
        JoinFailedUrl = plan.JoinFailedUrl;
        ReadSettings();
        ApplyHeadline(ComputeHeadline(), raise: false);
        Announcement = BuildAnnouncement();
    }

    // ---- what the request says ----------------------------------------------------------------------------------

    public string Title => _request.Title;

    /// <summary>"12:01 – 12:31 PM · Work · 4 people"; parts that do not apply are left out.</summary>
    public string Details { get; }

    /// <summary>Join only exists when the request has a link.</summary>
    public bool HasJoin => _request.JoinUrl is { Length: > 0 };

    public IReadOnlyList<SnoozeChoice> SnoozeChoices { get; }

    public bool HasSnooze => SnoozeChoices.Count > 0;

    /// <summary>"Return joins · Esc dismisses · 1, 5 or 0 snoozes", listing only the keys that do something.</summary>
    public string Hints { get; }

    /// <summary>The hints as a screen reader should say them.</summary>
    public string SpokenHints { get; }

    /// <summary>True for the window with the controls; mirrors on other monitors show only the countdown and the title.</summary>
    public bool IsPrimary { get; }

    // ---- the countdown ------------------------------------------------------------------------------------------

    /// <summary>"Starts in" while counting down; null for "Starting now" and "Started ... ago".</summary>
    public string? CountdownLabel
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasCountdownLabel));
            }
        }
    }

    public bool HasCountdownLabel => CountdownLabel is not null;

    /// <summary>"4:05", "1:05:00", "Starting now" or "Started 3 min ago".</summary>
    public string CountdownValue { get; private set => SetProperty(ref field, value); } = "";

    /// <summary>True while the value is a clock (it is shown large); false for the two status texts.</summary>
    public bool IsCountdown { get; private set => SetProperty(ref field, value); }

    public TugTone Tone { get; private set => SetProperty(ref field, value); }

    /// <summary>The headline as a screen reader should say it: "Starts in 4 minutes 5 seconds".</summary>
    public string SpokenCountdown { get; private set => SetProperty(ref field, value); } = "";

    /// <summary>What Narrator is told when the tug appears: "Design review starts in 4 minutes". Fixed when the window is made.</summary>
    public string Announcement { get; }

    /// <summary>The window title; Windows reads it out when the window is activated.</summary>
    public string WindowTitle => _text.GetString("Tug_WindowTitle") + ": " + Announcement;

    /// <summary>Recomputes the countdown from the time provider; call it once a second.</summary>
    public void Refresh() => ApplyHeadline(ComputeHeadline(), raise: true);

    // ---- appearance ---------------------------------------------------------------------------------------------

    /// <summary>True when Windows animations are off: nothing may move.</summary>
    public bool ReducedMotion { get; private set => SetProperty(ref field, value); }

    /// <summary>The urgent countdown breathes slowly, unless motion is reduced.</summary>
    public bool PulseEnabled => _pulse;

    public TugPalette Palette { get; private set => SetProperty(ref field, value); }

    public TugBackdropKind Backdrop { get; private set => SetProperty(ref field, value); }

    /// <summary>Re-reads the Windows appearance settings (animations, transparency, contrast).</summary>
    public void RefreshSettings()
    {
        ReadSettings();
        UpdatePulse();
    }

    // ---- what the plan adds to the request ----------------------------------------------------------------------

    /// <summary>Windows did not hand over the keyboard: show "Click anywhere to respond".</summary>
    public bool ShowClickToRespond { get; private set => SetProperty(ref field, value); }

    /// <summary>Set when opening the join link failed: the link is shown to copy.</summary>
    public string? JoinFailedUrl
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasJoinFailed));
            }
        }
    }

    public bool HasJoinFailed => JoinFailedUrl is { Length: > 0 };

    /// <summary>Applies the parts of a newer plan for the same request that can change while the tug is up.</summary>
    public void Update(TugPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ShowClickToRespond = plan.ShowClickToRespond;
        JoinFailedUrl = plan.JoinFailedUrl;
    }

    // ---- responses ----------------------------------------------------------------------------------------------

    /// <summary>What a key means right now, without doing it; null when it means nothing (a mirror, or a snooze length not offered).</summary>
    public TugCommand? Resolve(TugKey key)
    {
        if (!IsPrimary)
        {
            return null;
        }

        return key switch
        {
            TugKey.Return => HasJoin ? new TugCommand.Join() : new TugCommand.Dismiss(),
            TugKey.Escape => new TugCommand.Dismiss(),
            TugKey.Digit1 => SnoozeFor("1"),
            TugKey.Digit5 => SnoozeFor("5"),
            TugKey.Digit0 => SnoozeFor("0"),
            _ => null,
        };
    }

    /// <summary>Does what the key means; true when it meant something (the window marks the key handled).</summary>
    public async ValueTask<bool> HandleKeyAsync(TugKey key)
    {
        switch (Resolve(key))
        {
            case TugCommand.Join:
                await JoinAsync();
                return true;
            case TugCommand.Snooze snooze:
                await SnoozeAsync(snooze.Duration);
                return true;
            case TugCommand.Dismiss:
                await DismissAsync();
                return true;
            default:
                return false;
        }
    }

    public ValueTask JoinAsync() => IsPrimary && HasJoin ? _responder.JoinAsync() : ValueTask.CompletedTask;

    public ValueTask SnoozeAsync(TimeSpan duration) => IsPrimary ? _responder.SnoozeAsync(duration) : ValueTask.CompletedTask;

    public ValueTask DismissAsync() => IsPrimary ? _responder.DismissAsync() : ValueTask.CompletedTask;

    /// <summary>Reports whether the primary window has the keyboard (the window layer calls it when activation changes).</summary>
    public ValueTask ReportFocusAsync(bool hasKeyboardFocus) => IsPrimary ? _responder.ReportFocusAsync(hasKeyboardFocus) : ValueTask.CompletedTask;

    // ---- fixed texts for the window -----------------------------------------------------------------------------

    public string JoinText => _text.GetString("Tug_Join");

    public string JoinAutomationName => _text.GetString("Tug_JoinAutomation");

    public string SnoozeText => _text.GetString("Tug_Snooze");

    public string SnoozeAutomationName => _text.GetString("Tug_SnoozeAutomation");

    public string DismissText => _text.GetString("Tug_Dismiss");

    public string DismissAutomationName => _text.GetString("Tug_DismissAutomation");

    public string ClickToRespondText => _text.GetString("Tug_ClickToRespond");

    public string MirrorHint => _text.GetString("Tug_MirrorHint");

    public string MirrorAutomationName => _text.GetString("Tug_MirrorAutomation");

    public string JoinFailedText => _text.GetString("Tug_JoinFailed");

    public string JoinLinkAutomationName => _text.GetString("Tug_JoinLinkAutomation");

    public string CopyLinkText => _text.GetString("Tug_CopyLink");

    public string CopyLinkAutomationName => _text.GetString("Tug_CopyLinkAutomation");

    public string LinkCopiedText => _text.GetString("Tug_LinkCopied");

    public string HeroAutomationName => _text.GetString("Tug_HeroAutomation");

    // ---- wording ------------------------------------------------------------------------------------------------

    private readonly record struct Headline(string? Label, string Value, bool IsCountdown, TugTone Tone, string Spoken);

    private Headline ComputeHeadline()
    {
        var untilStart = _request.Start - _time.GetUtcNow();
        if (untilStart > TimeSpan.Zero)
        {
            var total = (long)Math.Ceiling(untilStart.TotalSeconds);
            return new Headline(
                _text.GetString("Tug_StartsInLabel"),
                Clock(total),
                true,
                untilStart > UrgentThreshold ? TugTone.Calm : TugTone.Urgent,
                _text.Format("Tug_Spoken_StartsIn", SpokenClock(total)));
        }

        var elapsed = -untilStart;
        if (elapsed < UrgentThreshold)
        {
            var now = _text.GetString("Tug_StartingNow");
            return new Headline(null, now, false, TugTone.Urgent, now);
        }

        return new Headline(
            null,
            _text.Format("Tug_StartedAgo", Compact(elapsed)),
            false,
            TugTone.Late,
            _text.Format("Tug_Spoken_StartedAgo", SpokenDuration(elapsed)));
    }

    private void ApplyHeadline(Headline headline, bool raise)
    {
        CountdownLabel = headline.Label;
        CountdownValue = headline.Value;
        IsCountdown = headline.IsCountdown;
        Tone = headline.Tone;
        SpokenCountdown = headline.Spoken;
        if (raise)
        {
            UpdatePulse();
        }
        else
        {
            _pulse = ComputePulse();
        }
    }

    /// <summary>"4:05", or "1:05:00" from an hour up. The caller rounds up so the last second reads 0:01, not 0:00.</summary>
    private static string Clock(long totalSeconds) => totalSeconds >= 3600
        ? string.Create(CultureInfo.InvariantCulture, $"{totalSeconds / 3600}:{totalSeconds % 3600 / 60:00}:{totalSeconds % 60:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{totalSeconds / 60}:{totalSeconds % 60:00}");

    /// <summary>"3 min", or "1h 5m" from an hour up.</summary>
    private string Compact(TimeSpan elapsed)
    {
        var total = (long)elapsed.TotalSeconds;
        return total >= 3600
            ? _text.Format("Tug_HoursMinutes", total / 3600, total % 3600 / 60)
            : _text.Format("Tug_Minutes", total / 60);
    }

    /// <summary>"4 minutes 5 seconds" for a countdown (seconds are rounded up by the caller).</summary>
    private string SpokenClock(long totalSeconds)
    {
        var parts = new List<string>(3);
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        if (hours > 0)
        {
            parts.Add(Plural("Tug_Spoken_Hour", hours));
        }

        if (minutes > 0)
        {
            parts.Add(Plural("Tug_Spoken_Minute", minutes));
        }

        if (seconds > 0)
        {
            parts.Add(Plural("Tug_Spoken_Second", seconds));
        }

        return string.Join(' ', parts);
    }

    /// <summary>"3 hours 40 minutes", whole minutes only; "less than a minute" below one.</summary>
    private string SpokenDuration(TimeSpan duration)
    {
        var totalMinutes = Math.Max(0, (long)duration.TotalMinutes);
        var parts = new List<string>(2);
        if (totalMinutes / 60 > 0)
        {
            parts.Add(Plural("Tug_Spoken_Hour", totalMinutes / 60));
        }

        if (totalMinutes % 60 > 0)
        {
            parts.Add(Plural("Tug_Spoken_Minute", totalMinutes % 60));
        }

        return parts.Count == 0 ? _text.GetString("Tug_Spoken_LessThanAMinute") : string.Join(' ', parts);
    }

    private string Plural(string keyPrefix, long count) =>
        count == 1 ? _text.GetString(keyPrefix + "_One") : _text.Format(keyPrefix + "_Other", count);

    private string BuildAnnouncement()
    {
        var untilStart = _request.Start - _time.GetUtcNow();
        if (untilStart > TimeSpan.Zero)
        {
            return _text.Format("Tug_Announce_StartsIn", Title, SpokenDuration(untilStart));
        }

        var elapsed = -untilStart;
        return elapsed < UrgentThreshold
            ? _text.Format("Tug_Announce_StartingNow", Title)
            : _text.Format("Tug_Announce_StartedAgo", Title, SpokenDuration(elapsed));
    }

    /// <summary>"12:01 – 12:31 PM · Work · 4 people".</summary>
    private string BuildDetails()
    {
        var parts = new List<string> { BuildRange() };
        if (_request.CalendarTitle is { Length: > 0 } calendar)
        {
            parts.Add(calendar);
        }

        if (_request.OtherAttendeeCount > 0)
        {
            parts.Add(_text.Format("Tug_People", _request.OtherAttendeeCount + 1));
        }

        return string.Join(_text.GetString("Tug_DetailsSeparator"), parts);
    }

    /// <summary>"7:15 – 8:00 PM": the AM/PM marker is written once when both ends share it; "All day" for an all-day event.</summary>
    private string BuildRange()
    {
        if (_request.IsAllDay)
        {
            return _text.GetString("Tug_AllDay");
        }

        var start = Clock(_request.Start);
        var end = Clock(_request.End);
        foreach (var marker in new[] { _culture.DateTimeFormat.AMDesignator, _culture.DateTimeFormat.PMDesignator })
        {
            if (marker.Length > 0 && start.EndsWith(" " + marker, StringComparison.Ordinal) && end.EndsWith(" " + marker, StringComparison.Ordinal))
            {
                return _text.Format("Tug_Range", start[..^(marker.Length + 1)], end);
            }
        }

        return _text.Format("Tug_Range", start, end);
    }

    /// <summary>The short local time with a plain space, so the text is stable across Windows and ICU versions.</summary>
    private string Clock(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, _time.LocalTimeZone).ToString("t", _culture).Replace(' ', ' ').Replace(' ', ' ');

    private string BuildHints()
    {
        var parts = new List<string>();
        if (HasJoin)
        {
            parts.Add(_text.GetString("Tug_Hint_Join"));
        }

        parts.Add(_text.GetString("Tug_Hint_Dismiss"));
        var keys = SnoozeChoices.Where(c => c.Key is not null).Select(c => c.Key!).ToList();
        if (keys.Count == 1)
        {
            parts.Add(_text.Format("Tug_Hint_SnoozeOne", keys[0]));
        }
        else if (keys.Count > 1)
        {
            parts.Add(_text.Format("Tug_Hint_SnoozeMany", string.Join(_text.GetString("Tug_Hint_ListSeparator"), keys.SkipLast(1)), keys[^1]));
        }

        return string.Join(_text.GetString("Tug_DetailsSeparator"), parts);
    }

    private string BuildSpokenHints()
    {
        var sentences = new List<string>();
        if (HasJoin)
        {
            sentences.Add(_text.GetString("Tug_SpokenHint_Join"));
        }

        sentences.Add(_text.GetString("Tug_SpokenHint_Dismiss"));
        var snoozes = SnoozeChoices.Where(c => c.Key is not null)
            .Select((c, index) => _text.Format(index == 0 ? "Tug_SpokenHint_SnoozeFirst" : "Tug_SpokenHint_SnoozeNext", c.Key!, c.Title))
            .ToList();
        if (snoozes.Count > 0)
        {
            sentences.Add(string.Join(_text.GetString("Tug_Hint_ListSeparator"), snoozes) + ".");
        }

        return string.Join(' ', sentences);
    }

    // ---- snooze menu --------------------------------------------------------------------------------------------

    /// <summary>Number key that snoozes for a length: 60 s is 1, 300 s is 5, 600 s is 0 (the Mac's keys).</summary>
    private static string? SnoozeKey(TimeSpan duration) => (int)duration.TotalSeconds == duration.TotalSeconds
        ? (int)duration.TotalSeconds switch
        {
            60 => "1",
            300 => "5",
            600 => "0",
            _ => null,
        }
        : null;

    /// <summary>"1 minute", "5 minutes", "10 minutes".</summary>
    private string SnoozeTitle(TimeSpan duration) =>
        Plural("Tug_SnoozeMinute", Math.Max(1, (long)Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero)));

    private TugCommand.Snooze? SnoozeFor(string key) =>
        SnoozeChoices.FirstOrDefault(c => c.Key == key) is { } choice ? new TugCommand.Snooze(choice.Duration) : null;

    // ---- settings -----------------------------------------------------------------------------------------------

    private void ReadSettings()
    {
        ReducedMotion = !_settings.AnimationsEnabled;
        Palette = _settings.HighContrast ? TugPalette.SystemContrast : TugPalette.Brand;
        Backdrop = _settings.HighContrast
            ? TugBackdropKind.SystemContrast
            : _settings.TransparencyEffectsEnabled ? TugBackdropKind.Acrylic : TugBackdropKind.SolidNavy;
    }

    private bool ComputePulse() => IsCountdown && Tone == TugTone.Urgent && !ReducedMotion;

    private void UpdatePulse()
    {
        var pulse = ComputePulse();
        if (pulse != _pulse)
        {
            _pulse = pulse;
            OnPropertyChanged(nameof(PulseEnabled));
        }
    }
}
