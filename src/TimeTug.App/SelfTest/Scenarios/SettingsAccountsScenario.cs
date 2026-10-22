#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Settings;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>settings-accounts</c>. First the real Settings window on the Accounts pane over scripted data (invented accounts, one in every
/// status the pane can show, an invented event title that must never reach a report): the statuses and who offers "Sign in again", the
/// Add buttons (one kind is not set up in this build), a deep link to one account, Sign in again, Remove with its question (cancel, then
/// confirm), the empty state, the waiting state with Cancel, a sign-in failure beside its provider, and Diagnostics over a real log file that holds
/// lines the safety net must drop. Then the real native engine in testing mode: the one recorded Google account is removed through the pane
/// and the scenario asserts that the engine asked the host's credential store to remove its secrets.
/// </summary>
internal static class SettingsAccountsScenario
{
    public const string Name = "settings-accounts";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var tug = TugRig.Create(options, cancellation);
        var run = tug.Run;
        using var settings = SettingsRig.Over(tug);
        var now = tug.Time.GetUtcNow();
        tug.Engine.Push(SettingsRig.AccountsState(now));

        // The log Copy Diagnostics reads: what the app writes, then lines it never would (the safety net must drop them).
        settings.Log.Record("info", "sync", "the engine fetched 3 calendars");
        settings.Log.Record("warning", "http", "Request to www.googleapis.com failed: HttpRequestException");
        File.AppendAllLines(settings.Log.CurrentPath,
        [
            "2026-10-21T08:00:00.000Z [info] sync: redirected to http://127.0.0.1:5000/?code=4%2F0AbCdEf&state=xyz",
            "2026-10-21T08:00:01.000Z [info] sync: refresh_token=1//0gAbCdEfGhIjKlMnOpQrStUv",
            "2026-10-21T08:00:02.000Z [info] sync: could not read Design review",
            "2026-10-21T08:00:03.000Z [info] sync: subscribed to webcal://feeds.example.test/team",
        ]);
        settings.Log.Record("info", "sync", "the engine fetched 4 calendars");

        var window = await settings.OpenAsync(SettingsPane.Accounts, large: true);
        run.Check(window is not null, "the Settings window opens on Accounts");
        if (window is null)
        {
            return tug.Finish();
        }

        run.Check(await tug.WaitFor(() => window.CurrentPage is AccountsPage { AccountCards.Count: 7 }), "the Accounts pane lists the seven accounts");
        if (window.CurrentPage is not AccountsPage page)
        {
            return tug.Finish();
        }

        // ---- every status in the Mac's words -------------------------------------------------------------------------------------
        var cards = page.AccountCards;
        run.Check(cards.Select(c => c.Account.StatusText).SequenceEqual(
            ["Connected", "Sign in again", "Calendar access is off", "Can’t reach this calendar right now", "Connecting…", "Connected", "Can’t start this account"]),
            "each account says how it is doing: " + string.Join(" | ", cards.Select(c => c.Account.StatusText)));
        run.Check(cards.Select(c => c.SignInAgain is not null).SequenceEqual([false, true, false, false, false, false, true]), "only an expired sign-in and an account that cannot start offer Sign in again");
        run.Check(cards.All(c => c.RemoveItem is not null && c.Menu is not null), "every account has its actions menu");
        run.Check(cards[5].Account.NoticeText is { } notice && notice.StartsWith("3 repeating events", StringComparison.Ordinal), "the account with repeating events the engine cannot read says so");
        run.Check(page.EmptyRowElement.Visibility == Visibility.Collapsed, "the empty row is hidden while accounts are listed");

        // ---- adding ----------------------------------------------------------------------------------------------------------------
        var microsoft = page.ProviderCardOf("microsoft");
        var google = page.ProviderCardOf("google");
        run.Check(microsoft is { Add.IsEnabled: false } && microsoft.Item.Hint.Contains("set up in this build", StringComparison.Ordinal), "a kind this build cannot sign in to is disabled and says why");
        run.Check(google is { Add.IsEnabled: true } && page.ProviderCardOf("icloud") is { Add.IsEnabled: true } && page.ProviderCardOf("caldav") is { Add.IsEnabled: true }, "the others can be added");
        run.Check(page.UnavailableNotice.IsOpen && page.UnavailableNotice.Title.Contains("missing configuration", StringComparison.Ordinal), "a notice says some kinds are unavailable in this build");
        run.Check(page.ProviderCardOf("eventkit") is null, "there is no Apple Calendar and no \"Soon\" tile");

        // ---- a deep link to one account ----------------------------------------------------------------------------------------------
        settings.Host.Show(SettingsPane.Accounts, "account:conn-2");
        run.Check(settings.Factory.Created == 1 && ReferenceEquals(settings.Window, window), "a second request focuses the open window");
        run.Check(await tug.WaitFor(() => SettingsHighlight.Hosts(page).Any(h => SettingsHighlight.GetId(h) == "account:conn-2" && SettingsHighlight.OverlayOf(h) is { Opacity: > 0 }), 2000), "the account the link names is tinted");
        await tug.Settle();
        await run.ShotAsync(window, "accounts-statuses");

        // ---- Sign in again -----------------------------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Invoke(window, "Sign in again to work@example.com"), "Sign in again is pressed on the expired account");
        run.Check(await tug.WaitFor(() => tug.Engine.Sent.Contains("reconnect:conn-2")), "the engine is asked to reconnect that account: " + string.Join(",", tug.Engine.Sent));

        // ---- Remove: the question, Cancel, then Remove ----------------------------------------------------------------------------------
        run.Check(await OpenRemoveQuestionAsync(window, tug, "family@example.com"), "Remove account asks first");
        run.Check(page.OpenDialog is { Title: "Remove family@example.com?" } d && d.Content is "Its calendars will no longer appear and their Tug and visibility choices are forgotten.", "naming the account and what is lost");
        await tug.Settle(2500);
        await run.ShotAsync(window, "accounts-remove");
        run.Check(await PressDialogAsync(window, tug, "CloseButton"), "Cancel closes the question");
        run.Check(await tug.WaitFor(() => page.OpenDialog is null), "the question is gone");
        run.Check(!tug.Engine.Sent.Any(c => c.StartsWith("remove:", StringComparison.Ordinal)), "and nothing was removed");

        run.Check(await OpenRemoveQuestionAsync(window, tug, "family@example.com"), "Remove account asks again");
        run.Check(await PressDialogAsync(window, tug, "PrimaryButton"), "Remove confirms");
        run.Check(await tug.WaitFor(() => tug.Engine.Sent.Contains("remove:conn-3")), "the engine is asked to remove that account: " + string.Join(",", tug.Engine.Sent));
        run.Check(page.AccountCards.Count == 7, "the list still shows it until the engine says it is gone");
        var without = SettingsRig.AccountsState(now);
        tug.Engine.Push(without with { Accounts = [.. without.Accounts.Where(a => a.ConnectionId != "conn-3")] });
        run.Check(await tug.WaitFor(() => page.AccountCards.Count == 6 && page.AccountCards.All(c => c.Account.ConnectionId != "conn-3")), "the engine's state takes it out of the list");

        // ---- the empty state --------------------------------------------------------------------------------------------------------------
        tug.Engine.Push(without with { Accounts = [] });
        run.Check(await tug.WaitFor(() => page.AccountCards.Count == 0 && page.EmptyRowElement.Visibility == Visibility.Visible), "with no account left the pane shows the empty row");
        await tug.Settle();
        await run.ShotAsync(window, "accounts-empty");

        // ---- waiting, and Cancel ------------------------------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Invoke(window, "Connect Google"), "Connect Google is pressed");
        run.Check(await tug.WaitFor(() => tug.Engine.Sent.Contains("add:google")), "the engine is asked to add a Google account");
        run.Check(await tug.WaitFor(() => page.WaitingPanel.Visibility == Visibility.Visible && page.WaitingLabel.Text == "Waiting for your browser…"), "the pane waits: " + page.WaitingLabel.Text);
        run.Check(page.ProviderCardOf("google") is { Add.IsEnabled: false } && page.ProviderCardOf("icloud") is { Add.IsEnabled: false }, "no other sign-in can start meanwhile");
        await tug.Settle();
        await run.ShotAsync(window, "accounts-waiting");
        run.Check(ScenarioUi.Invoke(window, "Cancel signing in"), "Cancel is pressed");
        run.Check(await tug.WaitFor(() => tug.Engine.Sent.Contains("cancel") && page.WaitingPanel.Visibility == Visibility.Collapsed), "the engine is told (account.cancel) and the pane stops waiting");

        // ---- a sign-in failure beside its provider -----------------------------------------------------------------------------------
        run.Check(ScenarioUi.Invoke(window, "Connect iCloud"), "Connect iCloud is pressed");
        await tug.WaitFor(() => page.WaitingPanel.Visibility == Visibility.Visible);
        tug.Engine.Push(Scenes.Empty(new AccountActivity(false, "Sign-in failed: access denied.", "inApp")));
        run.Check(await tug.WaitFor(() => page.ProviderCardOf("icloud") is { ErrorText.Length: > 0 }), "the failure shows beside iCloud");
        var icloud = page.ProviderCardOf("icloud")!;
        run.Check(icloud.ErrorText == "Couldn’t connect to iCloud. Check your details and try again." && icloud.DetailText == "Sign-in failed: access denied.", "in words and with the engine's detail: " + icloud.ErrorText);
        run.Check(page.ProviderCardOf("google") is { ErrorText.Length: 0 }, "and not beside the others");
        run.Check(!page.ErrorNotice.IsOpen, "and not a second time at the top");
        await tug.Settle();
        await run.ShotAsync(window, "accounts-failure");
        tug.Engine.Push(SettingsRig.AccountsState(now));
        await tug.WaitFor(() => page.AccountCards.Count == 7);

        // ---- Diagnostics -------------------------------------------------------------------------------------------------------------------
        await tug.Settle(800);
        page.ScrollArea.ChangeView(null, page.ScrollArea.ScrollableHeight, null, disableAnimation: true);
        run.Check(ScenarioUi.Invoke(window, "Copy Diagnostics"), "Copy Diagnostics is pressed");
        run.Check(await tug.WaitFor(() => settings.Clipboard.Texts.Count == 1), "the report is copied");
        var report = settings.Clipboard.Texts.LastOrDefault() ?? string.Empty;
        run.Info("the report: " + report.Replace('\r', ' ').Replace('\n', '|'));
        run.Info("real clipboard accepted the text: " + (settings.Clipboard.RealAccepted?.ToString() ?? "not tried"));
        run.Check(report.StartsWith("TimeTug diagnostics", StringComparison.Ordinal) && report.Contains("App version: 1.0.0.0", StringComparison.Ordinal), "it starts with the header");
        run.Check(report.Contains("the engine fetched 3 calendars", StringComparison.Ordinal) && report.Contains("the engine fetched 4 calendars", StringComparison.Ordinal) && report.Contains("Request to www.googleapis.com failed", StringComparison.Ordinal), "and the app's own entries");
        run.Check(!report.Contains("code=", StringComparison.Ordinal) && !report.Contains("refresh_token", StringComparison.Ordinal) && !report.Contains("Design review", StringComparison.Ordinal) && !report.Contains("webcal", StringComparison.Ordinal), "a link with a query, a token, an event title and a feed link never reach it");
        run.Check(page.DiagnosticsMessage.Text == "Copied to the clipboard.", "the pane says it was copied: " + page.DiagnosticsMessage.Text);
        await tug.Settle();
        await run.ShotAsync(window, "accounts-diagnostics");

        run.Check(ScenarioUi.Invoke(window, "Clear"), "Clear is pressed");
        run.Check(await tug.WaitFor(() => page.DiagnosticsMessage.Text == "The log was cleared."), "the pane says it was cleared");
        run.Check(Directory.Exists(settings.LogFolder) is false || Directory.EnumerateFiles(settings.LogFolder).All(f => new FileInfo(f).Length == 0), "the log is empty");
        ScenarioUi.Invoke(window, "Copy Diagnostics");
        await tug.WaitFor(() => settings.Clipboard.Texts.Count == 2);
        run.Check(settings.Clipboard.Texts.LastOrDefault() is { } again && !again.Contains("[info]", StringComparison.Ordinal) && !again.Contains("[warning]", StringComparison.Ordinal), "a report after Clear holds no entries");

        // ---- names -------------------------------------------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        run.Check(AutomationProperties.GetName(page.AccountCards[1].Menu) == "work@example.com actions", "an account's menu is named after it");
        run.Check(AutomationProperties.GetName(page.AccountCards[1].Card) == "Microsoft, work@example.com, Sign in again", "an account's card reads as provider, name and status");

        // ---- the real engine: Remove really removes the credentials ----------------------------------------------------------------
        await RemoveWithTheRealEngineAsync(options, run, cancellation);
        return tug.Finish();
    }

    /// <summary>
    /// Starts the whole app over the real native engine (testing mode; one recorded Google account whose secrets are in the scripted credential
    /// store), removes the account through the Accounts pane and asserts the engine asked the host's credential store to remove them.
    /// </summary>
    private static async Task<bool> RemoveWithTheRealEngineAsync(SelfTestOptions options, ScenarioRun run, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartAsync(options, cancellation);
        if (!rig.Ready)
        {
            Merge(run, rig);
            return false;
        }

        var window = await rig.OpenSettingsAsync(SettingsPane.Accounts);
        run.Check(window is not null, "(real engine) the Settings window opens on Accounts");
        if (window is null)
        {
            Merge(run, rig);
            return false;
        }

        run.Check(await rig.WaitFor(() => window.CurrentPage is AccountsPage { AccountCards.Count: 1 }), "(real engine) the pane lists the engine's one account");
        if (window.CurrentPage is not AccountsPage page || page.AccountCards.Count == 0)
        {
            Merge(run, rig);
            return false;
        }

        run.Info($"(real engine) the account: '{page.AccountCards[0].Account.Name}' is '{page.AccountCards[0].Account.StatusText}', {page.AccountCards[0].Account.KindName}");
        run.Check(page.AccountCards[0].Account.KindId == "google" && page.AccountCards[0].Account.StatusText == "Connected", "(real engine) it is the Google account and the engine says it is connected");
        run.Check(page.ProviderCardOf("google") is { Add.IsEnabled: true }, "(real engine) Google can be added (the rig configures it)");
        run.Check(rig.Host.Secrets.Has("conn-1"), "(real engine) the account's secrets are in the credential store");

        var name = page.AccountCards[0].Account.Name;
        run.Check(await OpenRemoveQuestionAsync(window, rig, name), "(real engine) Remove account asks first");
        run.Check(await PressDialogAsync(window, rig, "PrimaryButton"), "(real engine) Remove confirms");
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts.Count: 0 }), "(real engine) the engine removed the account from its state");
        run.Check(await rig.WaitFor(() => page.AccountCards.Count == 0 && page.EmptyRowElement.Visibility == Visibility.Visible), "(real engine) the pane shows the empty state");
        // The engine publishes the account's removal and then removes its secrets: wait for the store to be asked.
        run.Check(await rig.WaitFor(() => rig.Host.Secrets.Calls.ToArray().Contains("remove conn-1")), "(real engine) the engine asked the credential store to remove conn-1: " + string.Join(", ", rig.Host.Secrets.Calls.ToArray()));
        run.Check(await rig.WaitFor(() => !rig.Host.Secrets.Has("conn-1")), "(real engine) no secrets of the removed account are left in the store");
        run.Info("(real engine) credential calls: " + string.Join(", ", rig.Host.Secrets.Calls.ToArray()));
        Merge(run, rig);
        return true;
    }

    /// <summary>Brings the checks the engine rig recorded into the scenario's run (its screenshots are not kept).</summary>
    private static void Merge(ScenarioRun run, EngineRig rig)
    {
        var result = rig.Finish();
        foreach (var check in result.Checks)
        {
            run.Check(check.Ok, "(real engine) " + check.Name);
        }

        foreach (var note in result.Info)
        {
            run.Info("(real engine) " + note);
        }
    }

    // ---- driving the menu and the question the way a person does ---------------------------------------------------------------

    private static Task<bool> OpenRemoveQuestionAsync(SettingsWindow window, TugRig rig, string accountName) =>
        OpenRemoveQuestionAsync(window, (condition, milliseconds) => rig.WaitFor(condition, milliseconds), accountName);

    private static Task<bool> OpenRemoveQuestionAsync(SettingsWindow window, EngineRig rig, string accountName) =>
        OpenRemoveQuestionAsync(window, (condition, milliseconds) => rig.WaitFor(condition, milliseconds), accountName);

    private static async Task<bool> OpenRemoveQuestionAsync(SettingsWindow window, Func<Func<bool>, int, Task<bool>> waitFor, string accountName)
    {
        if (!ScenarioUi.Invoke(window, accountName + " actions"))
        {
            return false;
        }

        MenuFlyoutItem? item = null;
        if (!await waitFor(() => (item = FindInPopups<MenuFlyoutItem>(window, i => AutomationProperties.GetName(i) == "Remove " + accountName)) is not null, 3000))
        {
            return false;
        }

        if (FrameworkElementAutomationPeer.CreatePeerForElement(item).GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
        {
            return false;
        }

        invoke.Invoke();
        return await waitFor(() => window.CurrentPage is AccountsPage { OpenDialog: not null }, 3000);
    }

    private static Task<bool> PressDialogAsync(SettingsWindow window, TugRig rig, string buttonName) =>
        PressDialogAsync(window, (condition, milliseconds) => rig.WaitFor(condition, milliseconds), buttonName);

    private static Task<bool> PressDialogAsync(SettingsWindow window, EngineRig rig, string buttonName) =>
        PressDialogAsync(window, (condition, milliseconds) => rig.WaitFor(condition, milliseconds), buttonName);

    private static async Task<bool> PressDialogAsync(SettingsWindow window, Func<Func<bool>, int, Task<bool>> waitFor, string buttonName)
    {
        Button? button = null;
        if (!await waitFor(() => (button = FindInPopups<Button>(window, b => b.Name == buttonName && b.Visibility == Visibility.Visible)) is not null, 3000))
        {
            return false;
        }

        if (FrameworkElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
        {
            return false;
        }

        invoke.Invoke();
        return true;
    }

    private static T? FindInPopups<T>(SettingsWindow window, Func<T, bool> match)
        where T : DependencyObject
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(window.RootElement.XamlRoot))
        {
            if (popup.Child is { } child && Find(child, match) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    private static T? Find<T>(DependencyObject node, Func<T, bool> match)
        where T : DependencyObject
    {
        if (node is T typed && match(typed))
        {
            return typed;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (Find(VisualTreeHelper.GetChild(node, i), match) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }
}
#endif
