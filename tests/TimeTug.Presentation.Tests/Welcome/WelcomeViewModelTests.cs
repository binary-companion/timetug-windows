using System.Globalization;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Tests.Welcome;

public class WelcomeViewModelTests
{
    private static readonly AccountActivity Working = new(true, null, "browser");
    private static readonly AccountActivity Idle = new(false, null, "browser");

    private static (FakeEngineClient Engine, WelcomeViewModel Model) Create(AppState? first = null, TimeProvider? clock = null)
    {
        // Times are formatted for the current culture; pin it so the assertions read the same on every machine.
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        var engine = new FakeEngineClient();
        var model = new WelcomeViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), clock ?? new FixedClock(States.Noon));
        engine.Push(first ?? States.Empty());
        return (engine, model);
    }

    [Fact]
    public void Shows_at_startup_only_when_there_are_no_accounts()
    {
        WelcomeViewModel.ShouldShowAtStartup(States.Empty()).Should().BeTrue();
        WelcomeViewModel.ShouldShowAtStartup(States.With([States.Google()])).Should().BeFalse();
    }

    [Fact]
    public void Lists_the_engines_kinds_in_the_engines_order_with_how_each_signs_in()
    {
        var (_, model) = Create();

        model.Providers.Select(p => p.Name).Should().Equal("Google", "Microsoft", "iCloud", "Other (CalDAV)");
        model.Providers.Select(p => p.Id).Should().Equal("google", "microsoft", "icloud", "caldav");
        model.Providers[0].Hint.Should().Contain("browser");
        model.Providers[2].Hint.Should().NotContain("browser");
        model.Providers.Should().OnlyContain(p => p.IsEnabled);
        model.Step.Should().Be(WelcomeStep.Connect);
    }

    [Fact]
    public async Task An_unconfigured_kind_is_disabled_with_the_reason()
    {
        var kinds = new List<AccountKindInfo>(States.Kinds) { [0] = new("google", "Google", false, "oauth") };
        var (engine, model) = Create(States.Empty(kinds));

        var google = model.Providers[0];
        google.IsEnabled.Should().BeFalse();
        google.Hint.Should().Contain("Google").And.Contain("set up in this build");
        model.Providers[1].IsEnabled.Should().BeTrue();

        await model.ConnectAsync("google");
        engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public void System_kinds_are_not_offered()
    {
        var kinds = new List<AccountKindInfo>(States.Kinds) { new("eventkit", "Apple Calendar", true, "system") };
        var (_, model) = Create(States.Empty(kinds));

        model.Providers.Select(p => p.Id).Should().NotContain("eventkit");
    }

    [Fact]
    public async Task Connecting_sends_add_account_and_a_new_account_moves_to_the_connected_step()
    {
        var (engine, model) = Create();

        await model.ConnectAsync("google");
        engine.Sent.Should().Equal("add:google");
        model.Accounts.IsBusy.Should().BeTrue();
        model.Accounts.WaitingText.Should().Contain("browser");
        model.Providers.Should().OnlyContain(p => !p.IsEnabled);

        engine.Push(States.Empty(activity: Working));
        model.Step.Should().Be(WelcomeStep.Connect);

        var next = new NextMeetingInfo("Design review", States.Noon.AddMinutes(150), true);
        engine.Push(States.With([States.Google()], next, Idle));

        model.Step.Should().Be(WelcomeStep.Connected);
        model.Accounts.Accounts.Should().ContainSingle().Which.Summary.Should().Be("Google · you@example.com · 3 calendars");
        model.Accounts.NextMeetingText.Should().Contain("Design review").And.Contain("2:30");
        model.Accounts.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task The_summary_says_one_calendar_or_none_yet()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        engine.Push(States.With([States.Google(calendars: 1)]));
        model.Accounts.Accounts[0].Summary.Should().EndWith("1 calendar");

        engine.Push(States.With([States.Google(calendars: 0)]));
        model.Accounts.Accounts[0].Summary.Should().Contain("looking for calendars");
    }

    [Fact]
    public async Task A_meeting_on_another_day_names_the_day()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        var next = new NextMeetingInfo("Planning", States.Noon.AddDays(1), false);
        engine.Push(States.With([States.Google()], next));

        model.Accounts.NextMeetingText.Should().Contain("Planning").And.Contain("Saturday");
    }

    [Fact]
    public async Task No_meeting_says_so()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        engine.Push(States.With([States.Google()]));

        model.Accounts.NextMeetingText.Should().Contain("No upcoming");
    }

    [Fact]
    public async Task An_engine_error_keeps_the_connect_step_and_shows_the_error_beside_that_provider()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        engine.Push(States.Empty(activity: Working));
        engine.Push(States.Empty(activity: new AccountActivity(false, "Sign-in failed: access denied.", "browser")));

        model.Step.Should().Be(WelcomeStep.Connect);
        var google = model.Providers[0];
        google.Error.Should().Contain("Google");
        google.ErrorDetail.Should().Be("Sign-in failed: access denied.");
        google.HasError.Should().BeTrue();
        model.Providers[1].HasError.Should().BeFalse();
        model.Providers.Should().OnlyContain(p => p.IsEnabled);
        model.Accounts.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task A_new_account_whose_status_is_failing_counts_as_a_failed_sign_in()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("microsoft");
        engine.Push(States.With([States.Google(status: "failing")]));

        model.Step.Should().Be(WelcomeStep.Connect);
        model.Providers[1].Error.Should().Contain("Microsoft");
    }

    [Fact]
    public async Task An_engine_diagnostic_for_that_kind_is_a_failed_sign_in()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("icloud");
        engine.Push(new EngineDiagnosticInfo("warning", "accounts", "source for icloud-1 failed: timeout"));

        model.Providers[2].Error.Should().Contain("iCloud");
        model.Accounts.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task A_diagnostic_for_another_kind_or_category_is_ignored()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("icloud");
        engine.Push(new EngineDiagnosticInfo("warning", "accounts", "source for google-1 failed"));
        engine.Push(new EngineDiagnosticInfo("info", "accounts", "icloud ok"));

        model.Providers.Should().OnlyContain(p => !p.HasError);
        model.Accounts.IsBusy.Should().BeTrue();
    }

    [Fact]
    public async Task An_old_error_message_is_not_taken_for_this_attempts_failure()
    {
        var stale = States.Empty(activity: new AccountActivity(false, "Sign-in failed: earlier.", "browser"));
        var (engine, model) = Create(stale);
        await model.ConnectAsync("google");
        engine.Push(stale);

        model.Providers.Should().OnlyContain(p => !p.HasError);
        model.Accounts.IsBusy.Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_sends_account_cancel_and_shows_no_error()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        engine.Push(States.Empty(activity: Working));

        await model.Accounts.CancelAsync();
        engine.Push(States.Empty(activity: Idle));

        engine.Sent.Should().Equal("add:google", "cancel");
        model.Accounts.IsBusy.Should().BeFalse();
        model.Providers.Should().OnlyContain(p => !p.HasError && p.IsEnabled);
        model.Step.Should().Be(WelcomeStep.Connect);
    }

    [Fact]
    public async Task Closing_the_form_ends_the_attempt_quietly()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("icloud");
        engine.Push(States.Empty(activity: new AccountActivity(true, null, "inApp")));
        model.Accounts.WaitingText.Should().Contain("Signing in");

        engine.Push(States.Empty(activity: Idle));

        model.Accounts.IsBusy.Should().BeFalse();
        model.Providers.Should().OnlyContain(p => !p.HasError);
    }

    [Fact]
    public async Task Only_one_sign_in_runs_at_a_time()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        await model.ConnectAsync("microsoft");

        engine.Sent.Should().Equal("add:google");
    }

    [Fact]
    public async Task A_new_attempt_clears_the_old_error()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        engine.Push(States.With([States.Google(status: "failing")]));
        model.Providers[0].HasError.Should().BeTrue();

        await model.ConnectAsync("microsoft");

        model.Providers[0].HasError.Should().BeFalse();
    }

    [Fact]
    public async Task Test_tug_asks_the_engine_for_a_sample_takeover()
    {
        var (engine, model) = Create();
        await model.TestTugAsync();

        engine.Sent.Should().Equal("testTug");
        engine.TestTugs.Should().ContainSingle().Which.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Add_another_returns_to_the_list_and_done_asks_to_close()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        engine.Push(States.With([States.Google()]));
        model.Step.Should().Be(WelcomeStep.Connected);

        await model.AddAnotherAsync();
        model.Step.Should().Be(WelcomeStep.Connect);
        model.Providers.Should().OnlyContain(p => p.IsEnabled);

        var closed = 0;
        model.CloseRequested += (_, _) => closed++;
        model.Done();
        closed.Should().Be(1);
    }

    [Fact]
    public async Task Add_another_opens_the_Accounts_pane_when_the_app_provides_one_and_the_welcome_window_gets_out_of_the_way()
    {
        var opened = 0;
        var engine = new FakeEngineClient();
        var model = new WelcomeViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), new FixedClock(States.Noon), openAccountsPane: () => opened++);
        engine.Push(States.Empty());
        await model.ConnectAsync("google");
        engine.Push(States.With([States.Google()]));
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;

        await model.AddAnotherAsync();

        opened.Should().Be(1, "the pane is opened once");
        closed.Should().Be(1, "and the welcome window closes, since the person carries on in Settings");
        model.Step.Should().Be(WelcomeStep.Connected, "the window is not changed, it is closed");
    }

    [Fact]
    public async Task Skip_while_signing_in_cancels_first_then_closes()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;

        await model.SkipAsync();

        engine.Sent.Should().Equal("add:google", "cancel");
        closed.Should().Be(1);
    }

    [Fact]
    public async Task Skip_when_idle_only_closes()
    {
        var (engine, model) = Create();
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;

        await model.SkipAsync();

        engine.Sent.Should().BeEmpty();
        closed.Should().Be(1);
    }

    [Fact]
    public async Task An_account_that_needs_attention_shows_why()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        engine.Push(States.With([States.Google(status: "authExpired")]));

        model.Accounts.Accounts[0].Problem.Should().Contain("Sign in again");
    }

    [Fact]
    public void Updates_from_another_thread_are_posted_to_the_ui()
    {
        var engine = new FakeEngineClient();
        var posted = new List<Action>();
        using var model = new WelcomeViewModel(engine, new QueueDispatcher(posted), new ReswLocalizer());

        engine.Push(States.Empty());
        model.Providers.Should().BeEmpty();
        posted.ForEach(a => a());
        model.Providers.Should().HaveCount(4);
    }

    private sealed class QueueDispatcher(List<Action> queue) : TimeTug.Presentation.Abstractions.IUiDispatcher
    {
        public bool HasThreadAccess => false;

        public void Post(Action action) => queue.Add(action);

        public Task InvokeAsync(Action action)
        {
            queue.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
