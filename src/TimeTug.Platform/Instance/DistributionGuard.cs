using TimeTug.Presentation.Startup;

namespace TimeTug.Platform.Instance;

/// <summary>
/// The machine's table of "a copy of this distribution is running" markers. Real: named kernel objects, one per distribution, shared
/// by every package of the user's session (the package-scoped instance key cannot see another package). Tests use a set of names.
/// </summary>
public interface IDistributionMarkers
{
    /// <summary>Marks this distribution as running until the returned handle is disposed (or the process ends).</summary>
    IDisposable Claim(string name);

    /// <summary>True when some process holds the marker <paramref name="name"/>.</summary>
    bool IsPresent(string name);
}

/// <summary>Markers as named mutexes in the session's namespace (<c>Local\</c>). A packaged desktop app is a full-trust process of the user, so another distribution's package sees them.</summary>
public sealed class NamedMutexMarkers : IDistributionMarkers
{
    public IDisposable Claim(string name)
    {
        // Created, never owned: the marker exists while any handle to it is open, and the system closes the handle when the process ends, however it ends.
        return new Mutex(initiallyOwned: false, name);
    }

    public bool IsPresent(string name)
    {
        if (Mutex.TryOpenExisting(name, out var mutex))
        {
            mutex.Dispose();
            return true;
        }

        return false;
    }
}

/// <summary>What startup does after finding out whether this copy is the first of its distribution and whether another distribution runs.</summary>
public enum StartupDecision
{
    /// <summary>Carry on starting: engine, tray, windows.</summary>
    Run,

    /// <summary>Another copy of this distribution is running: hand this launch to it and exit (the existing behaviour).</summary>
    RedirectAndExit,

    /// <summary>A copy from a different distribution is running: tell the person and exit without starting the engine or touching any data.</summary>
    ShowCollisionAndExit,
}

public static class StartupArbiter
{
    /// <summary>
    /// A later launch of the same distribution always redirects. The first copy of its distribution runs unless another distribution's copy is
    /// already running. <paramref name="guard"/> is null for a launch that is not the first (it never looks at the other distributions).
    /// </summary>
    public static StartupDecision Decide(bool isPrimary, DistributionGuard? guard)
    {
        if (!isPrimary)
        {
            return StartupDecision.RedirectAndExit;
        }

        return guard is { OthersRunning.Count: > 0 } ? StartupDecision.ShowCollisionAndExit : StartupDecision.Run;
    }
}

/// <summary>
/// Dev, Store and Direct packages have different identities and so different data, and the Windows App SDK's instance key is per package: two
/// distributions would run side by side, two tray icons, two takeovers for the same meeting. Each running copy therefore holds a marker named after its
/// distribution, and a new first copy that finds another distribution's marker does not start (program design section 6: the other distribution's copy
/// is detected and a notice shown). The own marker is claimed before the others are looked at, so two starting at the same moment each see the
/// other and neither runs; the person starts one again.
/// </summary>
public sealed class DistributionGuard : IDisposable
{
    private readonly IDisposable _claim;

    private DistributionGuard(IDisposable claim, IReadOnlyList<Flavour> others)
    {
        _claim = claim;
        OthersRunning = others;
    }

    /// <summary>The distributions other than this one that are running.</summary>
    public IReadOnlyList<Flavour> OthersRunning { get; }

    /// <summary>The marker name of a distribution.</summary>
    public static string MarkerName(Flavour flavour) => @"Local\TimeTug.Running." + AppIdentity.For(flavour).DataNamespace;

    /// <summary>Marks this distribution as running, then looks for the others.</summary>
    public static DistributionGuard Enter(Flavour own, IDistributionMarkers markers)
    {
        ArgumentNullException.ThrowIfNull(markers);
        var claim = markers.Claim(MarkerName(own));
        var others = Enum.GetValues<Flavour>().Where(f => f != own && markers.IsPresent(MarkerName(f))).ToList();
        return new DistributionGuard(claim, others);
    }

    public void Dispose() => _claim.Dispose();
}
