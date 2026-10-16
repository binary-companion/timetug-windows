using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using TimeTug.Engine;
using TimeTug.Platform.Http;

namespace TimeTug.Platform.Tests.Http;

public class HttpClientTransportTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        public byte[]? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return await respond(request);
        }
    }

    private static EngineHttpRequest Get(string url) => new("GET", url, new Dictionary<string, string>(), null);

    [Fact]
    public async Task A_connection_refused_becomes_a_network_error()
    {
        var transport = new HttpClientTransport(new HttpClient(new Handler(_ => throw new HttpRequestException("refused"))));
        var act = async () => await transport.SendAsync(Get("https://example.test/"), default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Network);
    }

    [Fact]
    public async Task A_timeout_becomes_a_network_error_but_a_cancelled_token_stays_a_cancellation()
    {
        var timedOut = new HttpClientTransport(new HttpClient(new Handler(_ => throw new TaskCanceledException("timed out"))));
        var act = async () => await timedOut.SendAsync(Get("https://example.test/"), default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Network);

        using var cts = new CancellationTokenSource();
        var slow = new HttpClientTransport(new HttpClient(new Handler(async _ =>
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
            return new HttpResponseMessage();
        })));
        var pending = slow.SendAsync(Get("https://example.test/"), cts.Token).AsTask();
        await cts.CancelAsync();
        await pending.Invoking(p => p).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_dns_failure_becomes_a_network_error()
    {
        var transport = new HttpClientTransport(new HttpClient(new Handler(_ =>
            throw new HttpRequestException("no such host", new SocketException((int)SocketError.HostNotFound)))));
        var act = async () => await transport.SendAsync(Get("https://nowhere.invalid/"), default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Network);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Error_statuses_are_results_not_exceptions(int status)
    {
        var transport = new HttpClientTransport(new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{\"error\":\"x\"}") };
            response.Headers.TryAddWithoutValidation("Retry-After", "7");
            return Task.FromResult(response);
        })));

        var result = await transport.SendAsync(Get("https://example.test/"), default);
        result.Status.Should().Be(status);
        System.Text.Encoding.UTF8.GetString(result.Body!).Should().Be("{\"error\":\"x\"}");
        result.Headers!["Retry-After"].Should().Be("7");
    }

    [Fact]
    public async Task Method_headers_and_body_are_passed_through()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var transport = new HttpClientTransport(new HttpClient(handler));
        var body = new byte[] { 1, 2, 3, 250 };
        await transport.SendAsync(new EngineHttpRequest("POST", "https://example.test/token",
            new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer fake-token",
                ["Content-Type"] = "application/octet-stream",
                ["X-Custom"] = "yes",
            }, body), default);

        handler.Last!.Method.Should().Be(HttpMethod.Post);
        handler.Last.Headers.GetValues("Authorization").Single().Should().Be("Bearer fake-token");
        handler.Last.Headers.GetValues("X-Custom").Single().Should().Be("yes");
        handler.Last.Content!.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        handler.LastBody.Should().Equal(body);
    }

    [Fact]
    public async Task Failures_are_logged_without_the_path_or_query_string()
    {
        var log = new RecordingDiagnostics();
        var transport = new HttpClientTransport(
            new HttpClient(new Handler(_ => throw new HttpRequestException("refused https://example.test/a?code=SECRET"))), log);
        var act = async () => await transport.SendAsync(Get("https://example.test/a/b?code=SECRET&x=1"), default);
        var thrown = (await act.Should().ThrowAsync<EngineServiceException>()).Which;

        log.Entries.Should().ContainSingle();
        var line = log.Entries[0].Message;
        line.Should().Contain("example.test");
        line.Should().NotContain("SECRET").And.NotContain("?").And.NotContain("code=");
        thrown.Message.Should().NotContain("SECRET");
    }

    [Fact]
    public async Task A_malformed_url_fails_cleanly()
    {
        var transport = new HttpClientTransport(new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage()))));
        var act = async () => await transport.SendAsync(Get("not a url"), default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Failed);
    }

    [Fact]
    public void The_default_handler_has_no_cookies_and_the_default_client_is_shared()
    {
        using var handler = HttpClientTransport.CreateHandler();
        handler.UseCookies.Should().BeFalse();
        handler.UseProxy.Should().BeTrue();
        HttpClientTransport.CreateDefault().Should().NotBeNull();
        HttpClientTransport.SharedClient.Should().BeSameAs(HttpClientTransport.SharedClient);
    }

    [Fact]
    public async Task A_real_local_server_round_trips()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer);
            await stream.WriteAsync("HTTP/1.1 418 Teapot\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
        });
        var transport = HttpClientTransport.CreateDefault();
        var result = await transport.SendAsync(Get($"http://127.0.0.1:{port}/"), default);
        await serve;
        listener.Stop();
        result.Status.Should().Be(418);
    }
}
