using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Tests.Broker;

/// <summary>The S2 verdict of docs/spikes/2026-11-wam-broker.md: name the account only when exactly one listed account has an email-shaped user name.</summary>
public class BrokerWindowsAccountSourceTests
{
    private static async Task<string?> Ask(params BrokerAccount[] accounts) =>
        await new BrokerWindowsAccountSource(new FakeBrokerClient { Accounts = accounts }).GetEmailAsync(default);

    [Fact]
    public async Task One_account_with_an_email_shaped_name_is_named()
    {
        (await Ask(new BrokerAccount("id-1", "me@example.com"))).Should().Be("me@example.com");
    }

    [Fact]
    public async Task No_account_leaves_the_card_unlabelled()
    {
        (await Ask()).Should().BeNull();
    }

    [Fact]
    public async Task Two_accounts_leave_the_card_unlabelled_because_it_cannot_tell_which_is_meant()
    {
        (await Ask(new BrokerAccount("a", "me@example.com"), new BrokerAccount("b", "you@example.com"))).Should().BeNull();
    }

    [Fact]
    public async Task A_name_that_is_not_an_email_leaves_the_card_unlabelled()
    {
        (await Ask(new BrokerAccount("a", "LocalUser"))).Should().BeNull();
        (await Ask(new BrokerAccount("a", null))).Should().BeNull();
    }

    [Fact]
    public async Task The_brokered_host_carries_the_source_that_names_the_account()
    {
        var broker = new FakeBrokerClient { Accounts = [new BrokerAccount("a", "me@example.com")] };
        using var host = (BrokeredEngineHost)TimeTug.Platform.WindowsEngineHost.Create(
            new NoLauncher(), new NoForms(), new RecordingDiagnostics(), "client-id-for-test", new FixedWindow(), new TimeTug.Presentation.Accounts.BrokerSignInIntent(), (_, _) => broker);

        (await host.WindowsAccounts!.GetEmailAsync(default)).Should().Be("me@example.com");
    }

    // ---- the listing shape (shared accounts list, W2) ----------------------------------------------------------------------------

    private static async Task<WindowsAccountListing> List(params BrokerAccount[] accounts) =>
        await new BrokerWindowsAccountSource(new FakeBrokerClient { Accounts = accounts }).GetListingAsync(default);

    [Fact]
    public async Task Zero_accounts_is_none() =>
        (await List()).Should().Be(new WindowsAccountListing(WindowsListingKind.None));

    [Fact]
    public async Task One_named_account_is_one_named_with_its_email() =>
        (await List(new BrokerAccount("id", "me@example.com"))).Should().Be(new WindowsAccountListing(WindowsListingKind.OneNamed, "me@example.com"));

    [Fact]
    public async Task One_account_without_an_at_sign_is_unnamed()
    {
        (await List(new BrokerAccount("id", "LocalUser"))).Should().Be(new WindowsAccountListing(WindowsListingKind.Unnamed));
        (await List(new BrokerAccount("id", null))).Should().Be(new WindowsAccountListing(WindowsListingKind.Unnamed));
    }

    [Fact]
    public async Task Two_accounts_are_unnamed() =>
        (await List(new BrokerAccount("a", "me@example.com"), new BrokerAccount("b", "you@example.com"))).Should().Be(new WindowsAccountListing(WindowsListingKind.Unnamed));

    [Fact]
    public async Task A_throwing_broker_is_unavailable()
    {
        var source = new BrokerWindowsAccountSource(new FakeBrokerClient { ListThrows = new InvalidOperationException("broker gone") });
        (await source.GetListingAsync(default)).Should().Be(new WindowsAccountListing(WindowsListingKind.Unavailable));
    }

    [Fact]
    public async Task No_answer_within_five_seconds_is_unavailable_even_when_the_broker_ignores_cancellation()
    {
        var clock = new FakeTimeProvider();
        var broker = new FakeBrokerClient { ListGate = new TaskCompletionSource<IReadOnlyList<BrokerAccount>>() };
        var pending = new BrokerWindowsAccountSource(broker, clock: clock).GetListingAsync(default).AsTask();

        clock.Advance(TimeSpan.FromSeconds(4.9));
        pending.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(0.2));

        (await pending.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(new WindowsAccountListing(WindowsListingKind.Unavailable));
    }

    [Fact]
    public async Task An_answer_at_four_seconds_is_used()
    {
        var clock = new FakeTimeProvider();
        var gate = new TaskCompletionSource<IReadOnlyList<BrokerAccount>>();
        var pending = new BrokerWindowsAccountSource(new FakeBrokerClient { ListGate = gate }, clock: clock).GetListingAsync(default).AsTask();

        clock.Advance(TimeSpan.FromSeconds(4));
        gate.SetResult([new BrokerAccount("id", "me@example.com")]);

        (await pending.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(new WindowsAccountListing(WindowsListingKind.OneNamed, "me@example.com"));
    }

    [Fact]
    public async Task A_timeout_and_a_failure_record_a_diagnostics_line_without_account_data()
    {
        const string sentinel = "sentinel-user@example.test";
        var log = new RecordingDiagnostics();
        await new BrokerWindowsAccountSource(new FakeBrokerClient { ListThrows = new InvalidOperationException(sentinel) }, log).GetListingAsync(default);

        var clock = new FakeTimeProvider();
        var gate = new TaskCompletionSource<IReadOnlyList<BrokerAccount>>();
        var pending = new BrokerWindowsAccountSource(new FakeBrokerClient { ListGate = gate }, log, clock).GetListingAsync(default).AsTask();
        clock.Advance(TimeSpan.FromSeconds(5.1));
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        gate.SetException(new InvalidOperationException(sentinel));   // the abandoned call fails later: nobody must see it

        log.Entries.Should().HaveCount(2).And.OnlyContain(e => e.Category == "broker");
        log.Entries.Should().OnlyContain(e => !e.Message.Contains(sentinel, StringComparison.Ordinal) && !e.Message.Contains('@', StringComparison.Ordinal));
        log.Entries[0].Message.Should().Contain("listing");
        log.Entries[1].Message.Should().Contain("listing");
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_not_a_listing_failure()
    {
        var log = new RecordingDiagnostics();
        var gate = new TaskCompletionSource<IReadOnlyList<BrokerAccount>>();
        using var cts = new CancellationTokenSource();
        var pending = new BrokerWindowsAccountSource(new FakeBrokerClient { ListGate = gate }, log).GetListingAsync(cts.Token).AsTask();

        await cts.CancelAsync();

        await pending.Invoking(p => p).Should().ThrowAsync<OperationCanceledException>();
        log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_email_async_still_works_as_a_projection_of_the_listing()
    {
        (await Ask(new BrokerAccount("id", "me@example.com"))).Should().Be("me@example.com");
        (await Ask()).Should().BeNull();
        (await Ask(new BrokerAccount("a", "me@example.com"), new BrokerAccount("b", "you@example.com"))).Should().BeNull();
    }

    [Fact]
    public async Task The_default_listing_of_a_source_that_only_names_an_email_is_derived_from_it()
    {
        IWindowsAccountSource named = new EmailOnly("me@example.com");
        (await named.GetListingAsync(default)).Should().Be(new WindowsAccountListing(WindowsListingKind.OneNamed, "me@example.com"));
        IWindowsAccountSource none = new EmailOnly(null);
        (await none.GetListingAsync(default)).Should().Be(new WindowsAccountListing(WindowsListingKind.None));
    }

    private sealed class EmailOnly(string? email) : IWindowsAccountSource
    {
        public ValueTask<string?> GetEmailAsync(CancellationToken cancellationToken) => ValueTask.FromResult(email);
    }

    private sealed class FixedWindow : IBrokerParentWindow
    {
        public nint Handle => 1;
    }

    private sealed class NoForms : TimeTug.Presentation.Accounts.IAccountFormService
    {
        public ValueTask<IReadOnlyDictionary<string, string>?> ShowAsync(TimeTug.Presentation.Accounts.AccountFormRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(null);
    }

    private sealed class NoLauncher : TimeTug.Presentation.Abstractions.IUrlLauncher
    {
        public ValueTask<bool> LaunchAsync(string url) => ValueTask.FromResult(false);
    }
}
