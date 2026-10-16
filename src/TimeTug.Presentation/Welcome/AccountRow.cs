namespace TimeTug.Presentation.Welcome;

/// <summary>A connected account as one line, "Google \u00B7 you@example.com \u00B7 3 calendars", plus why it needs attention, if it does.</summary>
public sealed record AccountRow(string ConnectionId, string Summary, string? Problem)
{
    public bool HasProblem => !string.IsNullOrEmpty(Problem);
}
