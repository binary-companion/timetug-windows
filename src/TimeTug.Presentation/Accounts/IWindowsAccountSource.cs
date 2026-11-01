namespace TimeTug.Presentation.Accounts;

/// <summary>The engine-side ids of the Microsoft sign-in that goes through the Windows account broker, and the extra card the app adds for its account picker.</summary>
public static class BrokeredKinds
{
    /// <summary>The engine's account kind for "Microsoft account on this PC" (the library's <c>microsoft-brokered</c>).</summary>
    public const string Brokered = "microsoft-brokered";

    /// <summary>The existing browser sign-in kind.</summary>
    public const string Browser = "microsoft";

    /// <summary>The id of the card "Another Microsoft account": the same engine kind, started with the account picker intent. Never sent to the engine.</summary>
    public const string PickItem = "microsoft-brokered:pick";
}

/// <summary>
/// The Microsoft account Windows is signed in with, so the welcome card can say "Use name@example.com". Implemented by the platform over the broker's
/// account listing, which asks for no token and shows nothing. Returns null when Windows names no account or exactly one cannot be told.
/// </summary>
public interface IWindowsAccountSource
{
    ValueTask<string?> GetEmailAsync(CancellationToken cancellationToken);
}
