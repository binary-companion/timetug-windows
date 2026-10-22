#if SELFTEST
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TimeTug.App.Flyout;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using Pkg = TimeTug.Engine;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>flyout-merge</c>: the real flyout over the real app and the real native engine (testing mode, two invented Google calendars).
/// Both calendars hold a "Budget sync" that the engine merges by its rules, and a look-alike pair ("Offsite prep" and "Travel planning")
/// that it keeps apart and offers for a manual merge. Each action is driven the way a person drives it, through the card's menu, and the
/// engine's own published state is checked to echo it: "Not the same meeting" splits one member off, "Merge with…" merges the pair by hand
/// (labelled "Merged manually"), "Unmerge" splits it again. An action on a card that has gone stale shows a short message and sends the engine
/// nothing, and the scenario records what the engine itself answers to a merge command that names an event it does not have.
/// </summary>
internal static class FlyoutMergeScenario
{
    public const string Name = "flyout-merge";

    private const string Stale = "That meeting has changed. The list is up to date.";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        FlyoutWindow.IgnoreSystemDeactivation = true;
        await using var rig = await EngineRig.StartAsync(options, cancellation, "google-calendar-list-many.json", "google-events-merge-me.json", "google-events-merge-team.json");
        var run = rig.Run;
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        // The flyout is sized for the real display's scale, so a screenshot shows what a person would see.
        rig.Monitors.Set([.. TimeTug.Platform.OsState.MonitorService.Enumerate()]);
        try
        {
            await RunCoreAsync(rig, cancellation);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            run.Check(false, "the scenario stopped on an exception: " + e.GetType().Name);
            run.Info(e.ToString());
        }

        return rig.Finish();
    }

    private static async Task RunCoreAsync(EngineRig rig, CancellationToken cancellation)
    {
        var run = rig.Run;
        List<AgendaItem> Budget() => rig.Latest?.Agenda.Where(i => i.Title == "Budget sync").ToList() ?? [];
        AgendaItem? Candidates(string title) => rig.Latest?.Agenda.FirstOrDefault(i => i.Title == title);
        string Describe() => string.Join("; ", (rig.Latest?.Agenda ?? []).Select(i => $"{i.Title}[merged={i.IsMerged}/{i.MergeKind}, members={i.Members.Count}, candidates={i.Candidates.Count}]"));

        var diagnostics = new List<EngineDiagnosticInfo>();
        using var diagnosticSubscription = rig.Client.Diagnostics.Subscribe(new Observer<EngineDiagnosticInfo>(d =>
        {
            lock (diagnostics)
            {
                diagnostics.Add(d);
            }
        }));

        // ---- what the engine publishes -------------------------------------------------------------------------------------------
        run.Check(await rig.WaitFor(() => Budget().Count == 1 && Budget()[0].IsMerged && Candidates("Offsite prep")?.Candidates.Count == 1), "the engine merged the two Budget syncs and listed Offsite prep and Travel planning as a pair: " + Describe());
        var merged = Budget().Single();
        run.Check(merged.MergeKind == "rule" && merged.MergedEventCount == 2 && merged.Members.Count == 2, $"it is a rule merge of two events: {merged.MergeKind}, {merged.MergedEventCount}, {merged.Members.Count} members");
        run.Check(merged.Members.SelectMany(m => m.Calendars).Select(c => c.Title).Order().SequenceEqual(["Me", "Team Sync"]), "the members sit on Me and Team Sync: " + string.Join(", ", merged.Members.SelectMany(m => m.Calendars).Select(c => c.Title)));
        var offsite = Candidates("Offsite prep")!;
        var travel = Candidates("Travel planning");
        run.Check(offsite.Candidates[0].Title == "Travel planning" && travel?.Candidates.Count == 1 && travel.Candidates[0].EventId == offsite.EventId, "the pair is offered from both cards");

        // ---- the flyout ------------------------------------------------------------------------------------------------------------
        var host = rig.Shell.Flyout!;
        run.Check(host.Toggle() && host.IsOpen, "the flyout opens");
        var factory = (FlyoutSurfaceFactory)rig.Shell.Services.GetRequiredService<IFlyoutSurfaceFactory>();
        var window = factory.Last!.Window;
        await SelfTestRunner.WaitForFrameAsync(window, cancellation, 900);
        var model = window.ViewModel;
        run.Check(await rig.WaitFor(() => Card(window, "Budget sync") is { Row.Merge: not null }), "the merged Budget sync shows its merge line");
        var card = Card(window, "Budget sync")!;
        run.Check(card.Row.Merge!.Summary == "2 events merged" && card.Row.Merge.BadgeText is null, "a rule merge is silent: " + card.Row.Merge.Summary);
        run.Check(card.Row.Merge.CalendarsText == "Me, Team Sync", "the badge names the member calendars: " + card.Row.Merge.CalendarsText);
        run.Check(card.MenuButton.Visibility == Visibility.Visible && card.Menu is not null, "the merged card has its menu button");
        run.Check(Card(window, "Offsite prep") is { MenuButton.Visibility: Visibility.Visible } && Card(window, "Travel planning") is { MenuButton.Visibility: Visibility.Visible }, "the pair's cards have their menu buttons");
        run.Check(Card(window, "Design review") is { MenuButton.Visibility: Visibility.Collapsed }, "a card with nothing to merge has none");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        var keyboard = KeyboardReaches(window, card.MenuButton);
        run.Check(keyboard, "Tab reaches the menu button from the first control");
        await rig.Settle(700);
        await run.ShotAsync(window, "merged-and-pair");

        // ---- Not the same meeting ---------------------------------------------------------------------------------------------------
        var wasForeground = TimeTug.Platform.Tug.TugWindowChrome.IsForeground(window.Handle);
        run.Check(await OpenMenuAsync(rig, window, "Budget sync"), "the merge menu opens from its button");
        await rig.Settle(500);
        run.Info($"the flyout was the foreground window before the menu: {wasForeground}, after: {TimeTug.Platform.Tug.TugWindowChrome.IsForeground(window.Handle)}; activation log: {string.Join(" / ", window.ActivationLog.TakeLast(4))}");
        var menu = Card(window, "Budget sync")!.Menu!;
        run.Check(menu.Items.OfType<MenuFlyoutSubItem>().Select(i => i.Text).SequenceEqual(["Not the same meeting"]) && menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Unmerge"), "it offers Not the same meeting and Unmerge: " + string.Join(", ", menu.Items.Select(i => (i as MenuFlyoutItem)?.Text ?? (i as MenuFlyoutSubItem)?.Text ?? "-")));
        run.Check(!menu.Items.OfType<MenuFlyoutSubItem>().Any(i => i.Text == "Merge with…"), "and no Merge with… (this card has no candidate)");
        var separate = menu.Items.OfType<MenuFlyoutSubItem>().First();
        run.Check(separate.Items.Count == 2 && separate.Items.OfType<MenuFlyoutItem>().All(i => i.Text.StartsWith("Me · ", StringComparison.Ordinal) || i.Text.StartsWith("Team Sync · ", StringComparison.Ordinal)), "its submenu lists the members by calendar and time: " + string.Join(" | ", separate.Items.OfType<MenuFlyoutItem>().Select(i => i.Text)));
        run.Check(await rig.WaitFor(() => window.MenuOpen), "the window knows a menu is open (so a clock tick does not redraw the card)");
        Expand(separate);
        await rig.Settle(900);
        await run.ShotAsync(window, "menu-not-the-same-meeting");
        if (run.Options.HoldSeconds > 0)
        {
            // With --hold the open menu stays up for a real screen capture (the window capture above does not include popups).
            await File.WriteAllTextAsync(Path.Combine(run.Folder, "holding.txt"), "holding", cancellation);
            await Task.Delay(TimeSpan.FromSeconds(run.Options.HoldSeconds), cancellation);
        }

        var teamMember = merged.Members.First(m => m.Calendars.Any(c => c.Title == "Team Sync"));
        var teamItem = separate.Items.OfType<MenuFlyoutItem>().First(i => i.Text.StartsWith("Team Sync · ", StringComparison.Ordinal));
        run.Check(Press(teamItem), "the Team Sync member is picked");
        run.Check(await rig.WaitFor(() => !window.MenuOpen), "choosing an item closes the menu");
        run.Check(await rig.WaitFor(() => rig.Terminal.Contains($"separate:{merged.EventId}:{teamMember.MemberId}")), "the flyout sent dedup.separate with the event id and the member id only: " + string.Join(", ", rig.Terminal));
        run.Check(await rig.WaitFor(() => Budget().Count == 2 && Budget().All(i => !i.IsMerged)), "the engine republished two separate Budget syncs: " + Describe());
        run.Check(Budget().All(i => i.Candidates.Count == 1), "and now offers each as a candidate of the other");
        run.Check(await rig.WaitFor(() => !window.MenuOpen && window.EventCards.Count(c => c.Row.Title == "Budget sync") == 2), "the flyout shows two Budget sync cards");
        await rig.Settle(700);
        await run.ShotAsync(window, "after-separate");

        // ---- Merge with… ---------------------------------------------------------------------------------------------------------------
        var mine = Budget().First(i => i.Members.Count == 0 && i.Candidates.Count == 1);
        run.Check(await OpenMenuAsync(rig, window, "Budget sync", mine.EventId), "the menu of a Budget sync opens");
        menu = CardOf(window, mine.EventId)!.Menu!;
        var mergeWith = menu.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(i => i.Text == "Merge with…");
        run.Check(mergeWith is { Items.Count: 1 }, "it offers Merge with… listing the one candidate");
        if (mergeWith is not null)
        {
            Expand(mergeWith);
            await rig.Settle(900);
            await run.ShotAsync(window, "menu-merge-with");
            var other = mine.Candidates[0];
            run.Check(mergeWith.Items.OfType<MenuFlyoutItem>().First().Text.StartsWith("“Budget sync” (", StringComparison.Ordinal), "the item reads as on the Mac: " + mergeWith.Items.OfType<MenuFlyoutItem>().First().Text);
            run.Check(Press(mergeWith.Items.OfType<MenuFlyoutItem>().First()), "the candidate is picked");
            run.Check(await rig.WaitFor(() => rig.Terminal.Contains($"merge:{mine.EventId}+{other.EventId}")), "the flyout sent dedup.merge with the two event ids only: " + string.Join(", ", rig.Terminal));
        }

        run.Check(await rig.WaitFor(() => Budget().Count == 1 && Budget()[0] is { IsMerged: true, MergeKind: "userConfirmed" }), "the engine merged them as the person's choice: " + Describe());
        run.Check(await rig.WaitFor(() => Card(window, "Budget sync") is { Row.Merge.BadgeText: "Merged manually" }), "the card says Merged manually");
        run.Check(Card(window, "Budget sync")!.Row.Merge!.Summary == "Merged manually · 2 events", "with its count: " + Card(window, "Budget sync")!.Row.Merge!.Summary);
        await rig.Settle(700);
        await run.ShotAsync(window, "merged-manually");
        if (run.Options.HoldSeconds > 0)
        {
            await File.WriteAllTextAsync(Path.Combine(run.Folder, "holding2.txt"), "holding", cancellation);
            await Task.Delay(TimeSpan.FromSeconds(run.Options.HoldSeconds), cancellation);
        }

        // ---- Unmerge ------------------------------------------------------------------------------------------------------------------------
        var manual = Budget().Single();
        run.Check(await OpenMenuAsync(rig, window, "Budget sync"), "the menu of the manual merge opens");
        var unmerge = Card(window, "Budget sync")!.Menu!.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Text == "Unmerge");
        run.Check(unmerge is not null && AutomationProperties.GetName(unmerge) == "Unmerge all events in this group", "Unmerge is there, named for a screen reader");
        run.Check(unmerge is not null && Press(unmerge), "Unmerge is picked");
        run.Check(await rig.WaitFor(() => rig.Terminal.Contains("unmerge:" + manual.EventId)), "the flyout sent dedup.unmerge with the event id only: " + string.Join(", ", rig.Terminal));
        run.Check(await rig.WaitFor(() => Budget().Count == 2 && Budget().All(i => !i.IsMerged)), "the engine split them again: " + Describe());
        run.Check(await rig.WaitFor(() => window.EventCards.Count(c => c.Row.Title == "Budget sync") == 2 && !window.MenuOpen), "the flyout shows two Budget sync cards");

        // ---- the pair that was never merged: Merge with… on Offsite prep ----------------------------------------------------------------
        run.Check(await OpenMenuAsync(rig, window, "Offsite prep"), "the menu of Offsite prep opens");
        var offsiteMenu = Card(window, "Offsite prep")!.Menu!;
        run.Check(offsiteMenu.Items.Count == 1 && offsiteMenu.Items[0] is MenuFlyoutSubItem { Text: "Merge with…", Items.Count: 1 }, "it has only Merge with… (it is not a merge)");
        Expand((MenuFlyoutSubItem)offsiteMenu.Items[0]);
        await rig.Settle(900);
        await run.ShotAsync(window, "menu-pair");
        Card(window, "Offsite prep")!.Menu!.Hide();
        run.Check(await rig.WaitFor(() => !window.MenuOpen) && host.IsOpen, "closing the menu leaves the flyout open");

        // ---- a stale card ---------------------------------------------------------------------------------------------------------------
        var before = rig.Terminal.Count;
        await model.UnmergeAsync(manual.EventId);
        run.Check(model.Notice == Stale, "an Unmerge of a card that is not a merge any more says so: " + model.Notice);
        run.Check(rig.Terminal.Count == before, "and sent the engine nothing");
        run.Check(await rig.WaitFor(() => window.NoticeShown), "the flyout shows the message");
        await model.MergeAsync("google-conn-1/gone/1", "google-conn-1/also-gone/2");
        run.Check(model.Notice == Stale && rig.Terminal.Count == before, "a Merge on an event that is not there is handled the same way");
        await rig.Settle(700);
        await run.ShotAsync(window, "stale-message");
        model.DismissNotice();
        run.Check(await rig.WaitFor(() => !window.NoticeShown), "the message can be dismissed");

        // ---- what the engine itself answers to a merge command it cannot resolve ---------------------------------------------------
        await rig.Client.MergeEventsAsync("google-conn-1/gone/1", "google-conn-1/also-gone/2", "probe-merge");
        await rig.Client.UnmergeEventAsync("google-conn-1/gone/1", "probe-unmerge");
        await rig.Client.SeparateMembersAsync(offsite.EventId, ["no|1|2"], "probe-separate");
        run.Check(await rig.WaitFor(() => Answered(diagnostics, "probe-merge") && Answered(diagnostics, "probe-unmerge") && Answered(diagnostics, "probe-separate")), "the engine answers each command it cannot resolve with a diagnostic that echoes its command id: " + string.Join("; ", Snapshot(diagnostics).Select(d => d.CommandId + "=" + d.Message)));
        foreach (var d in Snapshot(diagnostics).Where(d => d.CommandId is { } id && id.StartsWith("probe-", StringComparison.Ordinal)))
        {
            run.Info($"engine refusal for {d.CommandId}: {d.Level} {d.Category} '{d.Message}'");
        }

        host.Close();
    }

    // ---- the flyout's cards ------------------------------------------------------------------------------------------------------------

    private static EventCard? Card(FlyoutWindow window, string title) => window.EventCards.FirstOrDefault(c => c.Row.Title == title);

    private static EventCard? CardOf(FlyoutWindow window, string eventId) => window.EventCards.FirstOrDefault(c => c.Row.EventId == eventId);

    private static bool Answered(List<EngineDiagnosticInfo> diagnostics, string id) => Snapshot(diagnostics).Any(d => d.CommandId == id);

    private static List<EngineDiagnosticInfo> Snapshot(List<EngineDiagnosticInfo> diagnostics)
    {
        lock (diagnostics)
        {
            return [.. diagnostics];
        }
    }

    // ---- driving a menu the way UI Automation does ------------------------------------------------------------------------------------

    private static async Task<bool> OpenMenuAsync(EngineRig rig, FlyoutWindow window, string title, string? eventId = null)
    {
        var card = eventId is null ? Card(window, title) : CardOf(window, eventId);
        if (card is null || !ScenarioUi.Invoke(card.MenuButton))
        {
            return false;
        }

        return await rig.WaitFor(() => card.Menu is { IsOpen: true });
    }

    private static void Expand(MenuFlyoutSubItem item)
    {
        if (FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand)
        {
            expand.Expand();
        }
    }

    private static bool Press(MenuFlyoutItem item)
    {
        if (FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
        {
            return false;
        }

        invoke.Invoke();
        return true;
    }

    /// <summary>True when moving the keyboard forward from the first focusable control gets to the button.</summary>
    private static bool KeyboardReaches(FlyoutWindow window, Button target)
    {
        var root = window.Content.XamlRoot;
        var current = FocusManager.FindFirstFocusableElement(window.Content) as DependencyObject;
        for (var i = 0; i < 40 && current is not null; i++)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            current = FocusManager.FindNextElement(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = window.Content });
            if (current is not null && root is not null)
            {
                _ = (current as Control)?.Focus(FocusState.Keyboard);
            }
        }

        return false;
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => onNext(value);
    }
}
#endif
