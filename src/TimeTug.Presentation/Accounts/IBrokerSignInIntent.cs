namespace TimeTug.Presentation.Accounts;

/// <summary>Which Windows dialog a Microsoft sign-in through the Windows account broker should open.</summary>
public enum BrokerIntent
{
    /// <summary>The account Windows is signed in with (consent for TimeTug, no account picker when Windows already knows it).</summary>
    WindowsAccount,

    /// <summary>The Windows account picker, which lists every account on the PC and can add one.</summary>
    PickAccount,
}

/// <summary>
/// What the person chose, handed to the broker host: the engine command <c>account.add</c> carries only the kind, so the view model arms the intent
/// before it sends the command and the host takes it when the engine asks for the sign-in. Host-local, like the armed form of a credentials prompt.
/// </summary>
public interface IBrokerSignInIntent
{
    /// <summary>Sets the intent for the next sign-in.</summary>
    void Arm(BrokerIntent intent);

    /// <summary>Returns the armed intent and resets to <see cref="BrokerIntent.WindowsAccount"/>.</summary>
    BrokerIntent Take();
}

/// <summary>The thread-safe <see cref="IBrokerSignInIntent"/>.</summary>
public sealed class BrokerSignInIntent : IBrokerSignInIntent
{
    private int _armed = (int)BrokerIntent.WindowsAccount;

    public void Arm(BrokerIntent intent) => Interlocked.Exchange(ref _armed, (int)intent);

    public BrokerIntent Take() => (BrokerIntent)Interlocked.Exchange(ref _armed, (int)BrokerIntent.WindowsAccount);
}
