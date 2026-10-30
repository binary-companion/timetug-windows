using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tests.Tug;

namespace TimeTug.Presentation.Tests.Startup;

public sealed class OAuthIdProviderTests : IDisposable
{
    // Obviously fake sentinels; the real ids never appear in this repository.
    private const string GoogleId = "sentinel-google-id.example";
    private const string GoogleSecret = "sentinel-google-secret";
    private const string MicrosoftId = "00000000-0000-0000-0000-0000000005e7";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "timetug-oauth-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingTugDiagnostics _diagnostics = new();

    public OAuthIdProviderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string WriteDevFile()
    {
        var path = Path.Combine(_folder, DevOAuthFile.FileName);
        File.WriteAllText(path, $$"""{ "google": { "clientId": "{{GoogleId}}", "clientSecret": "{{GoogleSecret}}" }, "microsoft": { "clientId": "{{MicrosoftId}}" } }""");
        return path;
    }

    private void NoSentinelsLogged()
    {
        foreach (var sentinel in new[] { GoogleId, GoogleSecret, MicrosoftId })
        {
            _diagnostics.Entries.Should().NotContain(e => e.Message.Contains(sentinel, StringComparison.Ordinal) || e.Category.Contains(sentinel, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_build_with_no_embedded_ids_has_no_provider_configured()
    {
        var provider = new EmbeddedOAuthIdProvider(new EmbeddedOAuthValues(null, null, null), _diagnostics);

        provider.Get(OAuthProvider.Google).Should().BeNull();
        provider.Get(OAuthProvider.Microsoft).Should().BeNull();
        OAuthIdProviders.ToClients(provider).Should().BeEmpty("the engine then reports both kinds as not configured and the views say so");
    }

    [Fact]
    public void Embedded_values_become_clients_and_the_log_names_only_the_families()
    {
        var provider = new EmbeddedOAuthIdProvider(new EmbeddedOAuthValues(GoogleId, GoogleSecret, MicrosoftId), _diagnostics);

        var clients = OAuthIdProviders.ToClients(provider);

        clients.Keys.Should().BeEquivalentTo("google", "microsoft");
        clients["google"].ClientID.Should().Be(GoogleId);
        clients["google"].ClientSecret.Should().Be(GoogleSecret);
        clients["microsoft"].ClientID.Should().Be(MicrosoftId);
        clients["microsoft"].ClientSecret.Should().BeNull();
        _diagnostics.Entries.Should().ContainSingle(e => e.Message.Contains("google, microsoft", StringComparison.Ordinal));
        NoSentinelsLogged();
    }

    [Theory]
    [InlineData(GoogleId, null)]
    [InlineData(null, GoogleSecret)]
    [InlineData(GoogleId, "  ")]
    public void Google_needs_both_its_id_and_its_secret_like_the_mac_build(string? id, string? secret)
    {
        var provider = new EmbeddedOAuthIdProvider(new EmbeddedOAuthValues(id, secret, null), _diagnostics);

        provider.Get(OAuthProvider.Google).Should().BeNull();
        NoSentinelsLogged();
    }

    [Fact]
    public void Microsoft_needs_only_its_id()
    {
        var provider = new EmbeddedOAuthIdProvider(new EmbeddedOAuthValues(null, null, "  " + MicrosoftId + " "), _diagnostics);

        provider.Get(OAuthProvider.Microsoft)!.ClientID.Should().Be(MicrosoftId);
    }

    [Fact]
    public void A_development_build_reads_the_file_on_the_machine()
    {
        var provider = OAuthIdProviders.Create(Flavour.Dev, WriteDevFile(), new EmbeddedOAuthValues(null, null, null), _diagnostics);

        provider.Get(OAuthProvider.Google)!.ClientID.Should().Be(GoogleId);
        provider.Get(OAuthProvider.Microsoft)!.ClientID.Should().Be(MicrosoftId);
        NoSentinelsLogged();
    }

    [Theory]
    [InlineData(Flavour.Store)]
    [InlineData(Flavour.Direct)]
    public void A_release_build_never_reads_the_development_file(Flavour flavour)
    {
        var path = WriteDevFile();

        var provider = OAuthIdProviders.Create(flavour, path, new EmbeddedOAuthValues(null, null, null), _diagnostics);

        provider.Get(OAuthProvider.Google).Should().BeNull();
        provider.Get(OAuthProvider.Microsoft).Should().BeNull();
        _diagnostics.Entries.Should().NotContain(e => e.Message.Contains(DevOAuthFile.FileName, StringComparison.Ordinal), "the file is not even looked at");
        NoSentinelsLogged();
    }

    [Theory]
    [InlineData(Flavour.Store)]
    [InlineData(Flavour.Direct)]
    public void A_release_build_uses_the_embedded_values(Flavour flavour)
    {
        var provider = OAuthIdProviders.Create(flavour, WriteDevFile(), new EmbeddedOAuthValues("embedded-id", "embedded-secret", "embedded-ms"), _diagnostics);

        provider.Get(OAuthProvider.Google)!.ClientID.Should().Be("embedded-id");
        provider.Get(OAuthProvider.Microsoft)!.ClientID.Should().Be("embedded-ms");
    }

    [Fact]
    public void A_development_build_ignores_embedded_values()
    {
        var provider = OAuthIdProviders.Create(Flavour.Dev, Path.Combine(_folder, "missing.json"), new EmbeddedOAuthValues(GoogleId, GoogleSecret, MicrosoftId), _diagnostics);

        provider.Get(OAuthProvider.Google).Should().BeNull();
        provider.Get(OAuthProvider.Microsoft).Should().BeNull();
    }

    [Fact]
    public void The_generated_values_of_this_build_are_empty_unless_the_release_script_wrote_them()
    {
        // Pull-request and merge builds embed no ids: the generated file does not exist in the repository.
        ReleaseOAuthIds.Values().Should().Be(new EmbeddedOAuthValues(null, null, null));
    }
}
