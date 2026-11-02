using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Welcome;
using static TimeTug.Presentation.Tests.Accounts.ListRig;

namespace TimeTug.Presentation.Tests.Accounts;

/// <summary>The "+" menu: one Microsoft entry, the order of the rest, and the browser fallback link under the box (spec D4, D5; L4).</summary>
public class AddMenuTests
{
    private static readonly IReadOnlyList<AccountKindInfo> AllKinds =
    [
        new("google", "Google", true, "oauth"),
        new("microsoft", "Microsoft", true, "oauth"),
        new("microsoft-brokered", "Microsoft", true, "oauth"),
        new("icloud", "iCloud", true, "password"),
        new("icalsub", "iCal link", true, "password"),
        new("caldav", "Other (CalDAV)", true, "password"),
    ];

    private static ListRig Create(IReadOnlyList<AccountKindInfo>? kinds = null, WindowsAccountListing? listing = null) =>
        ListRig.Create(listing ?? Named, first: States.Empty(kinds ?? AllKinds));

    private static List<AccountKindInfo> Without(string id, IReadOnlyList<AccountKindInfo>? from = null) => (from ?? AllKinds).Where(k => k.Id != id).ToList();

    private static List<AccountKindInfo> Unconfigured(string id, IReadOnlyList<AccountKindInfo>? from = null) =>
        (from ?? AllKinds).Select(k => k.Id == id ? k with { IsConfigured = false } : k).ToList();

    private static AppState Idle(IReadOnlyList<AccountKindInfo> kinds, string? error = null, params AccountInfo[] accounts) =>
        States.With(accounts, activity: new AccountActivity(false, error, "browser"), kinds: kinds);

    private static AppState Working(IReadOnlyList<AccountKindInfo> kinds) => States.With([], activity: new AccountActivity(true, null, "browser"), kinds: kinds);

    // ---- the entries --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_menu_is_Microsoft_Google_then_the_rest_with_CalDAV_last()
    {
        var rig = Create();

        rig.Model.MenuEntries.Select(e => e.Name).Should().Equal("Microsoft", "Google", "iCloud", "iCal link", "Other (CalDAV)");
    }

    [Fact]
    public void CalDAV_is_last_whatever_order_the_engine_lists_the_kinds_in()
    {
        var caldavFirst = new List<AccountKindInfo> { AllKinds[5], AllKinds[3], AllKinds[0], AllKinds[2], AllKinds[1], AllKinds[4] };

        Create(caldavFirst).Model.MenuEntries.Select(e => e.Name).Should().Equal("Microsoft", "Google", "iCloud", "iCal link", "Other (CalDAV)");
    }

    [Fact]
    public void The_Microsoft_entry_uses_the_picker_when_the_broker_is_offered()
    {
        var entry = Create().Model.MenuEntries[0];

        entry.Id.Should().Be(BrokeredKinds.PickItem);
        entry.Name.Should().Be("Microsoft");
        entry.Hint.Should().BeEmpty();
        entry.IsConfigured.Should().BeTrue();
        entry.Intent.Should().Be(BrokerIntent.PickAccount);
        entry.AutomationName.Should().Be("Connect Microsoft");
    }

    [Fact]
    public async Task The_Microsoft_entry_offers_the_browser_when_the_broker_is_not()
    {
        var rig = Create(Unconfigured("microsoft-brokered"));
        var entry = rig.Model.MenuEntries[0];

        entry.Id.Should().Be(BrokeredKinds.Browser);
        entry.Name.Should().Be("Microsoft");
        entry.Hint.Should().Be("Use your browser instead");
        entry.IsConfigured.Should().BeTrue();

        await Press(entry.ConnectCommand);
        rig.Engine.Sent.Should().Equal("add:microsoft");
        rig.Intent.Armed.Should().BeEmpty("the browser path needs no broker intent");
    }

    [Fact]
    public void The_Microsoft_entry_is_disabled_when_neither_route_is_configured()
    {
        var kinds = Unconfigured("microsoft", Unconfigured("microsoft-brokered"));
        var entry = Create(kinds).Model.MenuEntries[0];

        entry.Name.Should().Be("Microsoft");
        entry.IsConfigured.Should().BeFalse();
        entry.Hint.Should().Be("Microsoft sign-in isn’t set up in this build.");
        entry.AutomationName.Should().Be("Microsoft, unavailable in this build");
    }

    [Fact]
    public void Without_the_brokered_kind_at_all_the_Microsoft_entry_says_it_opens_the_browser_and_nothing_else_changes()
    {
        var entry = Create(Without("microsoft-brokered")).Model.MenuEntries[0];

        entry.Id.Should().Be(BrokeredKinds.Browser);
        entry.Hint.Should().Be("Opens your browser to sign in");
    }

    [Fact]
    public void An_unconfigured_kind_is_a_disabled_entry_with_a_reason()
    {
        var model = Create(Unconfigured("google", Unconfigured("icloud"))).Model;

        model.MenuEntries.Select(e => (e.Name, e.IsConfigured)).Should().Equal(
            ("Microsoft", true), ("Google", false), ("iCloud", false), ("iCal link", true), ("Other (CalDAV)", true));
        model.MenuEntries[1].Hint.Should().Be("Google sign-in isn’t set up in this build.");
        model.UnavailableNotice.Should().NotBeNull("the configuration warning under the box stays");
    }

    [Fact]
    public async Task Add_is_disabled_while_a_sign_in_runs_and_the_entries_remain()
    {
        var rig = Create();
        rig.Model.IsAddEnabled.Should().BeTrue();
        var before = rig.Model.MenuEntries.ToList();

        await rig.Model.ConnectAsync("google");
        rig.Engine.Push(Working(AllKinds));

        rig.Model.IsAddEnabled.Should().BeFalse();
        rig.Model.MenuEntries.Should().Equal(before, "entries are never removed while busy");
    }

    [Fact]
    public async Task The_picker_entry_arms_the_picker_intent_and_resets_it()
    {
        var rig = Create();

        await Press(rig.Model.MenuEntries[0].ConnectCommand);

        rig.Intent.Armed.Should().Equal((BrokerIntent.PickAccount, 0));
        rig.Engine.Sent.Should().Equal("add:microsoft-brokered");

        await rig.Model.CancelAsync();
        rig.Intent.Current.Should().Be(BrokerIntent.WindowsAccount);
    }

    [Fact]
    public async Task Other_entries_start_their_kind()
    {
        var rig = Create();

        await Press(rig.Model.MenuEntries.Single(e => e.Name == "Google").ConnectCommand);

        rig.Engine.Sent.Should().Equal("add:google");
    }

    [Fact]
    public void The_menu_is_stable_across_republished_states()
    {
        var rig = Create();
        var before = rig.Model.MenuEntries.ToList();

        for (var i = 0; i < 5; i++)
        {
            rig.Engine.Push(Idle(AllKinds));
        }

        rig.Model.MenuEntries.Should().Equal(before, "the same objects: the open menu is not rebuilt under the person");
    }

    // ---- the fallback link ----------------------------------------------------------------------------------------------------------

    private static async Task StartWindowsSignIn(ListRig rig)
    {
        await rig.Model.ConnectBrokeredAsync(BrokerIntent.PickAccount);
        rig.Engine.Push(Working(AllKinds));
    }

    [Fact]
    public async Task Closing_the_windows_dialog_shows_the_link_and_no_error_words()
    {
        var rig = Create();
        await StartWindowsSignIn(rig);

        rig.Engine.Push(Idle(AllKinds));

        rig.Model.ShowBrowserFallbackLink.Should().BeTrue();
        rig.Model.FallbackMessage.Should().BeNull();
        rig.Model.ErrorText.Should().BeNull();
        rig.Model.SignInFailureText.Should().BeNull();
        rig.Model.BrowserFallbackText.Should().Be("Use your browser instead");
    }

    [Fact]
    public async Task An_unavailable_answer_shows_the_message_and_the_link()
    {
        var rig = Create();
        await StartWindowsSignIn(rig);

        rig.Engine.Push(Idle(AllKinds, "Sign-in failed: Windows sign-in is not available."));

        rig.Model.FallbackMessage.Should().Be("Windows sign-in isn't available here");
        rig.Model.ShowBrowserFallbackLink.Should().BeTrue();
        rig.Model.ErrorText.Should().BeNull("the message already says it");
    }

    [Fact]
    public async Task A_failure_shows_the_engines_detail_under_the_usual_text_and_the_link()
    {
        var rig = Create();
        await StartWindowsSignIn(rig);

        rig.Engine.Push(Idle(AllKinds, "Sign-in failed: admin approval is required."));

        rig.Model.SignInFailureText.Should().Be("Couldn’t connect to Microsoft. Check your details and try again.");
        rig.Model.SignInFailureDetail.Should().Be("Sign-in failed: admin approval is required.");
        rig.Model.FallbackMessage.Should().BeNull();
        rig.Model.ShowBrowserFallbackLink.Should().BeTrue();
        rig.Model.ErrorText.Should().BeNull();
    }

    [Fact]
    public async Task The_link_starts_the_browser_kind()
    {
        var rig = Create();
        await StartWindowsSignIn(rig);
        rig.Engine.Push(Idle(AllKinds));
        rig.Engine.Sent.Clear();
        rig.Intent.Armed.Clear();

        await Press(rig.Model.BrowserFallbackCommand);

        rig.Engine.Sent.Should().Equal("add:microsoft");
        rig.Intent.Armed.Should().BeEmpty();
    }

    [Fact]
    public async Task The_link_disappears_when_any_sign_in_starts()
    {
        var rig = Create();
        await StartWindowsSignIn(rig);
        rig.Engine.Push(Idle(AllKinds, "Sign-in failed: Windows sign-in is not available."));
        rig.Model.ShowBrowserFallbackLink.Should().BeTrue();

        await rig.Model.ConnectAsync("google");

        rig.Model.ShowBrowserFallbackLink.Should().BeFalse();
        rig.Model.FallbackMessage.Should().BeNull();
        rig.Model.SignInFailureText.Should().BeNull();
    }

    [Fact]
    public async Task The_link_disappears_when_a_sign_in_succeeds()
    {
        var rig = Create();
        await StartWindowsSignIn(rig);
        rig.Engine.Push(Idle(AllKinds));
        rig.Model.ShowBrowserFallbackLink.Should().BeTrue();

        await Press(rig.Model.BrowserFallbackCommand);
        rig.Engine.Push(Working(AllKinds));
        rig.Engine.Push(Idle(AllKinds, null, new AccountInfo("m", "microsoft", "other@example.com", "ok", 2)));

        rig.Model.ShowBrowserFallbackLink.Should().BeFalse();
    }

    [Fact]
    public async Task The_link_never_shows_while_the_broker_works_and_nothing_failed()
    {
        var rig = Create();
        rig.Model.ShowBrowserFallbackLink.Should().BeFalse();

        await StartWindowsSignIn(rig);
        rig.Model.ShowBrowserFallbackLink.Should().BeFalse("a sign-in is running");

        rig.Engine.Push(Idle(AllKinds, null, new AccountInfo("m", "microsoft-brokered", "me@example.com", "ok", 2)));
        rig.Model.ShowBrowserFallbackLink.Should().BeFalse("it worked");
    }

    [Fact]
    public async Task A_failed_google_sign_in_does_not_offer_the_microsoft_browser_link_but_does_show_the_failure()
    {
        var rig = Create();
        await rig.Model.ConnectAsync("google");
        rig.Engine.Push(Working(AllKinds));

        rig.Engine.Push(Idle(AllKinds, "Sign-in failed: access denied."));

        rig.Model.ShowBrowserFallbackLink.Should().BeFalse();
        rig.Model.SignInFailureText.Should().Be("Couldn’t connect to Google. Check your details and try again.");
        rig.Model.SignInFailureDetail.Should().Be("Sign-in failed: access denied.");
    }

    [Fact]
    public async Task No_link_is_offered_when_the_browser_kind_is_not_configured()
    {
        var rig = Create(Unconfigured("microsoft"));
        await StartWindowsSignIn(rig);

        rig.Engine.Push(Idle(Unconfigured("microsoft")));

        rig.Model.ShowBrowserFallbackLink.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelling_with_the_waiting_rows_cancel_button_shows_no_link()
    {
        var rig = Create();
        await StartWindowsSignIn(rig);

        await rig.Model.CancelAsync();
        rig.Engine.Push(Idle(AllKinds));

        rig.Model.ShowBrowserFallbackLink.Should().BeFalse("the person cancelled on purpose");
    }
}
