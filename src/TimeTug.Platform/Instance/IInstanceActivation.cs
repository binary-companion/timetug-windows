using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.JumpList;

namespace TimeTug.Platform.Instance;

/// <summary>What a second launch asks the running copy to do.</summary>
public enum ActivationKind
{
    /// <summary>Open the flyout (the Mac does the same when the app is started again).</summary>
    Flyout,

    /// <summary>Carry out a jump list action (<see cref="AppActivation.Jump"/>).</summary>
    Jump,
}

/// <summary>
/// One request to the running copy: open the flyout (any second launch), or a <see cref="JumpAction"/> from an item of the taskbar jump
/// list (<c>--jump flyout|join-next|test-tug|settings</c>). Two requests are equal when they ask for the same thing.
/// </summary>
public readonly record struct AppActivation(JumpAction? Jump = null)
{
    /// <summary>A plain second launch: open the flyout.</summary>
    public static AppActivation Flyout => default;

    /// <summary>A jump list item was chosen.</summary>
    public static AppActivation ForJump(JumpAction action) => new(action);

    public ActivationKind Kind => Jump is null ? ActivationKind.Flyout : ActivationKind.Jump;

    /// <summary>A fixed word for the log: never anything from the launch beyond the action.</summary>
    public string Describe() => Jump is { } action ? "the jump list action " + JumpArguments.Word(action) : "the flyout";
}

/// <summary>
/// This process's place among the copies of the app. The first copy is the primary; any later launch redirects to it and exits
/// (see <see cref="AppInstanceService"/>).
/// </summary>
public interface IAppInstance : IDisposable
{
    /// <summary>True for the first copy. A copy that is not primary must call <see cref="RedirectAsync"/> and exit without touching the engine's data.</summary>
    bool IsPrimary { get; }

    /// <summary>
    /// A second launch asked the running copy for something. Raised on an arbitrary thread (post to the UI thread), never before
    /// <see cref="MarkReady"/>, at most once for a burst of identical requests, and never while the tug is showing.
    /// </summary>
    event Action<AppActivation>? Activated;

    /// <summary>For a copy that is not primary: hands this launch over to the primary and returns once it has taken it (or could not be reached).</summary>
    Task RedirectAsync(AppActivation activation);

    /// <summary>
    /// The first copy's own launch arguments carried a jump list action (the person chose an item while TimeTug was not running): it is
    /// held with any other early activation and delivered by <see cref="MarkReady"/>. A plain launch (<see cref="AppActivation.Flyout"/>)
    /// asks for nothing and is ignored.
    /// </summary>
    void AcceptLaunch(AppActivation activation);

    /// <summary>
    /// Startup has finished (the engine is up and has sent its first state). A launch that arrived earlier was held, one at most, and is
    /// delivered now. <paramref name="tug"/> is what tells later activations that the tug is up.
    /// </summary>
    void MarkReady(ITugPresence? tug = null);
}

/// <summary>The operating system's table of running copies, by key. The Windows App SDK implements it; tests use a fake; an unpackaged run uses a mutex.</summary>
public interface IInstanceRegistry
{
    /// <summary>Registers this process under <paramref name="key"/>, or finds the process that already has it.</summary>
    IInstanceRegistration FindOrRegister(string key);
}

/// <summary>One key's registration, as this process sees it.</summary>
public interface IInstanceRegistration : IDisposable
{
    /// <summary>True when this process is the one registered under the key.</summary>
    bool IsCurrent { get; }

    /// <summary>A later launch was redirected here. Only for the current registration; raised on an arbitrary thread.</summary>
    event Action<AppActivation>? ActivationReceived;

    /// <summary>Hands this process's launch to the process that holds the key.</summary>
    Task RedirectToAsync(AppActivation activation);
}
