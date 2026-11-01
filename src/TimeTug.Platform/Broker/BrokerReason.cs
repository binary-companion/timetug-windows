namespace TimeTug.Platform.Broker;

/// <summary>The documented reason codes of the engine's token protocol. A reason is a code only, never text from Windows.</summary>
public static class BrokerReason
{
    public const string Consent = "consent";
    public const string SignedOut = "signedOut";
    public const string Mfa = "mfa";
    public const string Policy = "policy";
    public const string AccountMissing = "accountMissing";
    public const string AdminConsentRequired = "adminConsentRequired";
    public const string BrokerUnavailable = "brokerUnavailable";
    public const string Other = "other";

    private static readonly string[] All = [Consent, SignedOut, Mfa, Policy, AccountMissing, AdminConsentRequired, BrokerUnavailable, Other];

    /// <summary>The code itself when it is a documented one, otherwise <see cref="Other"/>.</summary>
    public static string Normalize(string? reason) => reason is not null && Array.IndexOf(All, reason) >= 0 ? reason : Other;
}
