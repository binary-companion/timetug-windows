namespace TimeTug.Presentation.Tug;

/// <summary>What the tug window can ask for: the person's responses and the focus report. <see cref="TugController"/> implements it.</summary>
public interface ITugResponder
{
    ValueTask JoinAsync();

    ValueTask SnoozeAsync(TimeSpan duration);

    ValueTask DismissAsync();

    /// <summary>Whether the primary window actually has the keyboard (false shows "Click anywhere to respond").</summary>
    ValueTask ReportFocusAsync(bool hasKeyboardFocus);
}
