using TimeTug.Platform.Credentials;

namespace TimeTug.Platform.Tests.Credentials;

/// <summary>Runs only when <c>TIMETUG_REAL_VAULT=1</c>: the real Credential Locker needs an interactive user session.</summary>
public sealed class RealVaultFactAttribute : FactAttribute
{
    public RealVaultFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TIMETUG_REAL_VAULT") != "1")
        {
            Skip = "Set TIMETUG_REAL_VAULT=1 and run in an interactive user session to use the real Credential Locker.";
        }
    }
}

public class RealVaultTests
{
    [RealVaultFact]
    [Trait("RequiresInteractiveSession", "true")]
    public async Task The_real_credential_locker_round_trips_updates_and_removes()
    {
        var resource = "TimeTug.Test." + Guid.NewGuid().ToString("N");
        var connection = "test-connection";
        var vault = new WindowsPasswordVault();
        using var store = new CredentialLockerStore(vault, resource: resource);
        try
        {
            var longToken = new string('t', 3500);   // longer than one Credential Locker entry
            await store.SetAsync(connection, new Dictionary<string, string> { ["refresh_token"] = longToken, ["client_secret"] = "fake-secret" }, default);

            var snapshot = (await store.SnapshotAsync(connection, default))!;
            snapshot.Secrets["refresh_token"].Should().Be(longToken);
            snapshot.Secrets["client_secret"].Should().Be("fake-secret");

            (await store.UpdateRefreshTokenAsync(connection, "stale", "wrong-revision", default)).Should().BeFalse();
            (await store.UpdateRefreshTokenAsync(connection, "fresh-fake-token", snapshot.Revision, default)).Should().BeTrue();
            (await store.GetAsync(connection, default))!["refresh_token"].Should().Be("fresh-fake-token");

            // A second store over a second vault object sees the same data.
            using var other = new CredentialLockerStore(new WindowsPasswordVault(), resource: resource);
            (await other.GetAsync(connection, default))!["client_secret"].Should().Be("fake-secret");

            await store.RemoveAsync(connection, default);
            (await store.GetAsync(connection, default)).Should().BeNull();
            vault.Retrieve(resource, connection).Should().BeNull();
        }
        finally
        {
            await store.RemoveAsync(connection, default);
        }
    }
}
