using System.Globalization;
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

    private sealed class RecordingNotifier(Action? onReturned = null) : ISignInReturnNotifier
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void SignInReturned()
        {
            Interlocked.Increment(ref _count);
            onReturned?.Invoke();
        }
    }

    private sealed class FixedLocalizer : ILocalizer
    {
        public string GetString(string key) => TryGetString(key) ?? key;

        public string? TryGetString(string key) => key switch
        {
            "Landing_PageTitle" => "TimeTug",
            "Landing_ConnectedTitle" => "Connected from the localizer",
            "Landing_ConnectedBody" => "Go back.",
            "Landing_FailedTitle" => "Not finished",
            "Landing_FailedBody" => "Try again.",
            _ => null,
        };
    }

    private static readonly HttpClient Browser = new();

    private static async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> GetAsync(string url)
    {
        var response = await Browser.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), response);
    }

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
        WebUtility.HtmlDecode(page).Should().Contain("You're connected").And.Contain("Return to TimeTug.");
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

    // ---- the landing page, the stray-request rule and the foreground notice (shared accounts list, W1) -----------------

    [Fact]
    public async Task A_redirect_with_a_code_answers_200_with_the_branded_page_and_raises_the_notifier_once()
    {
        RedirectSession? session = null;
        (HttpStatusCode Status, string Body, HttpResponseMessage Response) answer = default;
        var notifier = new RecordingNotifier();
        var launcher = new FakeLauncher(async _ => answer = await GetAsync(session!.RedirectURI + "?code=abc&state=xyz"));
        using var auth = new LoopbackAuthorization(launcher, notifier: notifier);
        session = await auth.BeginRedirectAsync(default);

        await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);

        await Task.Delay(200);
        answer.Status.Should().Be(HttpStatusCode.OK);
        answer.Body.Should().Contain("<h1>You&#39;re connected</h1>");
        notifier.Count.Should().Be(1);
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task A_redirect_with_an_error_answers_200_with_the_did_not_finish_page_and_raises_the_notifier_once()
    {
        RedirectSession? session = null;
        (HttpStatusCode Status, string Body, HttpResponseMessage Response) answer = default;
        var notifier = new RecordingNotifier();
        var launcher = new FakeLauncher(async _ => answer = await GetAsync(session!.RedirectURI + "?error=access_denied&state=xyz"));
        using var auth = new LoopbackAuthorization(launcher, notifier: notifier);
        session = await auth.BeginRedirectAsync(default);

        var redirect = await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);

        await Task.Delay(200);
        redirect.Should().Contain("error=access_denied");
        answer.Status.Should().Be(HttpStatusCode.OK);
        answer.Body.Should().Contain("Return to TimeTug and try again.").And.NotContain("access_denied");
        notifier.Count.Should().Be(1, "the error page also tells the person to return (decision 3)");
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task A_bare_get_answers_404_and_the_sign_in_keeps_waiting()
    {
        RedirectSession? session = null;
        HttpStatusCode stray = 0;
        var notifier = new RecordingNotifier();
        var launcher = new FakeLauncher(async _ =>
        {
            stray = (await GetAsync(session!.RedirectURI)).Status;
            await GetAsync(session.RedirectURI + "?code=real&state=s");
        });
        using var auth = new LoopbackAuthorization(launcher, notifier: notifier);
        session = await auth.BeginRedirectAsync(default);

        var redirect = await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);

        stray.Should().Be(HttpStatusCode.NotFound);
        redirect.Should().EndWith("/?code=real&state=s");
        notifier.Count.Should().Be(1);
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task A_favicon_request_answers_404_and_does_not_raise_the_notifier()
    {
        RedirectSession? session = null;
        HttpStatusCode favicon = 0;
        var notifier = new RecordingNotifier();
        var launcher = new FakeLauncher(async _ =>
        {
            favicon = (await GetAsync(session!.RedirectURI + "favicon.ico")).Status;
            notifier.Count.Should().Be(0);
            await GetAsync(session.RedirectURI + "?code=real");
        });
        using var auth = new LoopbackAuthorization(launcher, notifier: notifier);
        session = await auth.BeginRedirectAsync(default);

        await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);

        favicon.Should().Be(HttpStatusCode.NotFound);
        notifier.Count.Should().Be(1, "only the real redirect raised it");
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task A_hostile_query_is_never_echoed_and_the_sign_in_completes()
    {
        RedirectSession? session = null;
        string scriptBody = "";
        string bigBody = "";
        var launcher = new FakeLauncher(async _ =>
        {
            scriptBody = (await GetAsync(session!.RedirectURI + "?error=%3Cscript%3Ealert(1)%3C/script%3E")).Body;
        });
        using var auth = new LoopbackAuthorization(launcher);
        session = await auth.BeginRedirectAsync(default);
        var first = await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);
        await Task.Delay(200);
        first.Should().Contain("error=");
        scriptBody.Should().NotContain("<script").And.NotContain("alert").And.Contain("Return to TimeTug and try again.");
        await auth.CloseRedirectAsync(session.SessionID, default);

        // 100 KB of query. HTTP.sys may refuse it before the listener sees it (then the wait goes on); if the listener answers, nothing is echoed.
        using var second = new LoopbackAuthorization(new FakeLauncher(async _ =>
        {
            var response = await Browser.GetAsync(session!.RedirectURI + "?code=" + new string('A', 100_000) + "<script>");
            bigBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.OK)
            {
                await GetAsync(session.RedirectURI + "?code=real");
            }
        }));
        session = await second.BeginRedirectAsync(default);

        var redirect = await second.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);

        await Task.Delay(200);
        redirect.Should().Contain("code=");
        bigBody.Should().NotContain("<script").And.NotContain("AAAA");
        await second.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task Stray_requests_do_not_end_the_wait()
    {
        RedirectSession? session = null;
        var statuses = new List<HttpStatusCode>();
        var launcher = new FakeLauncher(async _ =>
        {
            string[] strays = ["", "?state=x", "?code=", "?error=", "other", "other?code=zzz", "favicon.ico", "?a=b", "robots.txt", "?code=&error="];
            foreach (var stray in strays)
            {
                statuses.Add((await GetAsync(session!.RedirectURI + stray)).Status);
            }

            await GetAsync(session!.RedirectURI + "?code=real&state=s");
        });
        using var auth = new LoopbackAuthorization(launcher);
        session = await auth.BeginRedirectAsync(default);

        var redirect = await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);

        statuses.Should().HaveCount(10).And.OnlyContain(s => s == HttpStatusCode.NotFound);
        redirect.Should().EndWith("/?code=real&state=s");
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task Cancelling_the_wait_raises_no_notifier()
    {
        var notifier = new RecordingNotifier();
        using var auth = new LoopbackAuthorization(new FakeLauncher(), notifier: notifier);
        var session = await auth.BeginRedirectAsync(default);
        using var cts = new CancellationTokenSource();
        var pending = auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", cts.Token).AsTask();
        await Task.Delay(100);
        await cts.CancelAsync();
        await pending.Invoking(p => p).Should().ThrowAsync<EngineServiceException>();
        notifier.Count.Should().Be(0);
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task A_notifier_that_throws_does_not_break_the_sign_in()
    {
        RedirectSession? session = null;
        var log = new RecordingDiagnostics();
        var launcher = new FakeLauncher(async _ => await GetAsync(session!.RedirectURI + "?code=real"));
        using var auth = new LoopbackAuthorization(launcher, log, notifier: new RecordingNotifier(() => throw new InvalidOperationException("sentinel-notifier-detail")));
        session = await auth.BeginRedirectAsync(default);

        var redirect = await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);

        redirect.Should().Contain("code=real");
        log.Entries.Should().ContainSingle(e => e.Category == "signin");
        log.Entries[0].Message.Should().NotContain("sentinel-notifier-detail");
        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task Every_answer_carries_the_four_headers()
    {
        RedirectSession? session = null;
        var answers = new List<HttpResponseMessage>();
        var launcher = new FakeLauncher(async _ =>
        {
            answers.Add((await GetAsync(session!.RedirectURI)).Response);
            answers.Add((await GetAsync(session.RedirectURI + "?code=real")).Response);
        });
        using var auth = new LoopbackAuthorization(launcher);
        session = await auth.BeginRedirectAsync(default);

        await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);
        await Task.Delay(200);

        answers.Should().HaveCount(2);
        foreach (var response in answers)
        {
            foreach (var (name, value) in SignInLandingPage.Headers)
            {
                var actual = response.Headers.TryGetValues(name, out var values) ? values : response.Content.Headers.GetValues(name);
                actual.Should().ContainSingle().Which.Should().Be(value, name);
            }
        }

        await auth.CloseRedirectAsync(session.SessionID, default);
    }

    [Fact]
    public async Task The_page_words_come_from_the_localizer_and_the_language_from_the_current_ui_culture()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("ar-SA");
        try
        {
            RedirectSession? session = null;
            (HttpStatusCode Status, string Body, HttpResponseMessage Response) answer = default;
            var launcher = new FakeLauncher(async _ => answer = await GetAsync(session!.RedirectURI + "?code=real"));
            using var auth = new LoopbackAuthorization(launcher, localizer: new FixedLocalizer());
            session = await auth.BeginRedirectAsync(default);

            await auth.AuthorizeAsync(session.SessionID, "https://accounts.example.test/auth", default);
            await Task.Delay(200);

            answer.Body.Should().Contain("<html lang=\"ar-SA\" dir=\"rtl\"").And.Contain("Connected from the localizer").And.Contain("Go back.");
            await auth.CloseRedirectAsync(session.SessionID, default);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
