#if SELFTEST
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-late-return</c>: the session is locked when the engine says the takeover is due, so nothing is shown and
/// nothing is sent. Ten minutes into the meeting the person unlocks, and the full tug appears at once with "Started 10 min ago" and
/// exactly one <c>takeover.presented</c>. The app's clock and the engine's manual clock move together (<c>test.advanceClock</c>).
/// </summary>
internal static class TugLateReturnScenario
{
    public const string Name = "tug-late-return";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartAsync(options, cancellation);
        var run = rig.Run;
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        // The person locks the PC; the clocks reach the start of the meeting (13:16), so the engine fires its takeover.
        rig.Session.Lock();
        await rig.AdvanceAsync(TimeSpan.FromMinutes(16));
        run.Check(await rig.WaitFor(() => rig.Controller.State == TugState.HeldUntilReturn), "the takeover is held while the session is locked: " + rig.Controller.State);
        run.Check(rig.Windows.WindowCount == 0 && rig.Toasts.Shown.Count == 0, "nothing was shown while locked");
        run.Check(rig.Client.Sent.Count == 0, "nothing was sent to the engine while locked: " + string.Join(", ", rig.Client.Sent));

        // Ten more minutes pass, and the person comes back.
        await rig.AdvanceAsync(TimeSpan.FromMinutes(10));
        rig.Session.Unlock();
        if (!run.Check(await rig.WaitFor(() => rig.Windows.Primary is not null && rig.Windows.WindowCount == rig.Monitors.Monitors.Count), "unlocking shows the full tug on every monitor"))
        {
            return rig.Finish();
        }

        var plan = rig.Controller.Plan;
        run.Check(plan.Mode == TugMode.FullScreen && plan.LateBy == TimeSpan.FromMinutes(10), $"it is a full-screen tug that is 10 minutes late: {plan.Mode}, late by {plan.LateBy}");
        var primary = rig.Windows.Primary!;
        run.Check(primary.ViewModel.CountdownValue == "Started 10 min ago", "the window says how late: " + primary.ViewModel.CountdownValue);
        run.Check(primary.ViewModel.Title == "Design review" && ScenarioUi.HasButton(primary, "Join meeting"), "it is the recorded meeting, with its meeting link");
        run.Check(rig.Client.Sent.SequenceEqual(["presented:" + EngineRig.MeetingId]), "the engine heard exactly one takeover.presented: " + string.Join(", ", rig.Client.Sent));
        await rig.Settle();
        await run.ShotAsync(primary, "late-return");

        run.Check(ScenarioUi.Invoke(primary, "Join meeting"), "Join can be clicked");
        run.Check(await rig.WaitHiddenAsync(), "the windows close");
        run.Check(rig.Launcher.Opened.SequenceEqual(["https://meet.google.com/abc-defg-hij"]), "Join opened the recorded meeting link: " + string.Join(", ", rig.Launcher.Opened));
        run.Check(rig.Terminal.SequenceEqual(["join:" + EngineRig.MeetingId]), "exactly one terminal command: " + string.Join(", ", rig.Terminal));
        return rig.Finish();
    }
}
#endif
