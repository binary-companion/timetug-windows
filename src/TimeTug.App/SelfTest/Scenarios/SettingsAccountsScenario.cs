#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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

        run.Check(await tug.WaitFor(() => window.CurrentPage is AccountsPage { AccountsList.Accounts.ListAccounts.Count: 7 }), "the Accounts pane lists the seven accounts");
        if (window.CurrentPage is not AccountsPage page)
        {
            return tug.Finish();
        }

        var box = page.AccountsList;
        var model = box.Accounts!;
        Task<bool> Wait(Func<bool> condition, int milliseconds) => tug.WaitFor(condition, milliseconds);

        // ---- every status in the Mac's words -------------------------------------------------------------------------------------
        var rows = model.ListAccounts;
        run.Check(rows.Select(c => c.StatusText).SequenceEqual(
            ["Connected", "Sign in again", "Calendar access is off", "Can’t reach this calendar right now", "Connecting…", "Connected", "Can’t start this account"]),
            "each account says how it is doing: " + string.Join(" | ", rows.Select(c => c.StatusText)));
        await tug.Settle(600);
        run.Check(rows.Select(c => box.RowSignInAgain(c.ConnectionId) is not null).SequenceEqual([false, true, false, false, false, false, true]), "only an expired sign-in and an account that cannot start offer Sign in again");
        run.Check(rows[5].NoticeText is { } notice && notice.StartsWith("3 repeating events", StringComparison.Ordinal), "the account with repeating events the engine cannot read says so");
        run.Check(box.EmptyLineText.Visibility == Visibility.Collapsed, "the empty line is hidden while accounts are listed");
        run.Check(box.DeviceRowPanel.Visibility == Visibility.Collapsed, "there is no pinned Windows row on scripted data");
        run.Check(AccountsListUi.ExposesListPatterns(box), "the list exposes the Selection pattern and each row the SelectionItem pattern" + ": " + AccountsListUi.ListPatternReport(box).Detail);

        // ---- adding ----------------------------------------------------------------------------------------------------------------
        var items = await AccountsListUi.OpenMenuAsync(Wait, box);
        run.Check(items.Select(i => i.Text).SequenceEqual(["Microsoft", "Google", "iCloud", "Other (CalDAV)"]), "the + menu: " + string.Join(", ", items.Select(i => i.Text)));
        run.Check(items.Count > 0 && !items[0].IsEnabled && items[0].KeyboardAcceleratorTextOverride.Contains("set up in this build", StringComparison.Ordinal), "a kind this build cannot sign in to is disabled and says why");
        run.Check(items.Skip(1).All(i => i.IsEnabled), "the others can be chosen");
        run.Check(box.UnavailableNotice.IsOpen && box.UnavailableNotice.Title.Contains("missing configuration", StringComparison.Ordinal), "a notice says some kinds are unavailable in this build");
        run.Check(items.All(i => i.Text != "Apple Calendar"), "there is no Apple Calendar and no \"Soon\" entry");
        await run.ShotAsync(window, "accounts-menu");
        await AccountsListUi.CloseMenuAsync(Wait, box);

        // ---- a deep link to one account ----------------------------------------------------------------------------------------------
        settings.Host.Show(SettingsPane.Accounts, "account:conn-2");
        run.Check(settings.Factory.Created == 1 && ReferenceEquals(settings.Window, window), "a second request focuses the open window");
        run.Check(await tug.WaitFor(() => SettingsHighlight.Hosts(page).Any(h => SettingsHighlight.GetId(h) == "account:conn-2" && SettingsHighlight.OverlayOf(h) is { Opacity: > 0 }), 2000), "the account the link names is tinted");
        await tug.Settle();
        await run.ShotAsync(window, "accounts-statuses");

        // ---- Sign in again -----------------------------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Invoke(window, "Sign in again to work@example.com"), "Sign in again is pressed on the expired account");
        run.Check(await tug.WaitFor(() => tug.Engine.Sent.Contains("reconnect:conn-2")), "the engine is asked to reconnect that account: " + string.Join(",", tug.Engine.Sent));

        // ---- Select, then - asks: Cancel, then Remove ---------------------------------------------------------------------------------
        run.Check(model.SelectedAccountId is null && !box.RemoveMinus.IsEnabled, "nothing is selected at first and - is disabled");
        run.Check(AccountsListUi.Select(box, "conn-3"), "the family account is selected through UI Automation");
        run.Check(await tug.WaitFor(() => box.RemoveMinus.IsEnabled && AutomationProperties.GetName(box.RemoveMinus) == "Remove family@example.com"), "- is enabled and named for the row: " + AutomationProperties.GetName(box.RemoveMinus));
        run.Check(ScenarioUi.Invoke(box.RemoveMinus) && await tug.WaitFor(() => box.OpenDialog is not null), "- asks first");
        run.Check(box.OpenDialog is { Title: "Remove family@example.com?" } d && d.Content is Microsoft.UI.Xaml.Controls.TextBlock { Text: "Its calendars will no longer appear and their Tug and visibility choices are forgotten." }, "naming the account and what is lost");
        await tug.Settle(2500);
        await run.ShotAsync(window, "accounts-remove");
        run.Check(box.OpenDialog is { DefaultButton: ContentDialogButton.Close }, "the default button is Cancel, not Remove");
        run.Check(await AccountsListUi.PressDialogAsync(Wait, window.RootElement.XamlRoot, "CloseButton"), "Cancel closes the question");
        run.Check(await tug.WaitFor(() => box.OpenDialog is null), "the question is gone");
        run.Check(!tug.Engine.Sent.Any(c => c.StartsWith("remove:", StringComparison.Ordinal)), "and nothing was removed");

        run.Check(ScenarioUi.Invoke(box.RemoveMinus) && await tug.WaitFor(() => box.OpenDialog is not null), "- asks again");
        run.Check(await AccountsListUi.PressDialogAsync(Wait, window.RootElement.XamlRoot, "PrimaryButton"), "Remove confirms");
        run.Check(await tug.WaitFor(() => tug.Engine.Sent.Contains("remove:conn-3")), "the engine is asked to remove that account: " + string.Join(",", tug.Engine.Sent));
        run.Check(model.ListAccounts.Count == 7, "the list still shows it until the engine says it is gone");
        var without = SettingsRig.AccountsState(now);
        tug.Engine.Push(without with { Accounts = [.. without.Accounts.Where(a => a.ConnectionId != "conn-3")] });
        run.Check(await tug.WaitFor(() => model.ListAccounts.Count == 6 && model.ListAccounts.All(c => c.ConnectionId != "conn-3")), "the engine's state takes it out of the list");
        run.Check(model.SelectedAccountId == "conn-4", "the selection moved to the row now in its place: " + model.SelectedAccountId);

        // ---- the empty state --------------------------------------------------------------------------------------------------------------
        tug.Engine.Push(without with { Accounts = [] });
        run.Check(await tug.WaitFor(() => model.ListAccounts.Count == 0 && box.EmptyLineText.Visibility == Visibility.Visible), "with no account left the pane shows the empty line");
        run.Check(box.EmptyLineText.Text == "No accounts yet. Choose + to add one.", "saying how to add one: " + box.EmptyLineText.Text);
        await tug.Settle();
        await run.ShotAsync(window, "accounts-empty");

        // ---- waiting, and Cancel ------------------------------------------------------------------------------------------------------------
        run.Check(await AccountsListUi.ChooseAsync(Wait, box, "Connect Google"), "Google is chosen from the + menu");
        run.Check(await tug.WaitFor(() => tug.Engine.Sent.Contains("add:google")), "the engine is asked to add a Google account");
        run.Check(await tug.WaitFor(() => box.WaitingPanel.Visibility == Visibility.Visible && box.WaitingLabel.Text == "Waiting for your browser…"), "the pane waits: " + box.WaitingLabel.Text);
        run.Check(!box.AddPlus.IsEnabled && !box.RemoveMinus.IsEnabled, "no other sign-in can start meanwhile: + and - are off");
        await tug.Settle();
        await run.ShotAsync(window, "accounts-waiting");
        run.Check(ScenarioUi.Invoke(window, "Cancel signing in"), "Cancel is pressed");
        run.Check(await tug.WaitFor(() => tug.Engine.Sent.Contains("cancel") && box.WaitingPanel.Visibility == Visibility.Collapsed), "the engine is told (account.cancel) and the pane stops waiting");

        // ---- a sign-in failure under the box -------------------------------------------------------------------------------------------------
        run.Check(await AccountsListUi.ChooseAsync(Wait, box, "Connect iCloud"), "iCloud is chosen");
        await tug.WaitFor(() => box.WaitingPanel.Visibility == Visibility.Visible);
        tug.Engine.Push(Scenes.Empty(new AccountActivity(false, "Sign-in failed: access denied.", "inApp")));
        run.Check(await tug.WaitFor(() => box.ErrorLine.Text.Length > 0), "the failure shows under the box");
        run.Check(box.ErrorLine.Text == "Couldn’t connect to iCloud. Check your details and try again." && box.ErrorDetailLine.Text == "Sign-in failed: access denied.", "in words and with the engine's detail: " + box.ErrorLine.Text);
        run.Check(model.ErrorText is null, "and not a second time");
        await tug.Settle();
        await run.ShotAsync(window, "accounts-failure");
        tug.Engine.Push(SettingsRig.AccountsState(now));
        await tug.WaitFor(() => model.ListAccounts.Count == 7);

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
        run.Check(AccountsListUi.RowNames(box)[1] == "Microsoft, work@example.com, Sign in again", "an account's row reads as provider, name and status: " + AccountsListUi.RowNames(box)[1]);

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

        run.Check(await rig.WaitFor(() => window.CurrentPage is AccountsPage { AccountsList.Accounts.ListAccounts.Count: 1 }), "(real engine) the pane lists the engine's one account");
        if (window.CurrentPage is not AccountsPage page || page.AccountsList.Accounts!.ListAccounts.Count == 0)
        {
            Merge(run, rig);
            return false;
        }

        var box = page.AccountsList;
        var account = box.Accounts!.ListAccounts[0];
        run.Info($"(real engine) the account: '{account.Name}' is '{account.StatusText}', {account.KindName}");
        run.Check(account.KindId == "google" && account.StatusText == "Connected", "(real engine) it is the Google account and the engine says it is connected");
        var items = await AccountsListUi.OpenMenuAsync((c, ms) => rig.WaitFor(c, ms), box);
        run.Check(items.Any(i => i.Text == "Google" && i.IsEnabled), "(real engine) Google can be added (the rig configures it)");
        await AccountsListUi.CloseMenuAsync((c, ms) => rig.WaitFor(c, ms), box);
        run.Check(rig.Host.Secrets.Has("conn-1"), "(real engine) the account's secrets are in the credential store");

        run.Check(AccountsListUi.Select(box, account.ConnectionId), "(real engine) the row is selected");
        run.Check(await rig.WaitFor(() => box.RemoveMinus.IsEnabled), "(real engine) - is enabled");
        run.Check(ScenarioUi.Invoke(box.RemoveMinus) && await rig.WaitFor(() => box.OpenDialog is not null), "(real engine) - asks first");
        run.Check(await AccountsListUi.PressDialogAsync((c, ms) => rig.WaitFor(c, ms), window.RootElement.XamlRoot, "PrimaryButton"), "(real engine) Remove confirms");
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts.Count: 0 }), "(real engine) the engine removed the account from its state");
        run.Check(await rig.WaitFor(() => box.Accounts.ListAccounts.Count == 0 && box.EmptyLineText.Visibility == Visibility.Visible), "(real engine) the pane shows the empty state");
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
}
#endif
