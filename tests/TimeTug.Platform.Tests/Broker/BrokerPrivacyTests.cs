using System.Diagnostics.Tracing;
using Microsoft.Identity.Client;
using TimeTug.Engine;
using TimeTug.Platform.Broker;
using TimeTug.Platform.Diagnostics;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Tests.Broker;

/// <summary>No access token, email or account id reaches the rolling log, ETW or Copy diagnostics, whatever the broker does, including an exception whose message embeds them.</summary>
public sealed class BrokerPrivacyTests : IDisposable
{
    private const string Token = "eyJ0eXAiOiJKV1QiLCJhbGciOi.SENTINEL-TOKEN-4711.signature";
    private const string Email = "sentinel.person@sentinel-mail.example";
    private const string AccountId = "00000000-4711-4711-4711-000000004711.9188040d-6c67-4c5b-b112-36a304b66dad";
    private static readonly string[] Scopes = ["User.Read", "Calendars.Read"];
    private static readonly DateTimeOffset Expiry = new(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "timetug-broker-privacy-" + Guid.NewGuid().ToString("N"));

    private sealed class Listener : EventListener
    {
        public List<string> Texts { get; } = [];

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "BinaryCompanion-TimeTug")
            {
                EnableEvents(source, EventLevel.Verbose);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            lock (Texts)
            {
                Texts.Add(string.Join(" | ", e.Payload ?? []));
            }
        }
    }

    [Fact]
    public async Task Sentinel_token_email_and_account_id_appear_in_no_rolling_log_etw_or_copy_diagnostics()
    {
        using var listener = new Listener();
        var diagnostics = new FanOutDiagnostics(new RollingFileLog(_directory), EtwDiagnostics.Log);
        var broker = new FakeBrokerClient();
        using var host = new BrokeredTokenHost(broker, new BrokerSignInIntent(), diagnostics);
        var leaky = $"{Email} {Token} {AccountId}";
        var request = new ExternalTokenRequest("conn-1", AccountId, Email, Scopes, false);

        // Every outcome, through the host.
        foreach (var outcome in new BrokerOutcome[]
                 {
                     new BrokerOutcome.Ok(AccountId, Token, Expiry), new BrokerOutcome.UiRequired(leaky), new BrokerOutcome.Blocked(leaky),
                     new BrokerOutcome.Unavailable(leaky), new BrokerOutcome.Network(), new BrokerOutcome.Failed(), new BrokerOutcome.Cancelled(),
                 })
        {
            broker.Silent = outcome;
            broker.Interactive = outcome;
            await Swallow(async () => await host.GetTokenAsync(request, default));
            await Swallow(async () => await host.SignInAsync(new ExternalSignInRequest("sign-" + outcome.GetType().Name, Scopes, AccountId, Email), default));
        }

        // A broker that throws, with the data in the message.
        broker.SilentThrows = new InvalidOperationException(leaky);
        broker.InteractiveThrows = new InvalidOperationException(leaky);
        await Swallow(async () => await host.GetTokenAsync(request, default));
        await Swallow(async () => await host.SignInAsync(new ExternalSignInRequest("sign-throws", Scopes, AccountId, Email), default));

        // The mapper, with MSAL exceptions whose messages and error codes carry the data.
        foreach (var exception in new Exception[]
                 {
                     new MsalServiceException("WAM_provider_error_2156265473", leaky), new MsalUiRequiredException("failed_to_acquire_token_silently_from_broker", leaky),
                     new MsalClientException(Email, leaky), new HttpRequestException(leaky), new DllNotFoundException(leaky),
                 })
        {
            MsalErrorMapper.Map(exception, "silent", diagnostics);
        }

        var lines = new DiagnosticsReader(_directory).ReadLastLines(1000, []);
        var files = Directory.EnumerateFiles(_directory).Select(File.ReadAllText).ToList();
        string[] etw;
        lock (listener.Texts)
        {
            etw = [.. listener.Texts];
        }

        files.Should().NotBeEmpty("the rolling log received the entries");
        lines.Should().NotBeEmpty();
        foreach (var text in files.Concat(lines).Concat(etw))
        {
            text.Should().NotContain(Token).And.NotContain("SENTINEL-TOKEN").And.NotContain(Email).And.NotContain("sentinel.person").And.NotContain(AccountId).And.NotContain("4711");
        }
    }

    private static async Task Swallow(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is OperationCanceledException or EngineServiceException)
        {
            // The outcome decides; the test is about what was logged.
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }
}
