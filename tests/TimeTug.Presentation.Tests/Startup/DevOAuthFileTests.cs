using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tests.Tug;

namespace TimeTug.Presentation.Tests.Startup;

public class DevOAuthFileTests
{
    // Obviously fake values; the real ones never appear in this repository.
    private const string GoogleId = "google-test-client-id.example";
    private const string GoogleSecret = "google-test-secret-value";
    private const string MicrosoftId = "00000000-0000-0000-0000-000000000042";

    private const string Sample = $$"""
        {
          "google": { "clientId": "{{GoogleId}}", "clientSecret": "{{GoogleSecret}}" },
          "microsoft": { "clientId": "{{MicrosoftId}}" }
        }
        """;

    private static void NoIdsIn(RecordingTugDiagnostics diagnostics)
    {
        foreach (var secret in new[] { GoogleId, GoogleSecret, MicrosoftId })
        {
            diagnostics.Entries.Should().NotContain(e => e.Message.Contains(secret, StringComparison.Ordinal) || e.Category.Contains(secret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Both_families_are_read_with_the_google_secret_and_no_microsoft_secret()
    {
        var diagnostics = new RecordingTugDiagnostics();

        var clients = DevOAuthFile.Parse(Sample, diagnostics);

        clients.Keys.Should().BeEquivalentTo("google", "microsoft");
        clients["google"].ClientID.Should().Be(GoogleId);
        clients["google"].ClientSecret.Should().Be(GoogleSecret);
        clients["microsoft"].ClientID.Should().Be(MicrosoftId);
        clients["microsoft"].ClientSecret.Should().BeNull();
        NoIdsIn(diagnostics);
    }

    [Fact]
    public void Names_are_not_case_sensitive()
    {
        var clients = DevOAuthFile.Parse("""{ "Google": { "ClientId": "a-id", "ClientSecret": "a-secret" } }""", new RecordingTugDiagnostics());

        clients["google"].ClientID.Should().Be("a-id");
    }

    [Fact]
    public void A_family_without_a_client_id_and_unknown_families_are_ignored()
    {
        var clients = DevOAuthFile.Parse("""{ "google": { "clientId": "  " }, "microsoft": {}, "apple": { "clientId": "x" } }""", new RecordingTugDiagnostics());

        clients.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{ "google": "text" }""")]
    public void A_malformed_file_gives_no_clients_and_a_diagnostic_that_names_no_values(string text)
    {
        var diagnostics = new RecordingTugDiagnostics();

        var clients = DevOAuthFile.Parse(text, diagnostics);

        clients.Should().BeEmpty();
        diagnostics.Entries.Should().ContainSingle().Which.Level.Should().Be("warning");
        diagnostics.Entries[0].Message.Should().NotContain(text.Trim().Length > 3 ? text.Trim() : "\u0000");
    }

    [Fact]
    public void A_missing_file_is_normal_and_says_nothing()
    {
        var diagnostics = new RecordingTugDiagnostics();

        var clients = DevOAuthFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "dev-oauth.json"), diagnostics);

        clients.Should().BeEmpty();
        diagnostics.Entries.Should().BeEmpty();
    }

    [Fact]
    public void A_file_on_disk_is_loaded_and_the_diagnostic_names_only_the_families()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timetug-oauth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "dev-oauth.json");
            File.WriteAllText(path, Sample);
            var diagnostics = new RecordingTugDiagnostics();

            var clients = DevOAuthFile.Load(path, diagnostics);

            clients.Keys.Should().BeEquivalentTo("google", "microsoft");
            diagnostics.Entries.Should().ContainSingle().Which.Message.Should().Contain("google").And.Contain("microsoft");
            NoIdsIn(diagnostics);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_reported_without_its_contents()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timetug-oauth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var diagnostics = new RecordingTugDiagnostics();
            var path = Path.Combine(folder, "dev-oauth.json");
            File.WriteAllText(path, Sample);

            // Another process holds the file open exclusively: reading fails with an IO error.
            using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var clients = DevOAuthFile.Load(path, diagnostics);

            clients.Should().BeEmpty();
            diagnostics.Entries.Should().ContainSingle().Which.Level.Should().Be("warning");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
