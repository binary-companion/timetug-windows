using TimeTug.Platform.Broker;

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
