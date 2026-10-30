#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using TimeTug.App.Flyout;
using TimeTug.Platform.Flyout;
using TimeTug.Presentation.Flyout;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>flyout-day</c>: a scripted day with a holiday (all day), a finished meeting, the meeting in progress, the next meeting,
/// a later one and a merged one. Each card has its kind, colour bar, badge and Join button where the Mac has them; the header stays
/// put while a longer day scrolls inside a window capped at 520 DIP; Tab goes from one Join button to the next; Join opens the link
/// and the flyout gets out of the way; a link Windows cannot open leaves the card showing the link with a Copy button.
/// </summary>
internal static class FlyoutDayScenario
{
    public const string Name = "flyout-day";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = FlyoutRig.Create(options, cancellation);
        var run = rig.Run;
        rig.Tug.Engine.Push(rig.State(rig.Day()));

        var window = await rig.OpenAsync();
        run.Check(window is not null, "the flyout opens");
        if (window is null)
        {
            return rig.Tug.Finish();
        }

        var model = window.ViewModel;
        run.Check(model.Summary == "Oct 16 · 4 meetings left", "the header counts the meetings left: " + model.Summary);
        var cards = window.EventCards;
        run.Check(cards.Count == 6, "six cards: " + cards.Count);
        run.Check(cards.Select(c => c.Row.Kind).SequenceEqual([FlyoutRowKind.AllDay, FlyoutRowKind.Past, FlyoutRowKind.Current, FlyoutRowKind.Next, FlyoutRowKind.Upcoming, FlyoutRowKind.Upcoming]), "kinds: all day, past, current, next, upcoming, upcoming");
        run.Check(cards[4].Row.CalendarColorHex == "#FBFBFB" && cards[4].ColorBar.BorderThickness.Left == 1, "a calendar colour as light as the card gets a one pixel edge on its bar");
        run.Check(cards.Where((_, i) => i != 4).All(c => c.ColorBar.BorderThickness.Left == 0), "the other bars, which show against the card, have none");
        run.Check(cards[0].Row.TimeText == "All day", "the holiday says All day");
        run.Check(cards[2].Row.BadgeText == "Now" && cards[2].Row.Progress is > 0 and < 1, "the meeting in progress has a Now pill and progress");
        run.Check(cards[3].Row.BadgeText is { Length: > 0 } countdown && countdown.StartsWith("in ", StringComparison.Ordinal), "the next meeting has a countdown pill: " + cards[3].Row.BadgeText);
        run.Check(cards[3].Row.AttendeeText == "8 others", "attendees are counted: " + cards[3].Row.AttendeeText);
        run.Check(cards[5].Row.MergeSummary == "Merged with Apple Intelligence · 3 events", "the merged item says so: " + cards[5].Row.MergeSummary);
        run.Check(cards.Select(c => c.Join is not null).SequenceEqual([false, false, true, true, false, false]), "only the meeting in progress and the next one have Join");
        run.Check(ScenarioUi.HasButton(window, "Join Design review") && ScenarioUi.HasButton(window, "Join Quarterly planning with the platform and growth teams"), "the Join buttons are named for their meeting");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        run.Check(cards.All(c => AutomationProperties.GetName(c).Length > 0), "every card is named for a screen reader");
        run.Check(AutomationProperties.GetName(cards[2]) is { } spoken && spoken.StartsWith("Design review, ", StringComparison.Ordinal) && spoken.Contains("in progress", StringComparison.Ordinal) && spoken.EndsWith("Work calendar", StringComparison.Ordinal), "a card's spoken text: " + AutomationProperties.GetName(cards[2]));
        await rig.Tug.Settle(600);
        await run.ShotAsync(window, "day");
        await rig.Tug.HoldAsync();

        // ---- keyboard: the first control has the keyboard, Tab goes on to the next Join, Enter presses it -----------------------
        if (window.EverActive)
        {
            var focused = FocusManager.GetFocusedElement(window.Content.XamlRoot);
            run.Check(ReferenceEquals(focused, cards[2].Join), "the keyboard starts on the first Join button");
            run.Check(FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = window.Content }) && ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), cards[3].Join), "Tab moves to the next Join button");
        }
        else
        {
            run.Info("the flyout never became the active window on this machine; the focus checks were skipped");
        }

        // ---- Join: the link opens and the flyout closes -------------------------------------------------------------------------
        run.Check(ScenarioUi.Invoke(window, "Join Design review"), "Join is pressed");
        run.Check(await rig.Tug.WaitFor(() => !rig.Host.IsOpen), "after a Join that worked the flyout closes");
        run.Check(rig.Tug.Launcher.Opened.SequenceEqual(["https://zoom.us/j/123456789"]), "Windows was asked to open the link once: " + string.Join(", ", rig.Tug.Launcher.Opened));
        run.Check(rig.Tug.Terminal.Count == 0, "Join sent the engine no command: " + string.Join(", ", rig.Tug.Terminal));
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(100), cancellation);

        // ---- a link that cannot be opened ----------------------------------------------------------------------------------------
        rig.Tug.Launcher.Succeeds = false;
        window = await rig.OpenAsync();
        run.Check(window is not null && ScenarioUi.Invoke(window, "Join Quarterly planning with the platform and growth teams"), "Join is pressed again, and fails");
        run.Check(await rig.Tug.WaitFor(() => rig.Window?.EventCards.Any(c => c.Row.HasJoinFailed) == true), "the card shows the link instead of closing");
        window = rig.Window;
        run.Check(rig.Host.IsOpen && window is not null, "the flyout stays open");
        if (window?.EventCards.FirstOrDefault(c => c.Row.HasJoinFailed) is { } failed)
        {
            run.Check(failed.LinkText.Text == "https://meet.google.com/abc-defg-hij", "the link is in a box to copy");
            run.Check(ScenarioUi.HasButton(window, "Copy the meeting link") && ScenarioUi.Invoke(window, "Copy the meeting link"), "Copy can be pressed");
            run.Check(failed.CopiedShown, "the card says the link was copied");
            await rig.Tug.Settle(1500);
            await run.ShotAsync(window, "join-failed");
        }

        rig.Host.Close();
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(100), cancellation);
        rig.Tug.Launcher.Succeeds = true;

        // ---- a long day scrolls under a fixed header -----------------------------------------------------------------------------
        rig.Tug.Engine.Push(rig.State(rig.LongDay()));
        window = await rig.OpenAsync();
        run.Check(window is not null && window.EventCards.Count == 14, "a long day has all its cards");
        if (window is not null)
        {
            var outer = FlyoutWindowChrome.OuterBounds(window.Handle);
            var scale = FlyoutWindowChrome.DpiOf(window.Handle) / 96.0;
            run.Check(outer is { } o && Math.Abs(o.Height - Math.Round(520 * scale)) <= 2, $"the window is capped at 520 DIP ({outer?.Height} px at scale {scale})");
            run.Check(window.List.ScrollableHeight > 0, "the list scrolls");
            await rig.Tug.Settle(600);
            var headerBefore = window.HeaderElement.TransformToVisual(window.RootElement).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            window.List.ChangeView(null, window.List.ScrollableHeight, null, disableAnimation: true);
            await rig.Tug.Settle(400);
            var headerAfter = window.HeaderElement.TransformToVisual(window.RootElement).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            run.Check(Math.Abs(headerBefore - headerAfter) < 1, $"the header stays put while the list scrolls ({headerBefore} then {headerAfter})");
            await run.ShotAsync(window, "scrolled");
        }

        rig.Host.Close();
        return rig.Tug.Finish();
    }
}
#endif
