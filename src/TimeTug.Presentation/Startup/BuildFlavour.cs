namespace TimeTug.Presentation.Startup;

/// <summary>
/// Which package this build is. One codebase makes three: <see cref="Dev"/> (CI and the VM; the throwaway certificate, the
/// <c>timetug-dev</c> alias, the self-test), <see cref="Store"/> (identity reserved in Partner Center; Microsoft signs it) and
/// <see cref="Direct"/> (the signed website download; identity <c>BinaryCompanion.TimeTug</c>). They have different identities and so
/// separate data (program design section 6).
/// </summary>
public enum Flavour
{
    Dev,
    Store,
    Direct,
}

/// <summary>The flavour of this build, from the <c>STORE</c> and <c>DIRECT</c> compile constants (neither means <see cref="Flavour.Dev"/>).</summary>
public static class BuildFlavour
{
    /// <summary>This build's flavour. <c>Directory.Build.props</c> defines the constant from <c>-p:Flavour=Store|Direct</c>.</summary>
    public static Flavour Current { get; } = Resolve(
#if STORE
        store: true,
#else
        store: false,
#endif
#if DIRECT
        direct: true);
#else
        direct: false);
#endif

    /// <summary>The flavour for a pair of constants. Both at once is a build mistake.</summary>
    public static Flavour Resolve(bool store, bool direct)
    {
        if (store && direct)
        {
            throw new InvalidOperationException("A build is the Store flavour or the Direct flavour, not both.");
        }

        return store ? Flavour.Store : direct ? Flavour.Direct : Flavour.Dev;
    }

    /// <summary>
    /// True only for <see cref="Flavour.Dev"/>: the self-test, the execution alias, the throwaway publisher and the file that holds
    /// developer OAuth ids exist in no other flavour.
    /// </summary>
    public static bool HasDevelopmentScaffolding(Flavour flavour) => flavour == Flavour.Dev;
}

/// <summary>What a flavour calls itself. Used in the diagnostics report only; it decides nothing.</summary>
public sealed record AppIdentity(string DisplayName, string DataNamespace)
{
    /// <summary>The version with the distribution after it, "1.2.3.0 (store)": the first line of Copy diagnostics, so a report says which package made it.</summary>
    public string Label(string version) => $"{version} ({DataNamespace})";

    public static AppIdentity For(Flavour flavour) => flavour switch
    {
        Flavour.Dev => new AppIdentity("TimeTug (development)", "dev"),
        Flavour.Store => new AppIdentity("TimeTug", "store"),
        Flavour.Direct => new AppIdentity("TimeTug", "direct"),
        _ => throw new ArgumentOutOfRangeException(nameof(flavour)),
    };
}
