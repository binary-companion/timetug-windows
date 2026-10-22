using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Presentation.Settings;

/// <summary>One option of an Appearance setting: its value, its visible title, the name assistive technology reads, and a line that says what it does.</summary>
public sealed record AppearanceChoice<T>(T Value, string Title, string AutomationName, string Detail);

/// <summary>
/// The Appearance pane (the Mac's <c>AppearancePane</c>): the tray icon style (the Mac's menu-bar text modes), the flyout backdrop (the Mac's
/// popup card styles) and the theme (Light, Dark or Auto). They are presentation preferences kept app-side in the
/// <see cref="IPresentationSettingsStore"/>; choosing one saves it at once and the store raises <c>Changed</c> once, which the tray, the
/// flyout host and the theme service listen to. The pane also says when Windows overrides the backdrop (reduced transparency, contrast themes).
/// </summary>
public sealed class AppearanceViewModel : ObservableObject, IDisposable
{
    private readonly IPresentationSettingsStore _store;
    private readonly IUiSettings _ui;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILocalizer _text;
    private PresentationSettings _current;
    private bool _backdropOverridden;
    private string _overrideText;
    private bool _previewDark;
    private bool _taskbarLight;
    private bool _disposed;

    public AppearanceViewModel(IPresentationSettingsStore store, IUiSettings ui, IUiDispatcher dispatcher, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(text);
        _store = store;
        _ui = ui;
        _dispatcher = dispatcher;
        _text = text;
        _current = store.Load();
        _backdropOverridden = AppearanceResolver.BackdropOverridden(ui);
        _overrideText = OverrideTextNow();
        _previewDark = AppearanceResolver.IsDark(_current.Theme, ui);
        _taskbarLight = ui.TaskbarUsesLightTheme;
        ThemeChoices =
        [
            Choice(AppTheme.UseSystem, "Appearance_Theme_UseSystem"),
            Choice(AppTheme.Light, "Appearance_Theme_Light"),
            Choice(AppTheme.Dark, "Appearance_Theme_Dark"),
        ];
        TrayChoices =
        [
            Choice(TrayIconStyle.IconOnly, "Appearance_Tray_IconOnly"),
            Choice(TrayIconStyle.IconWithMinuteDigit, "Appearance_Tray_IconWithMinuteDigit"),
        ];
        BackdropChoices =
        [
            Choice(FlyoutBackdrop.Material, "Appearance_Backdrop_Material"),
            Choice(FlyoutBackdrop.Solid, "Appearance_Backdrop_Solid"),
        ];
        store.Changed += OnStoreChanged;
        ui.Changed += OnUiChanged;
    }

    /// <summary>The search catalog's ids for the three settings.</summary>
    public static class Ids
    {
        public const string Theme = "appearance";
        public const string Backdrop = "popup-card-style";
        public const string TrayStyle = "tray-icon-style";
    }

    public IReadOnlyList<AppearanceChoice<AppTheme>> ThemeChoices { get; }

    public IReadOnlyList<AppearanceChoice<TrayIconStyle>> TrayChoices { get; }

    public IReadOnlyList<AppearanceChoice<FlyoutBackdrop>> BackdropChoices { get; }

    public AppTheme Theme => _current.Theme;

    public TrayIconStyle TrayStyle => _current.TrayStyle;

    /// <summary>The backdrop the person chose (kept even while Windows overrides it).</summary>
    public FlyoutBackdrop Backdrop => _current.Backdrop;

    /// <summary>The backdrop the flyout actually uses right now.</summary>
    public FlyoutBackdrop EffectiveBackdrop => _backdropOverridden ? FlyoutBackdrop.Solid : _current.Backdrop;

    /// <summary>True while reduced transparency or a contrast theme decides the flyout's backdrop.</summary>
    public bool BackdropOverridden => _backdropOverridden;

    /// <summary>What is overriding the choice right now (empty when nothing is).</summary>
    public string BackdropOverrideText => _overrideText;

    /// <summary>The footnote under the backdrop choices; it always says that reduced transparency and contrast themes override them.</summary>
    public string BackdropNote => _text.GetString("Appearance_BackdropNote");

    public string ThemeNote => _text.GetString("Appearance_ThemeNote");

    public string TrayNote => _text.GetString("Appearance_TrayNote");

    /// <summary>True when the theme that applies is the dark one: the previews are drawn in it.</summary>
    public bool PreviewDark => _previewDark;

    /// <summary>True when the taskbar is light: the tray preview's strip follows the taskbar, not the app theme.</summary>
    public bool TaskbarLight => _taskbarLight;

    public void SetTheme(AppTheme theme) => Save(s => s.Theme == theme ? null : s with { Theme = theme });

    public void SetTrayStyle(TrayIconStyle style) => Save(s => s.TrayStyle == style ? null : s with { TrayStyle = style });

    public void SetBackdrop(FlyoutBackdrop backdrop) => Save(s => s.Backdrop == backdrop ? null : s with { Backdrop = backdrop });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Changed -= OnStoreChanged;
        _ui.Changed -= OnUiChanged;
    }

    private string OverrideTextNow() => !AppearanceResolver.BackdropOverridden(_ui)
        ? string.Empty
        : _text.GetString(_ui.HighContrast ? "Appearance_BackdropOverride_Contrast" : "Appearance_BackdropOverride_Transparency");

    private AppearanceChoice<T> Choice<T>(T value, string key) =>
        new(value, _text.GetString(key), _text.GetString(key + "_Name"), _text.GetString(key + "_Detail"));

    /// <summary>Stores the edited settings (read fresh, so the Settings window's place and the other choices are kept); a null edit is no change.</summary>
    private void Save(Func<PresentationSettings, PresentationSettings?> edit)
    {
        var edited = edit(_store.Load());
        if (edited is not null)
        {
            // The store raises Changed once; OnStoreChanged brings the pane up to date.
            _store.Save(edited);
        }
    }

    private void OnStoreChanged() => OnUi(() => Apply(_store.Load()));

    private void OnUiChanged() => OnUi(() => Apply(_current));

    private void OnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }

    private void Apply(PresentationSettings settings)
    {
        if (_disposed)
        {
            return;
        }

        var before = _current;
        _current = settings;
        var overridden = AppearanceResolver.BackdropOverridden(_ui);
        var dark = AppearanceResolver.IsDark(settings.Theme, _ui);
        var light = _ui.TaskbarUsesLightTheme;

        if (before.Theme != settings.Theme)
        {
            OnPropertyChanged(nameof(Theme));
        }

        if (before.TrayStyle != settings.TrayStyle)
        {
            OnPropertyChanged(nameof(TrayStyle));
        }

        if (before.Backdrop != settings.Backdrop)
        {
            OnPropertyChanged(nameof(Backdrop));
        }

        var overrideChanged = overridden != _backdropOverridden;
        _backdropOverridden = overridden;
        if (overrideChanged)
        {
            OnPropertyChanged(nameof(BackdropOverridden));
        }

        if (overrideChanged || before.Backdrop != settings.Backdrop)
        {
            OnPropertyChanged(nameof(EffectiveBackdrop));
        }

        var overrideText = OverrideTextNow();
        if (overrideText != _overrideText)
        {
            _overrideText = overrideText;
            OnPropertyChanged(nameof(BackdropOverrideText));
        }

        if (dark != _previewDark)
        {
            _previewDark = dark;
            OnPropertyChanged(nameof(PreviewDark));
        }

        if (light != _taskbarLight)
        {
            _taskbarLight = light;
            OnPropertyChanged(nameof(TaskbarLight));
        }
    }
}
