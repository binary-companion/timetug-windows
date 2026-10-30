using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.JumpList;

/// <summary>
/// The tasks the jump list holds right now, in order. It is built only from the engine's state, and only the flags the engine
/// publishes: the fixed tasks, plus <see cref="JumpAction.JoinNext"/> when the meeting that is running or next has a join link (the same
/// rule as the Join button on the flyout's cards, which show it for the meeting in progress and the next one). Nothing from the
/// calendar is in the plan: it is a list of actions, so two states with the same flags give equal plans and the service saves the list
/// only when the plan changes.
/// </summary>
public sealed record JumpListPlan(ValueList<JumpAction> Actions)
{
    /// <summary>The list without "Join next meeting": what is saved at startup and on quit, because Windows keeps the list on disk after the app exits.</summary>
    public static JumpListPlan Base { get; } = new([JumpAction.Flyout, JumpAction.TestTug, JumpAction.Settings]);

    private static JumpListPlan WithJoin { get; } = new([JumpAction.Flyout, JumpAction.JoinNext, JumpAction.TestTug, JumpAction.Settings]);

    /// <summary>The plan for the engine's state (the base plan before the first state).</summary>
    public static JumpListPlan For(AppState? state) => JoinEventId(state) is null ? Base : WithJoin;

    /// <summary>
    /// The id of the meeting "Join next meeting" opens: the first timed meeting the engine says is in progress and has a link, else
    /// the one it names as next when that has one; null when there is none. All-day events and past meetings never count.
    /// </summary>
    public static string? JoinEventId(AppState? state)
    {
        if (state is null)
        {
            return null;
        }

        var timed = state.Agenda.Where(i => !i.IsAllDay && i.Phase != AgendaPhase.Past && i.JoinUrl is not null).ToList();
        return (timed.Where(i => i.Phase == AgendaPhase.Current).OrderBy(i => i.Start).FirstOrDefault()
                ?? timed.FirstOrDefault(i => i.IsNext))?.EventId;
    }
}
