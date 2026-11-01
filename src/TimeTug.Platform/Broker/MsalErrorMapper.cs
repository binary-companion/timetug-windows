using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Identity.Client;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Broker;

/// <summary>
/// Turns what MSAL and the Windows account broker throw into a <see cref="BrokerOutcome"/>, from the observed table in
/// docs/spikes/2026-11-wam-broker.md (S3). Classifies by exception type and <c>ErrorCode</c> first; the WAM numeric code in the message only picks a
/// documented reason, and only from a short tested set. Nothing from a message is ever copied: the log line carries the step and the MSAL error code.
/// </summary>
public static partial class MsalErrorMapper
{
    // WAM codes seen in MsalException messages as "Error Code: <decimal>".
    private const string WamInteractionRequired = "3399548929"; // 0xCAA10001
    private const string WamScopeNotGranted = "2156265484"; // 0x8086000C

    public static BrokerOutcome Map(Exception exception, string step, IDiagnostics diagnostics)
    {
        var outcome = Classify(exception);
        Log(diagnostics, step, exception, outcome);
        return outcome;
    }

    private static BrokerOutcome Classify(Exception exception)
    {
        switch (exception)
        {
            case OperationCanceledException:
                return new BrokerOutcome.Cancelled();
            case MsalClientException { ErrorCode: MsalError.AuthenticationCanceledError }:
                return new BrokerOutcome.Cancelled();
            case MsalUiRequiredException ui:
                return new BrokerOutcome.UiRequired(UiRequiredReason(ui));
            case MsalClientException { ErrorCode: "network_not_available" }:
                return new BrokerOutcome.Network();
            case MsalServiceException service when IsNoNetwork(service):
                return new BrokerOutcome.Network();
            case MsalServiceException service when IsAdminApprovalRequired(service):
                return new BrokerOutcome.Blocked(BrokerReason.AdminConsentRequired);
            case HttpRequestException or SocketException or IOException:
                return new BrokerOutcome.Network();
            case DllNotFoundException or BadImageFormatException or TypeInitializationException or PlatformNotSupportedException or EntryPointNotFoundException:
                return new BrokerOutcome.Unavailable(BrokerReason.BrokerUnavailable);
            default:
                return new BrokerOutcome.Failed();
        }
    }

    private static string UiRequiredReason(MsalUiRequiredException exception)
    {
        if (exception.ErrorCode == MsalError.NoAccountForLoginHint)
        {
            return BrokerReason.AccountMissing;
        }

        var wam = WamCode(exception.Message);
        return exception.Classification switch
        {
            UiRequiredExceptionClassification.ConsentRequired => BrokerReason.Consent,
            UiRequiredExceptionClassification.UserPasswordExpired => BrokerReason.SignedOut,
            UiRequiredExceptionClassification.AdditionalAction => BrokerReason.Mfa,
            _ when wam == WamScopeNotGranted => BrokerReason.Consent,
            _ => BrokerReason.SignedOut,
        };
    }

    // The offline broker error is the decimal of 0x800704CF as the ErrorCode; the message also says NoNetwork.
    private static bool IsNoNetwork(MsalServiceException exception) =>
        exception.ErrorCode == "2147943631" || exception.Message.Contains("NoNetwork", StringComparison.Ordinal);

    // The tenant's consent policy blocks the app: AADSTS90094 "the grant requires admin permission".
    private static bool IsAdminApprovalRequired(MsalServiceException exception) => exception.Message.Contains("AADSTS90094", StringComparison.Ordinal);

    private static string? WamCode(string message)
    {
        var match = WamErrorCode().Match(message);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void Log(IDiagnostics diagnostics, string step, Exception exception, BrokerOutcome outcome)
    {
        var code = ErrorCodeOf(exception);
        var result = outcome.GetType().Name;
        diagnostics.Record(outcome is BrokerOutcome.Cancelled ? "info" : "warning", "broker", $"{step}: {result} ({code})");
    }

    /// <summary>The MSAL error code when it looks like a code (letters, digits, underscore, dot, dash), otherwise the exception's type name.</summary>
    private static string ErrorCodeOf(Exception exception)
    {
        if (exception is MsalException { ErrorCode: { Length: > 0 and <= 64 } code } && SafeCode().IsMatch(code))
        {
            return code;
        }

        return exception is MsalException ? "unknown" : exception.GetType().Name;
    }

    [GeneratedRegex(@"Error Code:\s*(\d+)")]
    private static partial Regex WamErrorCode();

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+$")]
    private static partial Regex SafeCode();
}
