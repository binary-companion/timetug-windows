#if SELFTEST
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-toast</c>: while Windows says a full-screen window cannot be shown (an exclusive full-screen app or a
/// presentation) the takeover falls back to a toast and no tug window opens; the toast's Dismiss answers it with exactly one
/// command. When Windows refuses the toast too, the request is held with the attention flag and appears as the full tug once
/// the full-screen app ends, and Dismiss then sends exactly one command.
/// </summary>
internal static class TugToastScenario
{
    public const string Name = "tug-toast";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, cancellation);
        var run = rig.Run;

        // ---- full-screen is not allowed: a toast, no window -------------------------------------------------------------
        rig.Notifications.CanShowFullScreen = false;
        await rig.Controller.OnTakeoverDueAsync(rig.Request("e-toast", "Design review", TimeSpan.FromSeconds(60)));
        run.Check(await rig.WaitFor(() => rig.Controller.State == TugState.Toast), "the takeover falls back to a toast: " + rig.Controller.State);
        run.Check(rig.Toasts.Shown.SequenceEqual(["e-toast"]), "the toast service was asked for the toast once: " + string.Join(", ", rig.Toasts.Shown));
        run.Check(rig.Host.WindowCount == 0, "no tug window opened");
        run.Check(!rig.Controller.NeedsAttention, "Windows accepted the toast, so nothing needs attention");
        run.Check(rig.Engine.Sent.SequenceEqual(["presented:e-toast"]), "the engine was told it was presented, and nothing else: " + string.Join(", ", rig.Engine.Sent));

        await rig.Controller.DismissAsync("e-toast");
        run.Check(rig.Controller.State == TugState.Hidden, "the toast's Dismiss ends the request: " + rig.Controller.State);
        run.Check(rig.Terminal.SequenceEqual(["dismiss:e-toast"]), "exactly one terminal command: " + string.Join(", ", rig.Terminal));

        // ---- the toast is refused too: held, then the full tug when the full-screen app ends ------------------------------
        rig.Toasts.Accept = false;
        await rig.Controller.OnTakeoverDueAsync(rig.Request("e-held", "Weekly sync", TimeSpan.FromSeconds(60)));
        run.Check(await rig.WaitFor(() => rig.Controller.NeedsAttention), "a refused toast raises the attention flag");
        run.Check(rig.Host.WindowCount == 0 && rig.Terminal.Count == 1, "still no window and no new command");

        rig.Notifications.CanShowFullScreen = true;
        await rig.Controller.OnFullScreenEndedAsync();
        run.Check(await rig.WaitFor(() => rig.Host.WindowCount == 2 && rig.Host.Primary is not null), "when the full-screen app ends, the full tug appears");
        run.Check(!rig.Controller.NeedsAttention, "the attention flag clears once the tug is up");
        await rig.Settle();
        await run.ShotAsync(rig.Host.Primary!, "tug-after-full-screen");

        run.Check(ScenarioUi.Invoke(rig.Host.Primary!, "Dismiss this reminder"), "Dismiss can be pressed");
        run.Check(await rig.WaitHiddenAsync(), "the windows close");
        run.Check(rig.Terminal.SequenceEqual(["dismiss:e-toast", "dismiss:e-held"]), "each request received exactly one command: " + string.Join(", ", rig.Terminal));
        return rig.Finish();
    }
}
#endif
