using TimeTug.Engine;
using TimeTug.Platform.Auth;
using TimeTug.Platform.Credentials;
using TimeTug.Platform.Http;
using TimeTug.Platform.Tests.Credentials;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Tests;

public class WindowsEngineHostTests
{
    private sealed class NoForms : IAccountFormService
    {
        public ValueTask<IReadOnlyDictionary<string, string>?> ShowAsync(AccountFormRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>?>(null);
    }

    private sealed class NoLauncher : IUrlLauncher
    {
        public ValueTask<bool> LaunchAsync(string url) => ValueTask.FromResult(false);
    }

    private static WindowsEngineHost Host(RecordingDiagnostics log)
    {
        var loopback = new LoopbackAuthorization(new NoLauncher(), log);
        return new WindowsEngineHost(
            new CredentialLockerStore(new FakePasswordVault(), log),
            HttpClientTransport.CreateDefault(log),
            new CompositeAuthorization(loopback, new CredentialFormPrompter(new NoForms())),
            log);
    }

    [Fact]
    public void Every_member_of_the_engine_host_is_provided_except_the_text_generator()
    {
        var host = Host(new RecordingDiagnostics());
        foreach (var property in typeof(IEngineHost).GetProperties())
        {
            var value = property.GetValue(host);
            if (property.Name == nameof(IEngineHost.TextGenerator))
            {
                value.Should().BeNull("Windows has no on-device text generation");
            }
            else
            {
                value.Should().NotBeNull(property.Name);
            }
        }

        typeof(IEngineHost).GetMethods().Select(m => m.Name).Should().Contain(nameof(IEngineHost.Diagnostic));
        typeof(IEngineHost).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            nameof(IEngineHost.Credentials), nameof(IEngineHost.Http), nameof(IEngineHost.Authorization), nameof(IEngineHost.TextGenerator));
    }

    [Fact]
    public async Task The_services_answer()
    {
        var log = new RecordingDiagnostics();
        var host = Host(log);

        (await host.Credentials.GetAsync("missing", default)).Should().BeNull();
        (await host.Authorization.LastNonSecretValuesAsync(default)).Should().BeNull();
        var session = await host.Authorization.BeginRedirectAsync(default);
        session.RedirectURI.Should().StartWith("http://127.0.0.1:");
        await host.Authorization.CloseRedirectAsync(session.SessionID, default);
        var act = async () => await host.Authorization.PromptCredentialsAsync([new CredentialField("k", "K", false)], default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Cancelled);
    }

    [Fact]
    public void Engine_diagnostics_reach_the_log()
    {
        var log = new RecordingDiagnostics();
        Host(log).Diagnostic(new EngineDiagnostic { Level = "warning", Category = "sync", Message = "A source failed." });
        log.Entries.Should().ContainSingle().Which.Should().Be(("warning", "sync", "A source failed."));
    }

    [Fact]
    public void The_real_services_can_be_composed()
    {
        using var host = WindowsEngineHost.Create(new NoLauncher(), new NoForms(), new RecordingDiagnostics());
        host.Credentials.Should().BeOfType<CredentialLockerStore>();
        host.Http.Should().BeOfType<HttpClientTransport>();
        host.Authorization.Should().BeOfType<CompositeAuthorization>();
    }
}
