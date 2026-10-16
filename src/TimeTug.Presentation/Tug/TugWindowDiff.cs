using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Tug;

/// <summary>What the window layer must do to go from the windows that are open to a new plan.</summary>
/// <param name="Close">Open windows to destroy (their monitor is gone, moved, resized or changed role, or the request changed).</param>
/// <param name="Open">Windows to create, mirrors first and the primary last so the primary ends up with the keyboard.</param>
public sealed record TugWindowChanges(IReadOnlyList<TugMonitorPlan> Close, IReadOnlyList<TugMonitorPlan> Open)
{
    public bool IsEmpty => Close.Count == 0 && Open.Count == 0;
}

/// <summary>
/// Compares the tug windows that are open with a plan. A window is kept only when the monitor id, its bounds and its role are
/// all unchanged and the request is the same one, so a monitor unplugged while the tug is up closes just that window, and
/// a change of primary monitor rebuilds the windows whose role changed (the controls move to the new primary).
/// </summary>
public static class TugWindowDiff
{
    public static TugWindowChanges Compute(string? openRequestId, IReadOnlyList<TugMonitorPlan> open, TugPlan plan)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Mode != TugMode.FullScreen || plan.Request is null || plan.Monitors.Count == 0)
        {
            return new TugWindowChanges(open, []);
        }

        var sameRequest = openRequestId == plan.Request.EventId;
        var close = new List<TugMonitorPlan>();
        var keep = new List<TugMonitorPlan>();
        foreach (var window in open)
        {
            // Records compare by value: the monitor (id, position, size, primary flag) and the role must all match.
            if (sameRequest && plan.Monitors.Contains(window))
            {
                keep.Add(window);
            }
            else
            {
                close.Add(window);
            }
        }

        var create = plan.Monitors.Where(m => !keep.Contains(m)).OrderBy(m => m.Role == TugMonitorRole.Primary).ToList();
        return new TugWindowChanges(close, create);
    }
}
