namespace TimeTug.Presentation.JumpList;

/// <summary>What an item of the taskbar jump list asks the app to do. The argument form is <c>--jump flyout|join-next|test-tug|settings</c>.</summary>
public enum JumpAction
{
    /// <summary>Open the flyout with today's meetings.</summary>
    Flyout,

    /// <summary>Open the link of the meeting that is running or next.</summary>
    JoinNext,

    /// <summary>Show a test tug (the tray menu's Test tug).</summary>
    TestTug,

    /// <summary>Open the Settings window.</summary>
    Settings,
}

/// <summary>The command-line form of a <see cref="JumpAction"/>: one switch and one fixed word, never anything from the calendar.</summary>
public static class JumpArguments
{
    /// <summary>The switch that introduces a jump action.</summary>
    public const string Switch = "--jump";

    /// <summary>The words that follow the switch.</summary>
    public static string Word(JumpAction action) => action switch
    {
        JumpAction.Flyout => "flyout",
        JumpAction.JoinNext => "join-next",
        JumpAction.TestTug => "test-tug",
        JumpAction.Settings => "settings",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>The arguments a jump list item carries, for example <c>--jump join-next</c>.</summary>
    public static string For(JumpAction action) => Switch + " " + Word(action);

    /// <summary>The action a word names (case does not matter), or false for any other word.</summary>
    public static bool TryParseWord(string? word, out JumpAction action)
    {
        foreach (var candidate in Enum.GetValues<JumpAction>())
        {
            if (string.Equals(word, Word(candidate), StringComparison.OrdinalIgnoreCase))
            {
                action = candidate;
                return true;
            }
        }

        action = default;
        return false;
    }
}
