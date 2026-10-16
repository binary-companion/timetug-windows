using TimeTug.Engine;
using TimeTug.Platform.Credentials;

namespace TimeTug.Platform.Tests.Credentials;

public class CredentialLockerStoreTests
{
    private static Dictionary<string, string> Secrets(string refresh = "fake-refresh-1") =>
        new() { ["refresh_token"] = refresh, ["client_secret"] = "fake-client-secret" };

    [Fact]
    public async Task A_missing_connection_is_null_and_never_an_exception()
    {
        var store = new CredentialLockerStore(new FakePasswordVault());
        (await store.GetAsync("nope", default)).Should().BeNull();
        (await store.SnapshotAsync("nope", default)).Should().BeNull();
        await store.RemoveAsync("nope", default);
        (await store.UpdateRefreshTokenAsync("nope", "t", "r", default)).Should().BeFalse();
    }

    [Fact]
    public async Task Secrets_round_trip_and_are_removed()
    {
        var store = new CredentialLockerStore(new FakePasswordVault());
        await store.SetAsync("c1", Secrets(), default);
        (await store.GetAsync("c1", default)).Should().BeEquivalentTo(Secrets());
        await store.RemoveAsync("c1", default);
        (await store.GetAsync("c1", default)).Should().BeNull();
    }

    [Fact]
    public async Task Refresh_token_update_with_a_stale_revision_writes_nothing()
    {
        var vault = new FakePasswordVault();
        var store = new CredentialLockerStore(vault);
        await store.SetAsync("c1", new Dictionary<string, string> { ["refresh_token"] = "old" }, default);
        var snapshot = (await store.SnapshotAsync("c1", default))!;
        var writes = vault.WriteCount;

        (await store.UpdateRefreshTokenAsync("c1", "new", "not-the-revision", default)).Should().BeFalse();
        vault.WriteCount.Should().Be(writes);
        (await store.GetAsync("c1", default))!["refresh_token"].Should().Be("old");

        (await store.UpdateRefreshTokenAsync("c1", "new", snapshot.Revision, default)).Should().BeTrue();
        (await store.GetAsync("c1", default))!["refresh_token"].Should().Be("new");
        (await store.SnapshotAsync("c1", default))!.Revision.Should().NotBe(snapshot.Revision);
    }

    [Fact]
    public async Task Twenty_parallel_updates_with_the_same_revision_let_exactly_one_win()
    {
        var store = new CredentialLockerStore(new FakePasswordVault());
        await store.SetAsync("c1", Secrets("start"), default);
        var revision = (await store.SnapshotAsync("c1", default))!.Revision;

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Task.Run(async () => await store.UpdateRefreshTokenAsync("c1", $"token-{i}", revision, default))));

        results.Count(won => won).Should().Be(1);
        var winner = Array.IndexOf(results, true);
        (await store.GetAsync("c1", default))!["refresh_token"].Should().Be($"token-{winner}");
    }

    [Fact]
    public async Task Updating_the_refresh_token_keeps_the_other_secrets()
    {
        var store = new CredentialLockerStore(new FakePasswordVault());
        await store.SetAsync("c1", Secrets(), default);
        var revision = (await store.SnapshotAsync("c1", default))!.Revision;
        await store.UpdateRefreshTokenAsync("c1", "next", revision, default);
        (await store.GetAsync("c1", default))!["client_secret"].Should().Be("fake-client-secret");
    }

    [Fact]
    public async Task The_vault_holds_only_the_json_blob_under_one_resource_and_the_connection_id()
    {
        var vault = new FakePasswordVault();
        var store = new CredentialLockerStore(vault);
        await store.SetAsync("c1", Secrets(), default);

        vault.Items.Keys.Should().OnlyContain(key => key.Resource == "TimeTug");
        vault.Items.Keys.Select(key => key.UserName).Should().Contain("c1");
        // No secret appears in a resource or user name, and the connection id is the head entry's name.
        vault.Items.Keys.Should().NotContain(key => key.UserName.Contains("fake-") || key.Resource.Contains("fake-"));
        // Reassembling the stored text gives one JSON object holding the secrets and a revision.
        var text = string.Concat(vault.Items.Values);
        text.Should().Contain("\"revision\"").And.Contain("\"refresh_token\"");
    }

    [Fact]
    public async Task A_secret_larger_than_one_vault_entry_round_trips_and_leaves_no_stale_chunks()
    {
        var vault = new FakePasswordVault();
        var store = new CredentialLockerStore(vault);
        var longToken = new string('x', 4500);
        await store.SetAsync("c1", Secrets(longToken), default);
        vault.Items.Count.Should().BeGreaterThan(1);
        vault.Items.Values.Should().OnlyContain(value => value.Length <= 1100);
        (await store.GetAsync("c1", default))!["refresh_token"].Should().Be(longToken);

        await store.SetAsync("c1", Secrets("short"), default);
        vault.Items.Count.Should().Be(1);
        await store.RemoveAsync("c1", default);
        vault.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task A_vault_that_refuses_becomes_a_clear_service_error_and_a_diagnostic()
    {
        var vault = new FakePasswordVault { FailWith = new UnauthorizedAccessException("blocked by policy") };
        var log = new RecordingDiagnostics();
        var store = new CredentialLockerStore(vault, log);

        var act = async () => await store.SetAsync("c1", Secrets(), default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Denied);
        log.Entries.Should().ContainSingle(e => e.Level == "error" && e.Category == "credentials");
        log.Entries.Single().Message.Should().NotContain("fake-");
    }

    [Fact]
    public async Task A_damaged_entry_reads_as_no_credentials()
    {
        var vault = new FakePasswordVault();
        vault.Add("TimeTug", "c1", "not even close to the format");
        var store = new CredentialLockerStore(vault);
        (await store.GetAsync("c1", default)).Should().BeNull();
    }
}
