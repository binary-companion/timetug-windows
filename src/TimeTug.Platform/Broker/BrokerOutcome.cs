namespace TimeTug.Platform.Broker;

/// <summary>An account the broker knows (the Windows sign-in account, or one the person added). The email is a hint for matching, never a secret.</summary>
public sealed record BrokerAccount(string AccountId, string? Email);

/// <summary>What one broker request came to. Every outcome carries codes only: no Windows text, no exception message.</summary>
public abstract record BrokerOutcome
{
    private BrokerOutcome()
    {
    }

    /// <summary>A token for <see cref="AccountId"/>.</summary>
    public sealed record Ok(string AccountId, string AccessToken, DateTimeOffset ExpiresAt) : BrokerOutcome
    {
        // The token is a secret: keep it out of any ToString() that reaches a log or a test failure message.
        public override string ToString() => "Ok";
    }

    /// <summary>The person has to act (sign in again, consent, MFA, policy). <see cref="Reason"/> is a <see cref="BrokerReason"/> code.</summary>
    public sealed record UiRequired(string Reason) : BrokerOutcome;

    /// <summary>The person closed or declined the Windows dialog.</summary>
    public sealed record Cancelled : BrokerOutcome;

    /// <summary>An administrator has to approve the app before this account can sign in.</summary>
    public sealed record Blocked(string Reason) : BrokerOutcome;

    /// <summary>The Windows account service cannot be used here (missing, blocked or not loadable).</summary>
    public sealed record Unavailable(string Reason) : BrokerOutcome;

    /// <summary>The request could not reach the service (offline).</summary>
    public sealed record Network : BrokerOutcome;

    /// <summary>Anything else.</summary>
    public sealed record Failed : BrokerOutcome;
}
