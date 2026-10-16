using TimeTug.Engine;
using TimeTug.Platform.Auth;
using TimeTug.Presentation.Accounts;

namespace TimeTug.Platform.Tests.Auth;

public class CredentialFormPrompterTests
{
    private sealed class FakeForms(Func<AccountFormRequest, CancellationToken, Task<IReadOnlyDictionary<string, string>?>> show) : IAccountFormService
    {
        public List<AccountFormRequest> Requests { get; } = [];

        public async ValueTask<IReadOnlyDictionary<string, string>?> ShowAsync(AccountFormRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await show(request, cancellationToken);
        }
    }

    private static readonly CredentialField[] Fields =
    [
        new("server", "Server", false),
        new("username", "User name", false),
        new("password", "Password", true),
    ];

    private static FakeForms Submits(Dictionary<string, string> values) => new((_, _) => Task.FromResult<IReadOnlyDictionary<string, string>?>(values));

    [Fact]
    public async Task The_prepared_form_is_shown_with_non_secret_prefill_only()
    {
        var forms = Submits(new() { ["server"] = "caldav.example.test", ["username"] = "pat", ["password"] = "fake-password" });
        var prompter = new CredentialFormPrompter(forms);
        await prompter.PrepareSignInAsync(new SignInForm("Sign in to CalDAV", new CredentialHelp("Use an app password", "Help", "https://example.test/help"),
            new Dictionary<string, string> { ["server"] = "caldav.example.test", ["password"] = "fake-old-password" }, "Wrong password"), default);

        await prompter.PromptCredentialsAsync(Fields, default);

        var shown = forms.Requests.Single();
        shown.Title.Should().Be("Sign in to CalDAV");
        shown.Help!.Url.Should().Be("https://example.test/help");
        shown.Error.Should().Be("Wrong password");
        shown.Fields.Select(f => f.Key).Should().Equal("server", "username", "password");
        shown.Fields.Single(f => f.Key == "password").IsSecret.Should().BeTrue();
        shown.Values.Should().BeEquivalentTo(new Dictionary<string, string> { ["server"] = "caldav.example.test" });
    }

    [Fact]
    public async Task The_engine_gets_everything_entered_but_only_non_secret_values_are_kept()
    {
        var prompter = new CredentialFormPrompter(Submits(new() { ["server"] = "caldav.example.test", ["username"] = "pat", ["password"] = "fake-password" }));
        (await prompter.LastNonSecretValuesAsync(default)).Should().BeNull();

        var entered = await prompter.PromptCredentialsAsync(Fields, default);

        entered["password"].Should().Be("fake-password");
        var kept = await prompter.LastNonSecretValuesAsync(default);
        kept.Should().BeEquivalentTo(new Dictionary<string, string> { ["server"] = "caldav.example.test", ["username"] = "pat" });
        kept!.Values.Should().NotContain("fake-password");
    }

    [Fact]
    public async Task Preparing_again_forgets_the_last_submission()
    {
        var prompter = new CredentialFormPrompter(Submits(new() { ["username"] = "pat", ["password"] = "x" }));
        await prompter.PromptCredentialsAsync(Fields, default);
        await prompter.PrepareSignInAsync(new SignInForm("Again", null, new Dictionary<string, string>(), null), default);
        (await prompter.LastNonSecretValuesAsync(default)).Should().BeNull();
    }

    [Fact]
    public async Task A_cancelled_form_is_a_cancelled_service_error_and_keeps_nothing()
    {
        var prompter = new CredentialFormPrompter(new FakeForms((_, _) => Task.FromResult<IReadOnlyDictionary<string, string>?>(null)));
        var act = async () => await prompter.PromptCredentialsAsync(Fields, default);
        (await act.Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Cancelled);
        (await prompter.LastNonSecretValuesAsync(default)).Should().BeNull();
    }

    [Fact]
    public async Task Cancelling_the_token_cancels_the_form()
    {
        var prompter = new CredentialFormPrompter(new FakeForms(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }));
        using var cts = new CancellationTokenSource();
        var pending = prompter.PromptCredentialsAsync(Fields, cts.Token).AsTask();
        await cts.CancelAsync();
        (await pending.Invoking(p => p).Should().ThrowAsync<EngineServiceException>()).Which.Kind.Should().Be(ServiceErrorKind.Cancelled);
    }
}
