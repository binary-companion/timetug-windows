#if SELFTEST
namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-monitors</c>: a monitor unplugged while the tug is up closes only its window and the main window stays; plugged
/// back in it gets a mirror again; when the main monitor goes, the controls move to the one that remains. No crash, no extra command.
/// </summary>
internal static class TugMonitorsScenario
{
    public const string Name = "tug-monitors";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, cancellation);
        var run = rig.Run;

        run.Check(await rig.PresentAsync(rig.Request("e-monitors", "Planning", TimeSpan.FromSeconds(180))), "a takeover opens on two monitors");
        var before = rig.Host.Primary;

        rig.Monitors.Set(rig.Main);
        await rig.Controller.OnMonitorsChangedAsync();
        run.Check(await rig.WaitFor(() => rig.Host.WindowCount == 1), "unplugging the second monitor closes only its window");
        run.Check(ReferenceEquals(rig.Host.Primary, before), "the main window stays");

        rig.Monitors.Set(rig.Main, rig.Second);
        await rig.Controller.OnMonitorsChangedAsync();
        run.Check(await rig.WaitFor(() => rig.Host.WindowCount == 2), "plugging it back in opens a mirror again");

        rig.Monitors.Set(rig.Second with { IsPrimary = true });
        await rig.Controller.OnMonitorsChangedAsync();
        run.Check(await rig.WaitFor(() => rig.Host.WindowCount == 1 && rig.Host.Primary is { Plan.Monitor.Id: "selftest-2" }), "when the main monitor goes, the controls move to the remaining monitor");
        run.Check(rig.Host.Primary is not null && ScenarioUi.HasButton(rig.Host.Primary, "Dismiss this reminder") && !ReferenceEquals(rig.Host.Primary, before), "the new main window has the controls");
        await rig.Settle();
        await run.ShotAsync(rig.Host.Primary!, "moved");

        rig.Monitors.Set(rig.Main, rig.Second);
        await rig.Controller.OnMonitorsChangedAsync();
        run.Check(await rig.WaitFor(() => rig.Host.WindowCount == 2 && rig.Host.Primary is { Plan.Monitor.Id: "selftest-1" }), "restoring the layout puts the controls back on the main monitor");
        run.Check(rig.Terminal.Count == 0, "the layout changes sent the engine nothing: " + string.Join(", ", rig.Terminal));

        run.Check(ScenarioUi.Invoke(rig.Host.Primary!, "Dismiss this reminder"), "Dismiss can be pressed");
        run.Check(await rig.WaitHiddenAsync(), "the windows close");
        run.Check(rig.Terminal.SequenceEqual(["dismiss:e-monitors"]), "Dismiss sent exactly one command: " + string.Join(", ", rig.Terminal));
        return rig.Finish();
    }
}
#endif
