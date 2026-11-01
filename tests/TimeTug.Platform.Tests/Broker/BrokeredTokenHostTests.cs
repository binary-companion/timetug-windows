using TimeTug.Engine;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Tests.Broker;

public class BrokeredTokenHostTests
{
    private static readonly string[] Scopes = ["User.Read", "MailboxSettings.Read", "Calendars.Read", "Calendars.Read.Shared"];
    private static readonly DateTimeOffset Expiry = new(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);

    private sealed record Rig(FakeBrokerClient Broker, BrokerSignInIntent Intents, BrokeredTokenHost Host, RecordingDiagnostics Log);

    private static Rig NewRig()
    {
        var broker = new FakeBrokerClient();
        var intents = new BrokerSignInIntent();
        var log = new RecordingDiagnostics();
        return new Rig(broker, intents, new BrokeredTokenHost(broker, intents, log), log);
    }

    private static ExternalTokenRequest Get(bool force = false) => new("conn-1", "acct-1", "me@example.com", Scopes, force);

    private static ExternalSignInRequest SignIn(string id = "sign-1", string? accountId = null, string? email = null) => new(id, Scopes, accountId, email);

    // ---- silent ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Silent_never_calls_interactive()
    {
        var rig = NewRig();
        foreach (var outcome in new BrokerOutcome[]
                 {
                     new BrokerOutcome.UiRequired(BrokerReason.SignedOut), new BrokerOutcome.Unavailable(BrokerReason.BrokerUnavailable),
                     new BrokerOutcome.Blocked(BrokerReason.AdminConsentRequired), new BrokerOutcome.Failed(), new BrokerOutcome.Ok("acct-1", "t", Expiry),
                 })
        {
            rig.Broker.Silent = outcome;
            try
            {
                await rig.Host.GetTokenAsync(Get(), default);
            }
            catch (EngineServiceException)
            {
            }
        }

        rig.Broker.Calls.Should().NotContain("interactive");
    }

    [Fact]
    public async Task Silent_ok_maps_the_token_and_its_expiry()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.Ok("acct-1", "token-1", Expiry);

        var result = await rig.Host.GetTokenAsync(Get(), default);

        result.Should().Be(ExternalTokenResult.Ok("token-1", Expiry));
    }

    [Fact]
    public async Task Silent_requests_carry_the_account_email_scopes_and_force_refresh_unchanged()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.Ok("acct-1", "t", Expiry);

        await rig.Host.GetTokenAsync(Get(force: true), default);

        var request = rig.Broker.SilentRequests.Should().ContainSingle().Subject;
        request.AccountId.Should().Be("acct-1");
        request.Email.Should().Be("me@example.com");
        request.ForceRefresh.Should().BeTrue();
        request.Scopes.Should().Equal(Scopes);
    }

    [Fact]
    public async Task Silent_ui_required_maps_to_interaction_required_with_the_reason()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.UiRequired(BrokerReason.Mfa);

        (await rig.Host.GetTokenAsync(Get(), default)).Should().Be(ExternalTokenResult.InteractionRequired(BrokerReason.Mfa));
    }

    [Fact]
    public async Task Silent_blocked_is_interaction_required_because_only_a_sign_in_can_say_why()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.Blocked(BrokerReason.AdminConsentRequired);

        (await rig.Host.GetTokenAsync(Get(), default)).Should().Be(ExternalTokenResult.InteractionRequired(BrokerReason.AdminConsentRequired));
    }

    [Fact]
    public async Task Silent_unavailable_maps_to_unavailable()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.Unavailable(BrokerReason.BrokerUnavailable);

        (await rig.Host.GetTokenAsync(Get(), default)).Should().Be(ExternalTokenResult.Unavailable(BrokerReason.BrokerUnavailable));
    }

    [Fact]
    public async Task Silent_network_throws_a_network_service_exception()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.Network();

        var act = async () => await rig.Host.GetTokenAsync(Get(), default);

        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Network);
    }

    [Fact]
    public async Task Silent_failed_cancelled_and_a_throwing_broker_map_to_failed_cancelled_and_failed()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.Failed();
        var failed = async () => await rig.Host.GetTokenAsync(Get(), default);
        (await failed.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Failed);

        rig.Broker.Silent = new BrokerOutcome.Cancelled();
        await ((Func<Task>)(async () => await rig.Host.GetTokenAsync(Get(), default))).Should().ThrowAsync<OperationCanceledException>();

        rig.Broker.SilentThrows = new InvalidOperationException("boom");
        (await failed.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Failed);
    }

    // ---- interactive ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sign_in_defaults_to_the_windows_account_intent()
    {
        var rig = NewRig();
        rig.Broker.Interactive = new BrokerOutcome.Ok("acct-1", "t", Expiry);

        await rig.Host.SignInAsync(SignIn(), default);

        rig.Broker.InteractiveRequests.Should().ContainSingle().Which.Intent.Should().Be(BrokerIntent.WindowsAccount);
    }

    [Fact]
    public async Task Sign_in_takes_the_armed_intent_and_resets_it()
    {
        var rig = NewRig();
        rig.Broker.Interactive = new BrokerOutcome.Ok("acct-1", "t", Expiry);
        rig.Intents.Arm(BrokerIntent.PickAccount);

        await rig.Host.SignInAsync(SignIn(), default);
        await rig.Host.SignInAsync(SignIn("sign-2"), default);

        rig.Broker.InteractiveRequests.Select(r => r.Intent).Should().Equal(BrokerIntent.PickAccount, BrokerIntent.WindowsAccount);
    }

    [Fact]
    public async Task Sign_in_forwards_the_account_email_and_scopes_for_a_reconnect()
    {
        var rig = NewRig();
        rig.Broker.Interactive = new BrokerOutcome.Ok("acct-1", "t", Expiry);

        await rig.Host.SignInAsync(SignIn(accountId: "acct-1", email: "me@example.com"), default);

        var request = rig.Broker.InteractiveRequests.Should().ContainSingle().Subject;
        request.AccountId.Should().Be("acct-1");
        request.Email.Should().Be("me@example.com");
        request.Scopes.Should().Equal(Scopes);
    }

    [Fact]
    public async Task Sign_in_ok_returns_the_account_id_and_token()
    {
        var rig = NewRig();
        rig.Broker.Interactive = new BrokerOutcome.Ok("acct-9", "token-9", Expiry);

        (await rig.Host.SignInAsync(SignIn(), default)).Should().Be(ExternalSignInResult.Ok("acct-9", "token-9", Expiry));
    }

    [Fact]
    public async Task Sign_in_blocked_maps_to_blocked_and_unavailable_to_unavailable()
    {
        var rig = NewRig();
        rig.Broker.Interactive = new BrokerOutcome.Blocked(BrokerReason.AdminConsentRequired);
        (await rig.Host.SignInAsync(SignIn(), default)).Should().Be(ExternalSignInResult.Blocked(BrokerReason.AdminConsentRequired));

        rig.Broker.Interactive = new BrokerOutcome.Unavailable(BrokerReason.BrokerUnavailable);
        (await rig.Host.SignInAsync(SignIn("sign-2"), default)).Should().Be(ExternalSignInResult.Unavailable(BrokerReason.BrokerUnavailable));
    }

    [Fact]
    public async Task Sign_in_cancelled_throws_operation_canceled_and_network_and_failed_throw_service_exceptions()
    {
        var rig = NewRig();
        rig.Broker.Interactive = new BrokerOutcome.Cancelled();
        await ((Func<Task>)(async () => await rig.Host.SignInAsync(SignIn(), default))).Should().ThrowAsync<OperationCanceledException>();

        rig.Broker.Interactive = new BrokerOutcome.Network();
        (await ((Func<Task>)(async () => await rig.Host.SignInAsync(SignIn("s2"), default))).Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Network);

        rig.Broker.Interactive = new BrokerOutcome.Failed();
        (await ((Func<Task>)(async () => await rig.Host.SignInAsync(SignIn("s3"), default))).Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Failed);

        rig.Broker.Interactive = new BrokerOutcome.UiRequired(BrokerReason.SignedOut);
        (await ((Func<Task>)(async () => await rig.Host.SignInAsync(SignIn("s4"), default))).Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Failed);
    }

    [Fact]
    public async Task Cancel_sign_in_cancels_only_that_sign_in()
    {
        var rig = NewRig();
        rig.Broker.HoldInteractive = true;
        rig.Broker.Interactive = new BrokerOutcome.Ok("acct-1", "t", Expiry);

        var first = rig.Host.SignInAsync(SignIn("sign-1"), default).AsTask();
        var second = rig.Host.SignInAsync(SignIn("sign-2"), default).AsTask();
        await WaitUntil(() => rig.Broker.HeldCount == 2);

        rig.Host.CancelSignIn("sign-1");

        await first.Awaiting(t => t).Should().ThrowAsync<OperationCanceledException>();
        second.IsCompleted.Should().BeFalse("the other sign-in is untouched");
        rig.Broker.Release();
        (await second).Should().Be(ExternalSignInResult.Ok("acct-1", "t", Expiry));
    }

    [Fact]
    public async Task A_result_that_arrives_after_the_cancel_is_not_reported()
    {
        // The broker finishes with a token in the instant the engine cancels: the engine would drop it anyway, and the host must not hand it out.
        var rig = NewRig();
        rig.Broker.Interactive = new BrokerOutcome.Ok("acct-1", "t", Expiry);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        var act = async () => await rig.Host.SignInAsync(SignIn(), cancel.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Cancel_sign_in_for_an_unknown_id_is_ignored()
    {
        var rig = NewRig();
        var act = () => rig.Host.CancelSignIn("nobody");
        act.Should().NotThrow();
        rig.Host.CancelSignIn("nobody");
    }

    [Fact]
    public async Task Cancel_sign_in_after_the_sign_in_ended_is_ignored()
    {
        var rig = NewRig();
        rig.Broker.Interactive = new BrokerOutcome.Ok("acct-1", "t", Expiry);
        await rig.Host.SignInAsync(SignIn("done"), default);

        var act = () => rig.Host.CancelSignIn("done");

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Exception_messages_are_codes_not_windows_text()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.Network();
        var network = (await ((Func<Task>)(async () => await rig.Host.GetTokenAsync(Get(), default))).Should().ThrowAsync<EngineServiceException>()).Which;
        network.Message.Should().Be("network");

        rig.Broker.Silent = new BrokerOutcome.Failed();
        var failed = (await ((Func<Task>)(async () => await rig.Host.GetTokenAsync(Get(), default))).Should().ThrowAsync<EngineServiceException>()).Which;
        failed.Message.Should().Be("failed");

        // A broker that throws something unexpected, with account data in its message, surfaces as a code too.
        rig.Broker.SilentThrows = new InvalidOperationException("me@example.com token-1");
        var thrown = (await ((Func<Task>)(async () => await rig.Host.GetTokenAsync(Get(), default))).Should().ThrowAsync<EngineServiceException>()).Which;
        thrown.Message.Should().Be("failed");
        thrown.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task Scopes_are_forwarded_unchanged_to_both_requests()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.Ok("acct-1", "t", Expiry);
        rig.Broker.Interactive = new BrokerOutcome.Ok("acct-1", "t", Expiry);

        await rig.Host.GetTokenAsync(Get(), default);
        await rig.Host.SignInAsync(SignIn(), default);

        rig.Broker.SilentRequests.Single().Scopes.Should().Equal(Scopes);
        rig.Broker.InteractiveRequests.Single().Scopes.Should().Equal(Scopes);
    }

    [Fact]
    public async Task A_reason_that_is_not_a_documented_code_is_sent_as_other()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.UiRequired("Your password expired, sign in again");

        (await rig.Host.GetTokenAsync(Get(), default)).Should().Be(ExternalTokenResult.InteractionRequired(BrokerReason.Other));
    }

    [Fact]
    public async Task The_log_carries_the_step_and_outcome_but_no_account_data()
    {
        var rig = NewRig();
        rig.Broker.Silent = new BrokerOutcome.UiRequired(BrokerReason.SignedOut);
        await rig.Host.GetTokenAsync(Get(), default);

        var text = string.Join('\n', rig.Log.Entries.Select(e => e.Message));
        text.Should().Contain("silent").And.NotContain("me@example.com").And.NotContain("acct-1").And.NotContain("conn-1");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue();
    }
}
