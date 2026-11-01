using Microsoft.Identity.Client;
using TimeTug.Platform.Broker;

namespace TimeTug.Platform.Tests.Broker;

/// <summary>One test per row of the S3 table in docs/spikes/2026-11-wam-broker.md, plus the cases the table leaves to the contract.</summary>
public class MsalErrorMapperTests
{
    private const string SentinelEmail = "sentinel.person@example.com";

    private static (BrokerOutcome Outcome, RecordingDiagnostics Log) Map(Exception exception, string step = "silent")
    {
        var log = new RecordingDiagnostics();
        return (MsalErrorMapper.Map(exception, step, log), log);
    }

    [Fact]
    public void A_silent_request_with_no_account_is_ui_required_signed_out()
    {
        // Observed: MsalUiRequiredException failed_to_acquire_token_silently_from_broker, WAM 0xCAA10001 (3399548929), Classification None.
        var exception = new MsalUiRequiredException("failed_to_acquire_token_silently_from_broker", "Error Code: 3399548929 interaction required");
        Map(exception).Outcome.Should().Be(new BrokerOutcome.UiRequired(BrokerReason.SignedOut));
    }

    [Fact]
    public void An_unknown_login_hint_is_ui_required_account_missing()
    {
        var exception = new MsalUiRequiredException("no_account_for_login_hint", "no account", null, UiRequiredExceptionClassification.AcquireTokenSilentFailed);
        Map(exception).Outcome.Should().Be(new BrokerOutcome.UiRequired(BrokerReason.AccountMissing));
    }

    [Fact]
    public void A_scope_the_account_never_granted_is_ui_required_consent()
    {
        var exception = new MsalUiRequiredException("failed_to_acquire_token_silently_from_broker", "Error Code: 2156265484 the scope is not granted");
        Map(exception).Outcome.Should().Be(new BrokerOutcome.UiRequired(BrokerReason.Consent));
    }

    [Fact]
    public void A_consent_required_classification_is_ui_required_consent()
    {
        var exception = new MsalUiRequiredException("invalid_grant", "consent", null, UiRequiredExceptionClassification.ConsentRequired);
        Map(exception).Outcome.Should().Be(new BrokerOutcome.UiRequired(BrokerReason.Consent));
    }

    [Fact]
    public void An_expired_password_is_ui_required_signed_out()
    {
        var exception = new MsalUiRequiredException("invalid_grant", "password", null, UiRequiredExceptionClassification.UserPasswordExpired);
        Map(exception).Outcome.Should().Be(new BrokerOutcome.UiRequired(BrokerReason.SignedOut));
    }

    [Fact]
    public void Extra_action_is_ui_required_mfa_and_any_other_ui_required_exception_is_still_ui_required()
    {
        Map(new MsalUiRequiredException("interaction_required", "mfa", null, UiRequiredExceptionClassification.AdditionalAction)).Outcome
            .Should().Be(new BrokerOutcome.UiRequired(BrokerReason.Mfa));
        Map(new MsalUiRequiredException("something_new", "x")).Outcome.Should().BeOfType<BrokerOutcome.UiRequired>();
    }

    [Fact]
    public void A_closed_dialog_is_cancelled()
    {
        Map(new MsalClientException("authentication_canceled", "User canceled authentication")).Outcome.Should().BeOfType<BrokerOutcome.Cancelled>();
        Map(new OperationCanceledException()).Outcome.Should().BeOfType<BrokerOutcome.Cancelled>();
        Map(new TaskCanceledException()).Outcome.Should().BeOfType<BrokerOutcome.Cancelled>();
    }

    [Fact]
    public void A_tenant_that_blocks_user_consent_is_blocked_admin_consent_required()
    {
        var exception = new MsalServiceException("access_denied", "AADSTS90094: The grant requires admin permission.");
        Map(exception, "interactive").Outcome.Should().Be(new BrokerOutcome.Blocked(BrokerReason.AdminConsentRequired));
    }

    [Fact]
    public void A_missing_or_unloadable_broker_is_unavailable_broker_unavailable()
    {
        foreach (var exception in new Exception[]
                 {
                     new DllNotFoundException("msalruntime"),
                     new BadImageFormatException("wrong architecture"),
                     new TypeInitializationException("Microsoft.Identity.Client.NativeInterop.Core", new DllNotFoundException()),
                     new PlatformNotSupportedException(),
                     new EntryPointNotFoundException(),
                 })
        {
            Map(exception).Outcome.Should().Be(new BrokerOutcome.Unavailable(BrokerReason.BrokerUnavailable), exception.GetType().Name);
        }
    }

    [Fact]
    public void No_network_is_network_and_never_ui_required()
    {
        // Observed offline: MsalServiceException, ErrorCode "2147943631" (0x800704CF), message mentions NoNetwork; it even failed requests a cached token could have served.
        Map(new MsalServiceException("2147943631", "NoNetwork: there is no Internet connection")).Outcome.Should().BeOfType<BrokerOutcome.Network>();
        Map(new MsalClientException("network_not_available", "offline")).Outcome.Should().BeOfType<BrokerOutcome.Network>();
        Map(new HttpRequestException("connection failed")).Outcome.Should().BeOfType<BrokerOutcome.Network>();
        Map(new System.Net.Sockets.SocketException()).Outcome.Should().BeOfType<BrokerOutcome.Network>();
    }

    [Fact]
    public void A_malformed_scope_a_missing_window_and_unknown_exceptions_are_failed()
    {
        Map(new MsalServiceException("WAM_provider_error_2156265473", "ApiContractViolation")).Outcome.Should().BeOfType<BrokerOutcome.Failed>();
        Map(new MsalClientException("window_handle_required", "no window")).Outcome.Should().BeOfType<BrokerOutcome.Failed>();
        Map(new InvalidOperationException("surprise")).Outcome.Should().BeOfType<BrokerOutcome.Failed>();
    }

    [Fact]
    public void The_log_line_carries_the_step_and_the_error_code_only()
    {
        var exception = new MsalServiceException("WAM_provider_error_2156265473", $"Error for {SentinelEmail} token=abc.def.ghi accountId=00000000-0000-0000-0000-000000000000");
        var (_, log) = Map(exception, "interactive");

        var entry = log.Entries.Should().ContainSingle().Subject;
        entry.Category.Should().Be("broker");
        entry.Message.Should().Contain("interactive").And.Contain("WAM_provider_error_2156265473");
        entry.Message.Should().NotContain(SentinelEmail).And.NotContain("abc.def.ghi").And.NotContain("00000000");
    }

    [Fact]
    public void An_error_code_that_carries_data_is_not_logged()
    {
        var (_, log) = Map(new MsalServiceException(SentinelEmail, "x"));
        log.Entries.Should().ContainSingle().Which.Message.Should().NotContain(SentinelEmail);
    }

    [Fact]
    public void A_plain_exception_is_logged_by_type_name_only()
    {
        var (_, log) = Map(new InvalidOperationException($"inner {SentinelEmail}"));
        log.Entries.Should().ContainSingle().Which.Message.Should().Contain(nameof(InvalidOperationException)).And.NotContain(SentinelEmail);
    }

    [Fact]
    public void Reasons_are_always_documented_codes()
    {
        BrokerReason.Normalize("signedOut").Should().Be("signedOut");
        BrokerReason.Normalize("Free text from Windows").Should().Be(BrokerReason.Other);
        BrokerReason.Normalize(null).Should().Be(BrokerReason.Other);
    }
}
