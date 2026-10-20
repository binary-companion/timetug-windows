#if SELFTEST
using System.Text.RegularExpressions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-basic</c>: a takeover with a link and attendees on two monitors becomes one borderless topmost window per
/// monitor, each covering its whole monitor, the main one with the hero, the live countdown, the title, the details line and Join,
/// Snooze and Dismiss, the other a mirror without controls. Every control has an automation name and Narrator was told about the tug.
/// Dismiss ends it with exactly one command to the engine.
/// </summary>
internal static partial class TugBasicScenario
{
    public const string Name = "tug-basic";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, cancellation);
        var run = rig.Run;
        var host = rig.Host;

        run.Check(await rig.PresentAsync(rig.Request("e-a", "Quarterly planning review with the platform and growth teams", TimeSpan.FromSeconds(245))), "a due takeover opens one window per monitor");
        var primary = host.Primary!;
        var mirror = host.Mirrors.Count > 0 ? host.Mirrors[0] : null;
        run.Check(mirror is not null && host.WindowCount == 2, "there is one primary window and one mirror");
        run.Check(rig.Controller.State is TugState.Shown or TugState.ShownWithoutFocus, "the controller is showing the tug");
        run.Check(rig.Engine.Sent.Contains("presented:e-a"), "takeover.presented was reported");
        await rig.Settle();

        if (mirror is not null)
        {
            run.Check(Covers(primary.AppWindow, rig.Main), $"the primary window covers its whole monitor ({Describe(primary.AppWindow)} for {Describe(rig.Main)})");
            run.Check(Covers(mirror.AppWindow, rig.Second), $"the mirror covers its whole monitor ({Describe(mirror.AppWindow)} for {Describe(rig.Second)})");
            run.Check(mirror.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true, HasBorder: false, HasTitleBar: false }, "the mirror is borderless and topmost");
        }

        run.Check(primary.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true, HasBorder: false, HasTitleBar: false }, "the primary is borderless and topmost");
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(primary);
        var client = TugWindowChrome.ClientSize(handle);
        run.Check(client == (rig.Main.Width, rig.Main.Height), $"the window has no frame: its drawable area is {client.Width}x{client.Height} for a {rig.Main.Width}x{rig.Main.Height} monitor");
        run.Info("primary " + TugWindowChrome.Describe(handle));
        run.Check(!primary.AppWindow.IsShownInSwitchers, "the tug has no taskbar button");
        run.Check(primary.Announcements.SequenceEqual([primary.ViewModel.Announcement]) && primary.ViewModel.Announcement.Contains("starts in", StringComparison.Ordinal), "Narrator was told about the tug through a UI Automation notification: " + primary.ViewModel.Announcement);

        var countdown = ScenarioUi.FindNamed<TextBlock>(primary, "CountdownValue");
        run.Check(countdown is not null && CountdownPattern().IsMatch(countdown.Text), "the live countdown reads like 4:03: " + countdown?.Text);
        var firstReading = countdown?.Text;
        await rig.Settle(2300);
        run.Check(countdown is not null && countdown.Text != firstReading && CountdownPattern().IsMatch(countdown.Text), $"the countdown moves with the clock ({firstReading} then {countdown?.Text})");
        run.Check(ScenarioUi.FindNamed<TextBlock>(primary, "TitleText") is { MaxLines: 2 }, "the title is limited to two lines");
        var details = ScenarioUi.FindNamed<TextBlock>(primary, "DetailsText")?.Text;
        run.Check(details is not null && details.Contains("· Work · 6 people", StringComparison.Ordinal), "the details line has the calendar and the people: " + details);
        run.Check(ScenarioUi.HasButton(primary, "Join meeting") && ScenarioUi.HasButton(primary, "Dismiss this reminder") && ScenarioUi.HasButton(primary, "Snooze this reminder"), "Join, Snooze and Dismiss are there");
        run.Check(primary.ViewModel.SnoozeChoices.Select(c => c.Title).SequenceEqual(["1 minute", "5 minutes", "10 minutes"]), "the Snooze menu offers the request's own options");
        run.Check(ScenarioUi.Unnamed(primary).Count == 0, "every control on the primary has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(primary)));
        if (mirror is not null)
        {
            run.Check(ScenarioUi.Unnamed(mirror).Count == 0, "every control on the mirror has an automation name");
            run.Check(!ScenarioUi.HasButton(mirror, "Join meeting"), "the mirror has no controls");
        }

        await run.ShotAsync(primary, "primary");
        if (mirror is not null)
        {
            await run.ShotAsync(mirror, "mirror");
        }

        await rig.HoldAsync();
        run.Check(ScenarioUi.Invoke(primary, "Dismiss this reminder"), "the Dismiss button can be invoked");
        run.Check(await rig.WaitHiddenAsync(), "the windows close after Dismiss");
        run.Check(rig.Terminal.SequenceEqual(["dismiss:e-a"]), "the engine received exactly one command: " + string.Join(", ", rig.Terminal));
        return rig.Finish();
    }

    private static bool Covers(AppWindow window, MonitorInfo monitor) =>
        window.Position.X == monitor.X && window.Position.Y == monitor.Y && window.Size.Width == monitor.Width && window.Size.Height == monitor.Height;

    private static string Describe(AppWindow window) => $"{window.Size.Width}x{window.Size.Height} at {window.Position.X},{window.Position.Y}";

    private static string Describe(MonitorInfo monitor) => $"{monitor.Width}x{monitor.Height} at {monitor.X},{monitor.Y}";

    [GeneratedRegex(@"^\d+:\d\d$")]
    private static partial Regex CountdownPattern();
}
#endif
