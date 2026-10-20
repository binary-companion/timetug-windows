#if SELFTEST
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-meeting-gone</c>: the takeover is due while the session is locked and is held. The meeting is then deleted from
/// the calendar (the recorded answer changes and the engine refreshes). On unlock no tug appears, the engine hears exactly one
/// <c>takeover.closed</c>, and diagnostics say the meeting is no longer on the calendar.
/// </summary>
internal static class TugMeetingGoneScenario
{
    public const string Name = "tug-meeting-gone";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartAsync(options, cancellation);
        var run = rig.Run;
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        rig.Session.Lock();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(15));
        run.Check(await rig.WaitFor(() => rig.Controller.State == TugState.HeldUntilReturn), "the takeover is held while the session is locked: " + rig.Controller.State);

        // Somebody deletes the meeting while the PC is locked.
        rig.AnswerEventsWith("google-events-deleted.json");
        await rig.RefreshAsync();
        run.Check(await rig.WaitFor(() => rig.Latest is { Events.Count: 0 }), "the engine's state no longer has the meeting");
        run.Check(rig.Controller.State == TugState.HeldUntilReturn && rig.Client.Sent.Count == 0, "still held, still nothing sent");

        rig.Session.Unlock();
        run.Check(await rig.WaitFor(() => rig.Controller.State == TugState.Hidden && rig.Controller.Current is null), "unlocking ends the request: " + rig.Controller.State);
        await rig.Settle(800);
        run.Check(rig.Windows.WindowCount == 0 && rig.Toasts.Shown.Count == 0, "no tug and no toast appeared");
        run.Check(rig.Client.Sent.SequenceEqual(["closed:" + EngineRig.MeetingId]), "the engine heard exactly one takeover.closed and no takeover.presented: " + string.Join(", ", rig.Client.Sent));
        run.Check(rig.Diagnostics.Entries.Any(e => e.Message.Contains("no longer on the calendar", StringComparison.Ordinal)), "diagnostics say the meeting is no longer on the calendar");
        run.Check(rig.Launcher.Opened.Count == 0, "no link was opened");
        return rig.Finish();
    }
}
#endif
