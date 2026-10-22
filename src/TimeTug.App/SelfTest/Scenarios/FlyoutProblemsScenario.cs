#if SELFTEST
using Microsoft.UI.Xaml.Automation;
using TimeTug.Presentation.Engine;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>flyout-problems</c>: a Google account whose sign-in expired and an iCloud source that fails sit above the header.
/// The expired one has a "Sign in again" button that asks the engine to reconnect that account (one command); the failing one has an
/// "Open Accounts" button that closes the flyout and asks the host to open the Accounts pane for that account. The day below them still shows.
/// </summary>
internal static class FlyoutProblemsScenario
{
    public const string Name = "flyout-problems";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = FlyoutRig.Create(options, cancellation);
        var run = rig.Run;
        var problems = new SourceProblem[]
        {
            new("google-conn-1", "you@example.com", ProblemKind.AuthExpired, null, "conn-1"),
            new("icloud-conn-2", "Work iCloud", ProblemKind.Failing, "the server did not answer", "conn-2"),
        };
        rig.Tug.Engine.Push(rig.State([rig.Item("review", "Design review", 30, 30, AgendaPhase.Upcoming, join: "https://zoom.us/j/1", next: true)], problems));

        var window = await rig.OpenAsync();
        run.Check(window is not null, "the flyout opens");
        if (window is null)
        {
            return rig.Tug.Finish();
        }

        var rows = window.ProblemRows;
        run.Check(rows.Count == 2, "two problem rows");
        run.Check(rows[0].Row.Message == "you@example.com needs you to sign in again", "the expired sign-in says so: " + rows[0].Row.Message);
        run.Check(rows[1].Row.Message == "Work iCloud failed: the server did not answer", "the failing source says why: " + rows[1].Row.Message);
        run.Check(rows[0].ActionButton is not null && AutomationProperties.GetName(rows[0].ActionButton) == "Sign in to you@example.com again", "the expired row has a named Sign in again button");
        run.Check(rows[1].ActionButton is not null && AutomationProperties.GetName(rows[1].ActionButton) == "Open Accounts to look at Work iCloud", "the failing row has a named Open Accounts button (nothing to fix from the flyout, but the pane shows more)");
        run.Check(window.EventCards.Count == 1, "the day still shows below the problems");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Tug.Settle(600);
        await run.ShotAsync(window, "problems");
        await rig.Tug.HoldAsync();

        run.Check(ScenarioUi.Invoke(window, "Sign in to you@example.com again"), "Sign in again is pressed");
        run.Check(await rig.Tug.WaitFor(() => rig.Tug.Engine.Sent.Contains("reconnect:conn-1")), "the engine was asked to reconnect that account once");
        run.Check(rig.Tug.Engine.Sent.Count(c => c == "reconnect:conn-1") == 1 && rig.Tug.Engine.Sent.Count(c => c.StartsWith("reconnect", StringComparison.Ordinal)) == 1, "exactly one reconnect: " + string.Join(", ", rig.Tug.Engine.Sent));

        // The problem goes away when the engine says the account is fine.
        rig.Tug.Engine.Push(rig.State([rig.Item("review", "Design review", 30, 30, AgendaPhase.Upcoming, next: true)], [problems[1]]));
        run.Check(await rig.Tug.WaitFor(() => rig.Window?.ProblemRows.Count == 1), "the row goes when its problem does");

        // Open Accounts: the flyout gets out of the way and the host is asked for the pane, naming the account.
        var asked = new List<string>();
        rig.Host.AccountsRequested += asked.Add;
        run.Check(ScenarioUi.Invoke(window, "Open Accounts to look at Work iCloud"), "Open Accounts is pressed");
        run.Check(asked.SequenceEqual(["conn-2"]) && !rig.Host.IsOpen, "the host is asked for that account's Accounts pane and the flyout has closed: " + string.Join(",", asked));
        run.Check(!rig.Tug.Engine.Sent.Contains("reconnect:conn-2"), "and nothing was sent to the engine");
        rig.Host.Close();
        return rig.Tug.Finish();
    }
}
#endif
