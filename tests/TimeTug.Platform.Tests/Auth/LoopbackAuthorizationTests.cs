using System.Net;
using System.Net.Sockets;
using TimeTug.Engine;
using TimeTug.Platform.Auth;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Tests.Auth;

public class LoopbackAuthorizationTests
{
    private sealed class FakeLauncher(Func<string, Task>? onOpen = null, bool result = true) : IUrlLauncher
    {
        public List<string> Opened { get; } = [];

        public ValueTask<bool> LaunchAsync(string url)
        {
            Opened.Add(url);
            if (onOpen is not null)
            {
                _ = onOpen(url);
            }

            return ValueTask.FromResult(result);
        }
    }

    private static readonly HttpClient Browser = new();

    private static async Task<string> BrowseAsync(string url)
    {
        var response = await Browser.GetAsync(url);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Loopback_returns_the_redirect_url_and_answers_the_browser()
    {
        string? page = null;
        RedirectSession? session = null;
        var launcher = new FakeLauncher(async _ => page = await BrowseAsync(session!.RedirectURI + "?code=abc&state=xyz"));
        using var auth = new LoopbackAuthorization(launcher);
        session = await auth.BeginRedirectAsync(default);

        var redirect = await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);

        redirect.Should().StartWith(session.RedirectURI.TrimEnd('/')).And.Contain("code=abc").And.Contain("state=xyz");
        launcher.Opened.Should().Equal("https://accounts.example.test/auth");
        await Task.Delay(200);
        page.Should().Contain("You can close this tab");
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task The_redirect_uri_is_an_explicit_loopback_address_with_a_port()
    {
        using var auth = new LoopbackAuthorization(new FakeLauncher());
        var session = await auth.BeginRedirectAsync(default);
        var uri = new Uri(session.RedirectURI);
        uri.Host.Should().Be("127.0.0.1");
        uri.Port.Should().BeGreaterThan(0);
        uri.Scheme.Should().Be("http");
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task A_favicon_request_is_not_taken_for_the_redirect()
    {
        RedirectSession? session = null;
        var launcher = new FakeLauncher(async _ =>
        {
            await Browser.GetAsync(session!.RedirectURI + "favicon.ico");
            await BrowseAsync(session.RedirectURI + "?code=real");
        });
        using var auth = new LoopbackAuthorization(launcher);
        session = await auth.BeginRedirectAsync(default);
        (await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default)).Should().Contain("code=real");
    }

    [Fact]
    public async Task Cancellation_cancels_the_wait()
    {
        using var auth = new LoopbackAuthorization(new FakeLauncher());
        var session = await auth.BeginRedirectAsync(default);
        using var cts = new CancellationTokenSource();
        var pending = auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", cts.Token).AsTask();
        await Task.Delay(100);
        await cts.CancelAsync();
        (await pending.Invoking(p => p).Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Cancelled);
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task A_closed_session_is_not_found_and_the_port_is_released()
    {
        using var auth = new LoopbackAuthorization(new FakeLauncher());
        var session = await auth.BeginRedirectAsync(default);
        var port = new Uri(session.RedirectURI).Port;
        await auth.CloseRedirectAsync(session.SessionID, default);

        var act = async () => await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.NotFound);

        var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start();   // would throw if the listener still held the port
        probe.Stop();
        await auth.CloseRedirectAsync(session.SessionID, default);   // closing twice is harmless
    }

    [Fact]
    public async Task An_unknown_session_is_not_found()
    {
        using var auth = new LoopbackAuthorization(new FakeLauncher());
        var act = async () => await auth.AuthorizeAsync("nope", "https://accounts.example.test/auth", default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.NotFound);
    }

    [Fact]
    public async Task A_browser_that_will_not_open_fails_with_a_diagnostic()
    {
        var log = new RecordingDiagnostics();
        using var auth = new LoopbackAuthorization(new FakeLauncher(result: false), log);
        var session = await auth.BeginRedirectAsync(default);
        var act = async () => await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth?secret=1", default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Failed);
        log.Entries.Should().ContainSingle(e => e.Category == "signin");
        log.Entries[0].Message.Should().NotContain("secret=1");
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task Sessions_get_different_ports_and_ids()
    {
        using var auth = new LoopbackAuthorization(new FakeLauncher());
        var a = await auth.BeginRedirectAsync(default);
        var b = await auth.BeginRedirectAsync(default);
        a.SessionID.Should().NotBe(b.SessionID);
        a.RedirectURI.Should().NotBe(b.RedirectURI);
    }
}
