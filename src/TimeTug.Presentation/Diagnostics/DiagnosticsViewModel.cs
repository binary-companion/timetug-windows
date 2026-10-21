using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Diagnostics;

/// <summary>
/// The Accounts pane's Diagnostics section (the Mac's "Copy Diagnostics" and "Clear"): copies a short report of recent activity to the
/// clipboard to share when reporting a problem, or empties the log. The report is a header (app and Windows versions, the time) and the
/// last <see cref="LineCount"/> lines of the rolling log, minus any line the safety net refuses.
/// </summary>
public sealed class DiagnosticsViewModel : ObservableObject, IDisposable
{
    /// <summary>The Mac keeps 500 entries; so does the copy.</summary>
    public const int LineCount = 500;

    private readonly IDiagnosticsReader _reader;
    private readonly IClipboardService _clipboard;
    private readonly IUiDispatcher _ui;
    private readonly ILocalizer _text;
    private readonly TimeProvider _clock;
    private readonly string _appVersion;
    private readonly string _systemVersion;
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private IReadOnlyCollection<string> _titles = [];

    public DiagnosticsViewModel(
        IEngineClient engine, IDiagnosticsReader reader, IClipboardService clipboard, IUiDispatcher ui, ILocalizer text, TimeProvider? clock = null,
        string appVersion = "unknown", string systemVersion = "unknown")
    {
        ArgumentNullException.ThrowIfNull(engine);
        _reader = reader;
        _clipboard = clipboard;
        _ui = ui;
        _text = text;
        _clock = clock ?? TimeProvider.System;
        _appVersion = appVersion;
        _systemVersion = systemVersion;
        _subscription = engine.State.Subscribe(new ActionObserver<AppState>(state =>
        {
            var titles = TitlesOf(state);
            lock (_gate)
            {
                _titles = titles;
            }
        }));
    }

    public string Title => _text.GetString("SettingsText_diagnostics");

    public string CopyText => _text.GetString("Diagnostics_Copy");

    public string ClearText => _text.GetString("Diagnostics_Clear");

    public string ExplanationText => _text.GetString("Diagnostics_Explanation");

    /// <summary>What the last Copy or Clear did, in words (also for a screen reader); null before either, or after <see cref="ClearStatus"/>.</summary>
    public string? StatusText
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Copies the report to the clipboard. False when the clipboard refused.</summary>
    public bool Copy()
    {
        IReadOnlyCollection<string> titles;
        lock (_gate)
        {
            titles = _titles;
        }

        var header = new[]
        {
            "TimeTug diagnostics",
            "App version: " + _appVersion,
            "Windows: " + _systemVersion,
            "Generated: " + _clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            string.Empty,
        };
        var report = string.Join(Environment.NewLine, header.Concat(_reader.ReadLastLines(LineCount, titles)));
        var copied = _clipboard.TrySetText(report);
        StatusText = _text.GetString(copied ? "Diagnostics_Copied" : "Diagnostics_CopyFailed");
        return copied;
    }

    /// <summary>Empties the log. False when a file could not be emptied.</summary>
    public bool Clear()
    {
        var cleared = _reader.Clear();
        StatusText = _text.GetString(cleared ? "Diagnostics_Cleared" : "Diagnostics_ClearFailed");
        return cleared;
    }

    public void ClearStatus() => StatusText = null;

    public void Dispose() => _subscription.Dispose();

    private static List<string> TitlesOf(AppState state)
    {
        var titles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var title in state.Events.Select(e => e.Title)
                     .Concat(state.Agenda.SelectMany(a => new[] { a.Title, a.CalendarTitle ?? string.Empty }))
                     .Concat(state.NextMeeting is { } next ? [next.Title] : [])
                     .Concat(state.CalendarGroups.SelectMany(g => g.Calendars).Select(c => c.Title))
                     .Concat(state.SystemCalendars.Select(c => c.Title)))
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                titles.Add(title);
            }
        }

        return [.. titles];
    }
}
