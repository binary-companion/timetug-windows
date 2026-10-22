namespace TimeTug.Presentation.Startup;

/// <summary>
/// What Windows says about the app's startup task (launch at login). Windows owns the answer: the person can switch the task off in
/// Settings or Task Manager and an organization's policy can force it either way, so the app never keeps a copy of its own.
/// </summary>
public enum StartupState
{
    /// <summary>The task will run at the next sign-in.</summary>
    Enabled,

    /// <summary>The task is off and the app may ask to turn it on.</summary>
    Disabled,

    /// <summary>The person turned the task off in Windows (Settings, Startup apps, or Task Manager). Only the person can turn it on again.</summary>
    DisabledByUser,

    /// <summary>A policy on this PC keeps the task off.</summary>
    DisabledByPolicy,

    /// <summary>A policy on this PC keeps the task on.</summary>
    EnabledByPolicy,
}

/// <summary>
/// Launch at login over <c>Windows.ApplicationModel.StartupTask</c>. Windows cannot enable a startup task silently when the person has
/// turned it off, so every call answers with the state Windows reports <em>afterwards</em> and the caller shows that, never what it asked
/// for. A failure of the call itself (no package identity, the system refusing) is an exception.
/// </summary>
public interface IStartupTaskService
{
    /// <summary>The task's state now.</summary>
    Task<StartupState> GetStateAsync();

    /// <summary>Asks Windows to turn the task on, and returns the state it is in afterwards (it stays off when the person or a policy has disabled it).</summary>
    Task<StartupState> RequestEnableAsync();

    /// <summary>Turns the task off, and returns the state it is in afterwards.</summary>
    Task<StartupState> DisableAsync();
}
