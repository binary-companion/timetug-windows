namespace TimeTug.Presentation.Abstractions;

/// <summary>
/// "A browser sign-in came back": the loopback listener has captured the redirect, and the app may bring the window that started the sign-in forward.
/// Best effort. Implementations must not throw and must not block.
/// </summary>
public interface ISignInReturnNotifier
{
    /// <summary>Raised once per captured redirect (a code or an error), never for the favicon, a stray request, a cancelled wait or the broker path.</summary>
    void SignInReturned();
}
