using TimeTug.Engine;
using TimeTug.Platform.Auth;
using TimeTug.Platform.Broker;
using TimeTug.Platform.Tests.Broker;
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

    // ---- the brokered capability (phase 6) --------------------------------------------------------------------------------------

    private sealed class FixedWindow : IBrokerParentWindow
    {
        public nint Handle => 1;
    }

    private static IEngineHost Create(string? clientId, Func<string, IBrokerParentWindow, IBrokerClient?>? factory) =>
        WindowsEngineHost.Create(new NoLauncher(), new NoForms(), new RecordingDiagnostics(), clientId, new FixedWindow(), new TimeTug.Presentation.Accounts.BrokerSignInIntent(), factory);

    [Fact]
    public void Create_without_a_microsoft_client_id_is_not_an_external_token_host()
    {
        var asked = false;
        using var host = (IDisposable)Create(null, (_, _) => { asked = true; return new FakeBrokerClient(); });

        host.Should().NotBeAssignableTo<IExternalTokenHost>();
        asked.Should().BeFalse("no id, no broker client is built");
        ((IEngineHost)host).Should().BeOfType<WindowsEngineHost>();
    }

    [Fact]
    public void Create_with_an_empty_client_id_is_not_an_external_token_host()
    {
        using var host = (IDisposable)Create("  ", (_, _) => new FakeBrokerClient());
        host.Should().NotBeAssignableTo<IExternalTokenHost>();
    }

    [Fact]
    public void Create_when_the_broker_client_cannot_be_built_is_not_an_external_token_host()
    {
        using var host = (IDisposable)Create("client-id-for-test", (_, _) => null);

        host.Should().NotBeAssignableTo<IExternalTokenHost>();
        ((IEngineHost)host).Should().BeOfType<WindowsEngineHost>();
    }

    [Fact]
    public void Create_with_a_client_id_and_a_broker_is_an_external_token_host_that_still_provides_every_service()
    {
        string? seenId = null;
        using var host = (IDisposable)Create("client-id-for-test", (id, _) => { seenId = id; return new FakeBrokerClient(); });

        host.Should().BeAssignableTo<IExternalTokenHost>();
        seenId.Should().Be("client-id-for-test");
        var engineHost = (IEngineHost)host;
        engineHost.Credentials.Should().BeOfType<CredentialLockerStore>();
        engineHost.Http.Should().BeOfType<HttpClientTransport>();
        engineHost.Authorization.Should().BeOfType<CompositeAuthorization>();
        engineHost.TextGenerator.Should().BeNull();
    }

    [Fact]
    public void The_brokered_host_forwards_diagnostics_to_the_plain_host()
    {
        var log = new RecordingDiagnostics();
        using var host = (IDisposable)WindowsEngineHost.Create(
            new NoLauncher(), new NoForms(), log, "client-id-for-test", new FixedWindow(), new TimeTug.Presentation.Accounts.BrokerSignInIntent(), (_, _) => new FakeBrokerClient());

        ((IEngineHost)host).Diagnostic(new EngineDiagnostic { Level = "warning", Category = "sync", Message = "A source failed." });

        log.Entries.Should().Contain(("warning", "sync", "A source failed."));
    }

    [Fact]
    public async Task The_brokered_host_answers_token_requests_through_the_broker()
    {
        var broker = new FakeBrokerClient { Silent = new BrokerOutcome.Ok("acct-1", "token-1", new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero)) };
        using var host = (IDisposable)Create("client-id-for-test", (_, _) => broker);

        var result = await ((IExternalTokenHost)host).GetTokenAsync(new ExternalTokenRequest("c", "acct-1", null, ["User.Read"], false), default);

        result.Status.Should().Be(ExternalTokenStatus.Ok);
    }
}
