namespace TimeTug.Presentation.Accounts;

/// <summary>The engine-side ids of the Microsoft sign-in that goes through the Windows account broker, and the menu entry the app adds for its account picker.</summary>
public static class BrokeredKinds
{
    /// <summary>The engine's account kind for "Microsoft account on this PC" (the library's <c>microsoft-brokered</c>).</summary>
    public const string Brokered = "microsoft-brokered";

    /// <summary>The existing browser sign-in kind.</summary>
    public const string Browser = "microsoft";

    /// <summary>The id of the menu entry "Microsoft" when the broker works: the same engine kind, started with the account picker intent. Never sent to the engine.</summary>
    public const string PickItem = "microsoft-brokered:pick";
}

/// <summary>What Windows says about its signed-in Microsoft accounts (the broker's account listing: no token, no dialog).</summary>
public enum WindowsListingKind
{
    /// <summary>The broker is not offered, the listing threw, or it did not answer in time. The pinned "This PC's Windows account" row stays hidden.</summary>
    Unavailable,

    /// <summary>Zero accounts: a local-only Windows account.</summary>
    None,

    /// <summary>Exactly one account, and its user name is an email address.</summary>
    OneNamed,

    /// <summary>One or more accounts otherwise (one without an <c>@</c>, or several).</summary>
    Unnamed,
}

/// <param name="Kind">What the listing came to.</param>
/// <param name="Email">The email address of the one account for <see cref="WindowsListingKind.OneNamed"/>; null otherwise.</param>
public sealed record WindowsAccountListing(WindowsListingKind Kind, string? Email = null);

/// <summary>
/// The Microsoft accounts Windows is signed in with, for the pinned "This PC's Windows account" row. Implemented by the platform over the broker's account
/// listing, which asks for no token and shows nothing.
/// </summary>
public interface IWindowsAccountSource
{
    /// <summary>The listing, mapped to the cases the pinned row needs. Never throws except for the caller's own cancellation.</summary>
    ValueTask<WindowsAccountListing> GetListingAsync(CancellationToken cancellationToken);
}
