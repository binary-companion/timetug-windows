using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Tug;

/// <summary>What the window layer should draw.</summary>
public enum TugMode
{
    /// <summary>Nothing: close every tug window.</summary>
    None,

    /// <summary>The full tug on the monitors listed in <see cref="TugPlan.Monitors"/>.</summary>
    FullScreen,

    /// <summary>A toast is showing; no tug window.</summary>
    Toast,
}

public enum TugMonitorRole
{
    /// <summary>Shows the controls (Join, Snooze, Dismiss) and takes the keyboard.</summary>
    Primary,

    /// <summary>Shows the countdown and the title and says "Dismiss on your main display".</summary>
    Mirror,
}

public sealed record TugMonitorPlan(MonitorInfo Monitor, TugMonitorRole Role);

/// <summary>
/// One complete description of what the tug looks like right now. The controller publishes a new plan whenever
/// anything about it changes; the window layer redraws from the plan and keeps no tug logic of its own.
/// </summary>
/// <param name="Mode">Full screen, toast or nothing.</param>
/// <param name="Request">The request being shown (null for <see cref="TugMode.None"/>).</param>
/// <param name="Monitors">One entry per monitor, exactly one of them primary (empty when no monitor is connected).</param>
/// <param name="ShowClickToRespond">True when Windows did not hand over the keyboard: show "Click anywhere to respond".</param>
/// <param name="JoinFailedUrl">Set when opening the join link failed: show this link to copy instead.</param>
/// <param name="LateBy">How long after the meeting started the tug appears, when it appears on return from a hold; null otherwise.</param>
public sealed record TugPlan(
    TugMode Mode,
    TakeoverRequest? Request,
    IReadOnlyList<TugMonitorPlan> Monitors,
    bool ShowClickToRespond,
    string? JoinFailedUrl,
    TimeSpan? LateBy)
{
    public static TugPlan None { get; } = new(TugMode.None, null, [], false, null, null);

    public static TugPlan ForToast(TakeoverRequest request) => new(TugMode.Toast, request, [], false, null, null);

    /// <summary>The plan with the join-failure link attached (the request stays; the person copies the link or responds another way).</summary>
    public TugPlan JoinFailed(string url) => this with { JoinFailedUrl = url };
}
