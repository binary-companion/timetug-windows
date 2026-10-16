namespace TimeTug.Presentation.Tug;

/// <summary>How urgent the headline is: calm (more than a minute), urgent (the last minute and "Starting now"), late (already started).</summary>
public enum TugTone
{
    Calm,
    Urgent,
    Late,
}

/// <summary>Which colours the tug uses.</summary>
public enum TugPalette
{
    /// <summary>The brand navy palette.</summary>
    Brand,

    /// <summary>The Windows contrast theme's own colours (window, text, highlight): the navy palette is replaced entirely.</summary>
    SystemContrast,
}

/// <summary>What is behind the tug's content.</summary>
public enum TugBackdropKind
{
    /// <summary>Desktop acrylic with a navy tint.</summary>
    Acrylic,

    /// <summary>Solid navy: transparency effects are off.</summary>
    SolidNavy,

    /// <summary>The contrast theme's window colour.</summary>
    SystemContrast,
}

/// <summary>A key the tug window understands.</summary>
public enum TugKey
{
    Return,
    Escape,
    Digit1,
    Digit5,
    Digit0,
}

/// <summary>One colour of the brand palette.</summary>
public readonly record struct TugColor(byte R, byte G, byte B);

/// <summary>The brand colours of the takeover screen (the Mac's <c>TakeoverColors</c>).</summary>
public static class TugBrand
{
    public static TugColor Navy { get; } = new(13, 20, 41);

    public static TugColor Calm { get; } = new(217, 232, 255);

    public static TugColor Orange { get; } = new(255, 176, 59);

    public static TugColor RedOrange { get; } = new(255, 107, 74);

    public static TugColor Muted { get; } = new(166, 181, 212);

    public static TugColor Hint { get; } = new(128, 143, 173);

    public static TugColor Blue { get; } = new(77, 148, 255);

    public static TugColor OnBlue { get; } = new(10, 18, 38);

    /// <summary>Opacity of the navy tint over the acrylic.</summary>
    public const double AcrylicTintOpacity = 0.62;

    public static TugColor Headline(TugTone tone) => tone switch
    {
        TugTone.Calm => Calm,
        TugTone.Urgent => Orange,
        _ => RedOrange,
    };
}

/// <summary>A response the keyboard asked for.</summary>
public abstract record TugCommand
{
    public sealed record Join : TugCommand;

    public sealed record Snooze(TimeSpan Duration) : TugCommand;

    public sealed record Dismiss : TugCommand;
}

/// <summary>One entry of the Snooze menu. <see cref="Key"/> is the number key that snoozes for it (1, 5, 0), when it has one.</summary>
public sealed record SnoozeChoice(TimeSpan Duration, string Title, string? Key);
