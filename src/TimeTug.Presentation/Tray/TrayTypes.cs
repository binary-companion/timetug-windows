namespace TimeTug.Presentation.Tray;

/// <summary>Which tray icon to show: the quiet puppy, the colour puppy when a meeting is near, or the puppy with a mark when the person is needed.</summary>
public enum TrayIconState
{
    Idle,
    Soon,
    Attention,
}

/// <summary>The commands a tray row stands for.</summary>
public enum TrayCommand
{
    Next,
    EnableTug,
    TestTug,
    Settings,
    Quit,
}

public enum TrayRowKind
{
    /// <summary>Text only; the menu draws it unavailable.</summary>
    Information,

    /// <summary>A command with a check mark.</summary>
    Toggle,

    Command,

    Separator,
}

/// <summary>One menu row. <see cref="Command"/> is null for a separator.</summary>
public sealed record TrayRow(TrayCommand? Command, TrayRowKind Kind, string Text, bool IsEnabled, bool IsChecked = false)
{
    public static TrayRow Separator { get; } = new(null, TrayRowKind.Separator, string.Empty, false);
}

/// <summary>
/// The file the tray icon is drawn from: one multi-size icon per variant, with the remaining whole minutes (1 to 9) drawn on the
/// colour icon in the last ten minutes. <see cref="Digit"/> is null for every other case.
/// </summary>
public sealed record TrayIconView(TrayIconState State, bool LightTaskbar, int? Digit = null)
{
    /// <summary>
    /// <c>light</c> and <c>dark</c> are the quiet puppy for a light or a dark taskbar (the taskbar theme, not the app theme);
    /// <c>color</c> and <c>attention</c> read on either.
    /// </summary>
    public string Variant => State switch
    {
        TrayIconState.Soon => "color",
        TrayIconState.Attention => "attention",
        _ => LightTaskbar ? "light" : "dark",
    };

    public string FileName => $"tray-{Variant}.ico";
}

/// <summary>Everything the tray shows right now.</summary>
public sealed record TrayView(IReadOnlyList<TrayRow> Rows, TrayIconView Icon, string Tooltip)
{
    /// <summary>What the icon and its tooltip should be right now.</summary>
    public TrayIconPlan Plan => new(Icon.State, Icon.Digit, Tooltip);

    public bool SameAs(TrayView other) => Icon == other.Icon && Tooltip == other.Tooltip && Rows.SequenceEqual(other.Rows);
}

/// <summary>What the tray asks the rest of the app to do.</summary>
public interface ITrayHost
{
    /// <summary>Opens the welcome window (the stand-in for the flyout until slice 2).</summary>
    void ShowWelcome();

    /// <summary>Shows the test tug.</summary>
    ValueTask TestTugAsync();

    /// <summary>Quits the application in order (closes an outstanding takeover, stops the engine, exits).</summary>
    ValueTask QuitAsync();
}

/// <summary>Whether the person is needed (a tug is held because a toast was refused). <c>TugController</c> implements it.</summary>
public interface ITugAttention
{
    bool NeedsAttention { get; }

    event Action? AttentionChanged;
}
