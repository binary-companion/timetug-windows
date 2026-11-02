#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using TimeTug.App.Controls;
using TimeTug.App.Settings;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using Windows.System;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>accounts-list</c>: the one accounts control in the real Settings window over a scripted engine and a scripted Windows listing. Selection through UI
/// Automation, the "+" menu entries and their order, "-" and Delete with the question, where selection and keyboard focus land after a removal (the next row, the
/// previous one, then "+"), the empty line, the busy state (+ and - off), the error and the "Use your browser instead" link under the box, a Windows listing that
/// answers late without moving focus or selection, a Windows-signed-in Microsoft account as an ordinary row with "Sign in again", and the service logos with their
/// names (Google, Microsoft; a generic glyph for the rest). There is no pinned row. Every control is named and the list exposes the Selection patterns.
/// </summary>
internal static class AccountsListScenario
{
    public const string Name = "accounts-list";

    private const string WindowsEmail = "windows-user@example.test";

    private static readonly AccountKindInfo Brokered = new("microsoft-brokered", "Microsoft", true, "oauth");

    private static AppState State(DateTimeOffset now, params AccountInfo[] accounts) => SettingsRig.AccountsState(now) with
    {
        Accounts = accounts,
        AvailableKinds = [new("google", "Google", true, "oauth"), new("microsoft", "Microsoft", true, "oauth"), Brokered, new("icloud", "iCloud", true, "password"), new("caldav", "Other (CalDAV)", true, "password")],
    };

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var tug = TugRig.Create(options, cancellation);
        var run = tug.Run;
        using var settings = SettingsRig.Over(tug);
        var now = tug.Time.GetUtcNow();
        AccountInfo[] three =
        [
            new("a1", "google", "one@example.com", "ok", 3),
            new("a2", "icloud", "two@example.com", "ok", 1),
            new("a3", "caldav", "three@example.com", "authExpired", 2),
        ];
        tug.Engine.Push(State(now, three));

        var window = await settings.OpenAsync(SettingsPane.Accounts, large: true);
        run.Check(window is not null, "the Settings window opens on Accounts");
        if (window?.CurrentPage is not AccountsPage page)
        {
            return tug.Finish();
        }

        var box = page.AccountsList;
        var model = box.Accounts!;
        var xamlRoot = window.RootElement.XamlRoot;
        Task<bool> Wait(Func<bool> condition, int milliseconds) => tug.WaitFor(condition, milliseconds);
        bool FocusIsOn(UIElement? element) => element is not null && ReferenceEquals(FocusManager.GetFocusedElement(xamlRoot), element);

        // ---- the list ------------------------------------------------------------------------------------------------------------------------
        run.Check(await Wait(() => model.ListAccounts.Count == 3 && AccountsListUi.RowNames(box).All(n => n != "(not realized)"), 5000), "the three accounts are listed");
        run.Check(await Wait(() => AccountsListUi.RowLogo(box, "a1") is { Name: "Google logo", Drawn: true }, 4000), "the Google row shows the Google logo, named for a screen reader: " + AccountsListUi.RowLogo(box, "a1"));
        run.Check(AccountsListUi.RowLogo(box, "a2") is null && AccountsListUi.RowLogo(box, "a3") is null, "the iCloud and CalDAV rows keep their generic glyphs (they have no vendor logo)");
        run.Check(model.SelectedAccountId is null && !box.RemoveMinus.IsEnabled && AutomationProperties.GetName(box.RemoveMinus) == "Remove account", "nothing is selected at first; - is disabled and named Remove account");
        run.Check(AccountsListUi.RowNames(box).SequenceEqual(["Google, one@example.com, Connected", "iCloud, two@example.com, Connected", "Other (CalDAV), three@example.com, Sign in again"]), "rows read provider, name and status in the engine's order: " + string.Join(" | ", AccountsListUi.RowNames(box)));
        run.Check(AccountsListUi.ExposesListPatterns(box), "the list exposes the Selection pattern and each row the SelectionItem pattern" + ": " + AccountsListUi.ListPatternReport(box).Detail);
        run.Check(AutomationProperties.GetName(box.Rows) == "Connected accounts", "the list is named Connected accounts");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await tug.Settle();
        await run.ShotAsync(window, "list");

        // ---- selection: stable under the engine's frequent publications ----------------------------------------------------------------------------
        run.Check(AccountsListUi.Select(box, "a2"), "the second row is selected through UI Automation");
        run.Check(await Wait(() => model.SelectedAccountId == "a2" && box.RemoveMinus.IsEnabled && AutomationProperties.GetName(box.RemoveMinus) == "Remove two@example.com", 3000), "- is enabled and names the row: " + AutomationProperties.GetName(box.RemoveMinus));
        var rowsBefore = model.ListAccounts.ToList();
        for (var i = 0; i < 5; i++)
        {
            tug.Engine.Push(State(now, three));
        }

        await tug.Settle(600);
        run.Check(model.SelectedAccountId == "a2" && model.ListAccounts.SequenceEqual(rowsBefore) && model.ListAccounts.Zip(rowsBefore).All(p => ReferenceEquals(p.First, p.Second)), "five identical publications keep the rows and the selection");

        // ---- Tab from a selected row reaches its own Sign in again button ----------------------------------------------------------------------------
        run.Check(AccountsListUi.Select(box, "a3"), "the third row (which needs the person) is selected");
        AccountsListUi.Container(box, "a3")?.Focus(FocusState.Programmatic);
        var moved = FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = xamlRoot.Content });
        var focusedAfterTab = FocusManager.GetFocusedElement(xamlRoot) as FrameworkElement;
        run.Info("after Tab from the third row: " + AccountsListUi.FocusedName(xamlRoot) + " (moved: " + moved + ")");
        run.Check(moved && ReferenceEquals(focusedAfterTab, box.RowSignInAgain("a3")), "Tab from the selected row reaches its own Sign in again button: " + AccountsListUi.FocusedName(xamlRoot));
        AccountsListUi.Select(box, "a2");
        await tug.Settle(300);

        // ---- the + menu --------------------------------------------------------------------------------------------------------------------------------
        var items = await AccountsListUi.OpenMenuAsync(Wait, box);
        run.Check(items.Select(i => i.Text).SequenceEqual(["Microsoft", "Google", "iCloud", "Other (CalDAV)"]), "the + menu lists Microsoft, Google, iCloud, then Other (CalDAV) last: " + string.Join(", ", items.Select(i => i.Text)));
        run.Check(items.All(i => i.IsEnabled && AutomationProperties.GetName(i).StartsWith("Connect ", StringComparison.Ordinal)), "each entry is enabled and named");
        await run.ShotAsync(window, "menu");
        await AccountsListUi.CloseMenuAsync(Wait, box);

        // ---- Delete asks; Cancel is the default; the engine's state takes the row away ---------------------------------------------------------------
        run.Check(box.HandleKey(VirtualKey.Delete) && await Wait(() => box.OpenDialog is not null, 3000), "Delete on the list asks the question");
        run.Check(box.OpenDialog is { Title: "Remove two@example.com?", DefaultButton: Microsoft.UI.Xaml.Controls.ContentDialogButton.Close }, "naming the account, and Cancel is the default button");
        run.Check(await AccountsListUi.PressDialogAsync(Wait, xamlRoot, "CloseButton") && await Wait(() => box.OpenDialog is null, 3000), "Cancel closes the question");
        run.Check(!tug.Engine.Sent.Any(c => c.StartsWith("remove:", StringComparison.Ordinal)) && model.SelectedAccountId == "a2", "nothing was removed and the selection is kept");

        run.Check(box.HandleKey(VirtualKey.Delete) && await Wait(() => box.OpenDialog is not null, 3000), "Delete asks again");
        run.Check(await AccountsListUi.PressDialogAsync(Wait, xamlRoot, "PrimaryButton"), "Remove confirms");
        run.Check(await Wait(() => tug.Engine.Sent.Contains("remove:a2"), 3000), "the engine is asked to remove that account");
        run.Check(model.ListAccounts.Count == 3, "the row stays until the engine's state says it is gone");
        tug.Engine.Push(State(now, three[0], three[2]));
        run.Check(await Wait(() => model.ListAccounts.Count == 2 && model.SelectedAccountId == "a3", 4000), "the selection moved to the row now in its place: " + model.SelectedAccountId);
        run.Check(await Wait(() => FocusIsOn(AccountsListUi.Container(box, "a3")), 4000), "and keyboard focus is on it: " + AccountsListUi.FocusedName(xamlRoot));

        // The last row removed: selection and focus go to the previous one.
        run.Check(box.HandleKey(VirtualKey.Delete) && await Wait(() => box.OpenDialog is not null, 3000), "Delete on the last row asks");
        run.Check(await AccountsListUi.PressDialogAsync(Wait, xamlRoot, "PrimaryButton"), "Remove confirms");
        run.Check(await Wait(() => tug.Engine.Sent.Contains("remove:a3"), 3000), "the engine is asked to remove it");
        tug.Engine.Push(State(now, three[0]));
        run.Check(await Wait(() => model.SelectedAccountId == "a1" && FocusIsOn(AccountsListUi.Container(box, "a1")), 4000), "selection and focus moved to the previous row: " + model.SelectedAccountId + ", " + AccountsListUi.FocusedName(xamlRoot));

        // The only row removed: focus goes to +.
        run.Check(box.HandleKey(VirtualKey.Delete) && await Wait(() => box.OpenDialog is not null, 3000), "Delete on the only row asks");
        run.Check(await AccountsListUi.PressDialogAsync(Wait, xamlRoot, "PrimaryButton"), "Remove confirms");
        run.Check(await Wait(() => tug.Engine.Sent.Contains("remove:a1"), 3000), "the engine is asked to remove it");
        tug.Engine.Push(State(now));
        run.Check(await Wait(() => model.SelectedAccountId is null && FocusIsOn(box.AddPlus), 4000), "nothing is selected and focus is on +: " + model.SelectedAccountId + ", " + AccountsListUi.FocusedName(xamlRoot));

        // ---- empty, and Delete with nothing selected ------------------------------------------------------------------------------------------------
        run.Check(box.EmptyLineText.Visibility == Visibility.Visible && box.EmptyLineText.Text == "No accounts yet. Choose + to add one.", "the empty box says how to add one: " + box.EmptyLineText.Text);
        run.Check(box.HandleKey(VirtualKey.Delete) && box.OpenDialog is null && model.PendingRemoval is null, "Delete with nothing selected does nothing");
        await tug.Settle();
        await run.ShotAsync(window, "empty");

        // ---- a Windows listing that arrives late changes nothing on screen -------------------------------------------------------------------------
        tug.Engine.Push(State(now, three));
        await Wait(() => model.ListAccounts.Count == 3, 3000);
        var gate = new TaskCompletionSource<WindowsAccountListing>();
        var source = new ScriptedWindowsAccountSource(new WindowsAccountListing(WindowsListingKind.Unavailable)) { Gate = gate };
        run.Check(await Wait(() => AccountsListUi.Select(box, "a1") && model.SelectedAccountId == "a1", 4000), "the first row is selected");
        box.AddPlus.Focus(FocusState.Programmatic);
        model.WindowsAccounts = source;
        await tug.Settle(500);
        run.Check(model.ListingPending && source.Asked == 1, "the listing was asked for and has not answered");
        run.Check(box.AddPlus.IsEnabled, "and + is usable meanwhile");
        gate.SetResult(new WindowsAccountListing(WindowsListingKind.OneNamed, WindowsEmail));
        run.Check(await Wait(() => !model.ListingPending && model.WindowsAccountOffered, 4000), "the answer arrives and the Windows account is offered to the first-run Continue");
        run.Check(FocusIsOn(box.AddPlus) && model.SelectedAccountId == "a1" && model.ListAccounts.Count == 3, "without moving focus or the selection, and no row appears: " + model.SelectedAccountId + ", " + AccountsListUi.FocusedName(xamlRoot));
        await tug.Settle();

        // ---- busy: + and - are off, with the waiting row ----------------------------------------------------------------------------------
        run.Check(await AccountsListUi.ChooseAsync(Wait, box, "Connect Google"), "Google is chosen");
        tug.Engine.Push(State(now, three) with { Activity = new AccountActivity(true, null, "browser") });   // the scripted engine answers an add with an empty state; keep the accounts
        run.Check(await Wait(() => box.WaitingPanel.Visibility == Visibility.Visible && box.WaitingLabel.Text == "Waiting for your browser…", 4000), "the waiting row shows: " + box.WaitingLabel.Text);
        run.Check(!box.AddPlus.IsEnabled && !box.RemoveMinus.IsEnabled, "+ and - are disabled");
        run.Check(await Wait(() => box.RowSignInAgain("a3") is { IsEnabled: false }, 4000), "and so is the row's Sign in again");
        await run.ShotAsync(window, "busy");
        run.Check(ScenarioUi.Invoke(window, "Cancel signing in"), "Cancel is pressed");
        tug.Engine.Push(State(now, three));
        run.Check(await Wait(() => tug.Engine.Sent.Contains("cancel") && box.WaitingPanel.Visibility == Visibility.Collapsed && box.AddPlus.IsEnabled, 4000), "the wait ends and + is usable again");

        // ---- a failed Windows sign-in: the error under the box and the browser link ----------------------------------------------------------------
        await model.ConnectBrokeredAsync(BrokerIntent.WindowsAccount);
        tug.Engine.Push(State(now, three) with { Activity = new AccountActivity(true, null, "browser") });
        await Wait(() => model.IsBusy, 3000);
        tug.Engine.Push(State(now, three) with { Activity = new AccountActivity(false, "Sign-in failed: Windows sign-in is not available.", "browser") });
        run.Check(await Wait(() => box.BrowserFallbackLink.Visibility == Visibility.Visible, 4000), "\"Use your browser instead\" is offered");
        run.Check(box.ErrorLine.Text == "Windows sign-in isn't available here" && box.ErrorDetailLine.Visibility == Visibility.Collapsed, "with the reason, once: " + box.ErrorLine.Text);
        run.Check(box.UnavailableNotice.IsOpen is false, "and no configuration warning: every kind is set up");
        await tug.Settle();
        await run.ShotAsync(window, "fallback");
        run.Check(ScenarioUi.Invoke(window, "Use your browser to sign in to Microsoft instead"), "the link is pressed");
        run.Check(await Wait(() => tug.Engine.Sent.Contains("add:microsoft") && box.BrowserFallbackLink.Visibility == Visibility.Collapsed, 4000), "it starts the browser sign-in for Microsoft and goes away");
        tug.Engine.Push(State(now, three) with { Activity = new AccountActivity(true, null, "browser") });
        await model.CancelAsync();
        tug.Engine.Push(State(now, three));
        await Wait(() => !model.IsBusy, 3000);

        // ---- the same mailbox through the browser: an ordinary Microsoft row, and nothing is offered to Continue -------------------------------------------
        tug.Engine.Push(State(now, new AccountInfo("m1", "microsoft", WindowsEmail, "ok", 2)));
        run.Check(await Wait(() => model.ListAccounts.Count == 1 && model.ListAccounts[0].ConnectionId == "m1" && !model.WindowsAccountOffered, 4000), "the browser account is an ordinary row and the same mailbox is not offered again");
        run.Check(await Wait(() => AccountsListUi.RowLogo(box, "m1") is { Name: "Microsoft logo", Drawn: true }, 4000), "with the Microsoft logo: " + AccountsListUi.RowLogo(box, "m1"));
        await run.ShotAsync(window, "browser-microsoft");

        // ---- the Windows account connected: an ordinary Microsoft row, with Sign in again in the row -----------------------------------------------------
        tug.Engine.Push(State(now, new AccountInfo("w1", "microsoft-brokered", WindowsEmail, "authExpired", 2)));
        run.Check(await Wait(() => model.ListAccounts is [{ ConnectionId: "w1" }], 4000), "the connected Windows account is a list row");
        run.Check(await Wait(() => AccountsListUi.RowNames(box).SequenceEqual(["Microsoft, " + WindowsEmail + ", Sign in again"]), 4000), "it reads like any Microsoft row, with no tag: " + string.Join(" | ", AccountsListUi.RowNames(box)));
        run.Check(await Wait(() => AccountsListUi.RowLogo(box, "w1") is { Name: "Microsoft logo", Drawn: true }, 4000), "with the same Microsoft logo");
        run.Check(await Wait(() => box.RowSignInAgain("w1") is { IsEnabled: true }, 4000), "and Sign in again is a button in the row");
        run.Check(ScenarioUi.Invoke(box.RowSignInAgain("w1")!), "Sign in again is pressed");
        run.Check(await Wait(() => tug.Engine.Sent.Contains("reconnect:w1"), 3000), "the engine is asked to reconnect the Windows account: " + string.Join(",", tug.Engine.Sent));
        await tug.Settle();
        await run.ShotAsync(window, "windows-account-row");

        // ---- the logos in the dark theme: the same white tile, the same unmodified marks ----------------------------------------------------------------
        window.RootElement.RequestedTheme = ElementTheme.Dark;
        await tug.Settle(600);
        run.Check(window.RootElement.ActualTheme == ElementTheme.Dark, "the window is dark");
        run.Check(await Wait(() => AccountsListUi.RowLogo(box, "w1") is { Name: "Microsoft logo", Drawn: true }, 4000), "the Microsoft logo is still drawn and named");
        await run.ShotAsync(window, "dark");
        window.RootElement.RequestedTheme = ElementTheme.Default;
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));

        return tug.Finish();
    }
}
#endif
