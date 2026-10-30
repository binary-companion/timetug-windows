using TimeTug.Presentation.Abstractions;
using Pkg = global::TimeTug.Engine;

namespace TimeTug.Presentation.Startup;

/// <summary>The OAuth sign-in families that need a client registration. The engine keys them <c>google</c> and <c>microsoft</c>.</summary>
public enum OAuthProvider
{
    Google,
    Microsoft,
}

/// <summary>
/// Where a build gets its OAuth client registrations from. A provider with no registration returns null; the engine then reports that kind
/// as not configured and the welcome window and Settings say "isn’t set up in this build" instead of showing a button that fails.
/// </summary>
public interface IOAuthIdProvider
{
    /// <summary>The client for <paramref name="provider"/>, or null when this build has none. Google's carries its secret (not confidential for an installed app).</summary>
    Pkg.OAuthClient? ClientFor(OAuthProvider provider);
}

/// <summary>The values the release script wrote into the generated <see cref="ReleaseOAuthIds"/> class; all null in every other build.</summary>
public sealed record EmbeddedOAuthValues(string? GoogleClientId, string? GoogleClientSecret, string? MicrosoftClientId);

#if DEVFLAVOUR
/// <summary>Development and CI builds only: the file on this machine (<see cref="DevOAuthFile"/>). Not compiled into Store or Direct builds.</summary>
public sealed class DevFileOAuthIdProvider(string path, IDiagnostics diagnostics) : IOAuthIdProvider
{
    private IReadOnlyDictionary<string, Pkg.OAuthClient>? _clients;

    public Pkg.OAuthClient? ClientFor(OAuthProvider provider)
    {
        _clients ??= DevOAuthFile.Load(path, diagnostics);
        return _clients.GetValueOrDefault(OAuthIdProviders.Family(provider));
    }
}

#endif

/// <summary>
/// Store and Direct builds: the ids the release job injected at build time. Google needs both its id and its secret (as the Mac build does:
/// both or neither), Microsoft only its id. The log names the families that were found and nothing else.
/// </summary>
public sealed class EmbeddedOAuthIdProvider : IOAuthIdProvider
{
    private readonly Dictionary<OAuthProvider, Pkg.OAuthClient> _clients = [];

    public EmbeddedOAuthIdProvider(EmbeddedOAuthValues values, IDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var googleId = Clean(values.GoogleClientId);
        var googleSecret = Clean(values.GoogleClientSecret);
        if (googleId is not null && googleSecret is not null)
        {
            _clients[OAuthProvider.Google] = new Pkg.OAuthClient(googleId, googleSecret);
        }
        else if (googleId is not null || googleSecret is not null)
        {
            diagnostics.Record("warning", "oauth", "google sign-in needs both of its client values in this build; it stays unconfigured");
        }

        var microsoftId = Clean(values.MicrosoftClientId);
        if (microsoftId is not null)
        {
            _clients[OAuthProvider.Microsoft] = new Pkg.OAuthClient(microsoftId, null);
        }

        if (_clients.Count > 0)
        {
            diagnostics.Record("info", "oauth", "this build configures: " + string.Join(", ", _clients.Keys.Select(OAuthIdProviders.Family).Order(StringComparer.Ordinal)));
        }
    }

    public Pkg.OAuthClient? ClientFor(OAuthProvider provider) => _clients.GetValueOrDefault(provider);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// The generated half of <see cref="ReleaseOAuthIds"/>: <c>scripts/release/write-oauth-ids.ps1</c> writes <c>ReleaseOAuthIds.g.cs</c> (git-ignored)
/// from the release job's environment just before a release build and deletes it afterwards. Without that file the partial method has no body
/// and every value is null.
/// </summary>
public static partial class ReleaseOAuthIds
{
    /// <summary>What this build embeds: all null unless the release script generated the other half.</summary>
    public static EmbeddedOAuthValues Values()
    {
        string? googleId = null, googleSecret = null, microsoftId = null;
        Provide(ref googleId, ref googleSecret, ref microsoftId);
        return new EmbeddedOAuthValues(googleId, googleSecret, microsoftId);
    }

    static partial void Provide(ref string? googleClientId, ref string? googleClientSecret, ref string? microsoftClientId);
}

public static class OAuthIdProviders
{
    /// <summary>
    /// The provider for a flavour: the file on this machine for Dev, the embedded values for Store and Direct (which never look at the file).
    /// In a release build the file reader is not compiled at all, so <paramref name="devFilePath"/> is unused there.
    /// </summary>
    public static IOAuthIdProvider Create(Flavour flavour, string devFilePath, EmbeddedOAuthValues embedded, IDiagnostics diagnostics)
    {
#if DEVFLAVOUR
        return BuildFlavour.HasDevelopmentScaffolding(flavour)
            ? new DevFileOAuthIdProvider(devFilePath, diagnostics)
            : new EmbeddedOAuthIdProvider(embedded, diagnostics);
#else
        _ = devFilePath;
        _ = flavour;
        return new EmbeddedOAuthIdProvider(embedded, diagnostics);
#endif
    }

    /// <summary>The clients in the shape the engine takes at start (keyed by connector family); empty when the build has none.</summary>
    public static IReadOnlyDictionary<string, Pkg.OAuthClient> ToClients(IOAuthIdProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var clients = new Dictionary<string, Pkg.OAuthClient>(StringComparer.Ordinal);
        foreach (var kind in Enum.GetValues<OAuthProvider>())
        {
            if (provider.ClientFor(kind) is { } client)
            {
                clients[Family(kind)] = client;
            }
        }

        return clients;
    }

    internal static string Family(OAuthProvider provider) => provider switch
    {
        OAuthProvider.Google => "google",
        OAuthProvider.Microsoft => "microsoft",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
}
