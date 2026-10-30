namespace TimeTug.Presentation.Chip;

/// <summary>
/// What the chip shows right now, with all text decided. Equal plans are interchangeable, so the view model can tell when nothing changed.
/// </summary>
/// <param name="Visible">False when there is nothing to show (the chip is hidden, never empty).</param>
/// <param name="Text">The whole line: "Planning review · in 12 min".</param>
/// <param name="AutomationName">What a screen reader says for the chip as a whole: "Planning review, in 12 minutes".</param>
/// <param name="AccentColorHex">The next meeting's calendar colour for the thin bar on the chip; null when the calendar has none.</param>
/// <param name="CanJoin">The meeting has a link the chip's menu can open.</param>
/// <param name="Announcement">A sentence for a screen reader to say now (30, 15, 5 and 1 minute before, and at the start); null at every other time.</param>
public sealed record ChipPlan(bool Visible, string Text, string AutomationName, string? AccentColorHex, bool CanJoin, string? Announcement)
{
    /// <summary>Nothing to show.</summary>
    public static ChipPlan Hidden { get; } = new(false, string.Empty, string.Empty, null, false, null);

    /// <summary>The meeting's title, which the window truncates with an ellipsis (the countdown beside it never is).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>"in 12 min", "now · ends 10:50".</summary>
    public string Countdown { get; init; } = string.Empty;

    /// <summary>The engine's id of the meeting the chip shows (what Join and "Hide until the next meeting" act on); null when hidden.</summary>
    public string? EventId { get; init; }
}
