using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tests.Settings;
using TimeTug.Presentation.Welcome;
using Pkg = TimeTug.Engine;

namespace TimeTug.Presentation.Tests.Welcome;

/// <summary>
/// The first-run window has four steps: Welcome, Connect your calendars, You're connected (the calendars, each with its Tug switch) and Keep TimeTug handy
/// (start at sign-in, the taskbar). Every choice is saved as it is made.
/// </summary>
public sealed class WelcomeFourStepsTests
{
    private static Pkg.CalendarInfo Cal(string id, string title, string source = "google-g1") => SettingsPaneRig.Cal(source, id, title, "me@gmail.com", "standard", "#112233");

    private static readonly IReadOnlyList<Pkg.CalendarInfo> Basic = [Cal("work", "Work"), Cal("home", "Home")];

    private sealed class Window : IDisposable
    {
        public Window(IReadOnlyList<AccountInfo>? accounts = null, IReadOnlyList<Pkg.CalendarInfo>? calendars = null, bool withCalendars = true, bool withLaunch = false)
        {
            Rig = new SettingsPaneRig(calendars: calendars ?? Basic, accounts: accounts);
            Startup = new FakeStartupTask();
            var launch = withLaunch ? new LaunchAtLoginViewModel(Startup, new FakeSystemLauncher(), Rig.Ui, Rig.Text) : null;
            var calendarModel = withCalendars ? new CalendarsViewModel(Rig.Engine, Rig.Client, Rig.Ui, Rig.Text, inferenceSupported: false, showAccountProblems: true) : null;
            Model = new WelcomeViewModel(Rig.Engine, Rig.Ui, Rig.Text, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(States.Noon), launchAtLogin: launch, calendars: calendarModel);
        }

        public SettingsPaneRig Rig { get; }

        public FakeStartupTask Startup { get; }

        public WelcomeViewModel Model { get; }

        /// <summary>Gets to the "You're connected" step the way a person does: Get started, then Continue with an account in the list.</summary>
        public async Task ToConnectedAsync()
        {
            Model.GetStartedCommand.Execute(null);
            await Model.ContinueCommand.ExecuteAsync(null);
        }

        public void Dispose()
        {
            Model.Dispose();
            Rig.Dispose();
        }
    }

    [Fact]
    public async Task The_window_walks_four_steps_in_order_each_with_its_own_button()
    {
        using var window = new Window(withLaunch: true);
        var model = window.Model;
        var closed = 0;
        model.CloseRequested += (_, _) => closed++;

        model.Step.Should().Be(WelcomeStep.Intro);
        model.GetStartedText.Should().Be("Get started");
        model.GetStartedCommand.CanExecute(null).Should().BeTrue();
        model.ConnectedContinueCommand.CanExecute(null).Should().BeFalse();

        model.GetStartedCommand.Execute(null);
        model.Step.Should().Be(WelcomeStep.Connect);
        model.ContinueText.Should().Be("Continue");
        model.ConnectedContinueCommand.CanExecute(null).Should().BeFalse("the connected step's button is not live on the connect step");

        await model.ContinueCommand.ExecuteAsync(null);
        model.Step.Should().Be(WelcomeStep.Connected);
        model.IsConnectedStep.Should().BeTrue();
        model.ConnectedContinueText.Should().Be("Continue", "the third step says Continue, not Done");
        model.ConnectedContinueCommand.CanExecute(null).Should().BeTrue();
        model.GetStartedCommand.CanExecute(null).Should().BeFalse();
        closed.Should().Be(0, "nothing closes the window before the last step");

        model.ConnectedContinueCommand.Execute(null);
        model.Step.Should().Be(WelcomeStep.Handy);
        model.IsHandyStep.Should().BeTrue();
        model.IsConnectedStep.Should().BeFalse();
        model.ConnectedContinueCommand.CanExecute(null).Should().BeFalse("and the button is for that step only");
        closed.Should().Be(0);

        model.DoneCommand.Execute(null);
        closed.Should().Be(1, "Done on the last step closes the window");
    }

    [Fact]
    public async Task Nothing_for_the_last_step_runs_before_it_is_shown()
    {
        using var window = new Window(withLaunch: true);
        var model = window.Model;

        model.GetStartedCommand.Execute(null);
        await model.ContinueCommand.ExecuteAsync(null);

        model.Step.Should().Be(WelcomeStep.Connected);
        model.ShowLaunchAtLogin.Should().BeFalse("the checkbox belongs to the last step");
        window.Startup.Calls.Should().BeEmpty("Windows is not asked about the startup task before the last step");

        model.ConnectedContinueCommand.Execute(null);
        model.ShowLaunchAtLogin.Should().BeTrue();
        window.Startup.Calls.Should().Equal("get");
    }

    [Fact]
    public async Task The_connected_step_lists_the_calendars_found()
    {
        using var window = new Window();
        await window.ToConnectedAsync();

        var calendars = window.Model.Calendars!;
        var section = calendars.Sections.Should().ContainSingle().Subject;
        section.Title.Should().Be("Google");
        section.Groups.Single().Rows.Select(r => r.Title).Should().Equal("Home", "Work");
        calendars.IsEmpty.Should().BeFalse();
        window.Model.CalendarsIntroText.Should().Be("Choose which calendars are allowed to tug you away from your work.");
        window.Model.CalendarsEmptyText.Should().StartWith("No calendars found yet");
    }

    [Fact]
    public async Task A_tug_switch_is_saved_at_once_and_closing_the_window_keeps_it()
    {
        using var window = new Window();
        await window.ToConnectedAsync();
        var calendars = window.Model.Calendars!;
        calendars.Sections.Single().Groups.Single().Rows.Should().OnlyContain(r => !r.Tugs, "nothing is preset");

        await calendars.SetCalendarTugAsync("google-g1/work", true);
        await calendars.SetGroupTugAsync("google-g1", "me@gmail.com", true);

        window.Rig.Settings.TakeoverCalendarKeys.Should().BeEquivalentTo("google-g1/work", "google-g1/home");
        window.Model.Dispose();
        window.Rig.Settings.TakeoverCalendarKeys.Should().HaveCount(2, "the choices were saved when they were made, not when the window closed");
    }

    [Fact]
    public async Task Hiding_a_calendar_follows_the_Settings_rules()
    {
        using var window = new Window();
        await window.ToConnectedAsync();
        var calendars = window.Model.Calendars!;
        await calendars.SetCalendarTugAsync("google-g1/work", true);

        await calendars.SetCalendarShownAsync("google-g1/work", false);

        window.Rig.Settings.HiddenCalendarKeys.Should().BeEquivalentTo("google-g1/work");
        window.Rig.Settings.TakeoverCalendarKeys.Should().BeEmpty("a calendar that is not shown never tugs (the same key lists as Settings)");
        calendars.Sections.Single().Groups.Single().Rows.Single(r => r.Key == "google-g1/work").TugEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task An_account_that_needs_the_person_says_so_on_its_header()
    {
        using var window = new Window(accounts: [new AccountInfo("g1", "google", "me@gmail.com", "authExpired", 2)]);
        await window.ToConnectedAsync();

        var section = window.Model.Calendars!.Sections.Should().ContainSingle().Subject;

        section.Problem.Should().Be("Sign in again");
        section.Groups.Should().NotBeEmpty("its calendars are still listed");
    }

    [Fact]
    public async Task An_account_that_is_fine_has_no_line()
    {
        using var window = new Window();
        await window.ToConnectedAsync();

        window.Model.Calendars!.Sections.Single().Problem.Should().BeNull();
    }

    [Fact]
    public async Task An_account_with_a_problem_and_no_calendars_still_gets_its_header()
    {
        using var window = new Window(accounts: [new AccountInfo("g1", "google", "me@gmail.com", "authExpired", 0)], calendars: []);
        await window.ToConnectedAsync();

        var calendars = window.Model.Calendars!;
        var section = calendars.Sections.Should().ContainSingle().Subject;
        (section.Title, section.SubtitleText, section.Problem).Should().Be(("Google", "· me@gmail.com · connected directly", "Sign in again"));
        section.Groups.Should().BeEmpty();
        calendars.IsEmpty.Should().BeFalse("the header with its line is something to show");
    }

    [Fact]
    public void An_account_that_cannot_start_says_so_on_its_header_too()
    {
        using var window = new Window(accounts: [new AccountInfo("g1", "google", "me@gmail.com", "failing", 0) { BuildFailure = true }], calendars: []);

        window.Model.Calendars!.Sections.Single().Problem.Should().Be("Can’t start this account");
    }

    [Fact]
    public void Settings_does_not_get_the_line()
    {
        using var rig = new SettingsPaneRig(calendars: Basic, accounts: [new AccountInfo("g1", "google", "me@gmail.com", "authExpired", 2)]);
        using var settings = new CalendarsViewModel(rig.Engine, rig.Client, rig.Ui, rig.Text, inferenceSupported: false);

        settings.Sections.Single().Problem.Should().BeNull("the Accounts pane carries the status there");
    }

    [Fact]
    public async Task A_Microsoft_account_that_signs_in_through_Windows_is_listed_as_Microsoft()
    {
        using var window = new Window(
            accounts: [new AccountInfo("m1", "microsoft-brokered", "me@example.com", "ok", 1)],
            calendars: [SettingsPaneRig.Cal("microsoft-brokered-m1", "cal", "Calendar", "me@example.com", "standard", "#112233")]);
        await window.ToConnectedAsync();

        window.Model.Calendars!.Sections.Single().Title.Should().Be("Microsoft");
    }

    [Fact]
    public async Task The_list_follows_the_engine_when_accounts_change()
    {
        using var window = new Window();
        await window.ToConnectedAsync();

        window.Rig.SetCalendars([.. Basic, Cal("team", "Team")]);

        window.Model.Calendars!.Sections.Single().Groups.Single().Rows.Select(r => r.Title).Should().Equal("Home", "Team", "Work");
    }

    [Fact]
    public async Task Without_a_calendars_model_the_step_lists_nothing_and_still_goes_on()
    {
        using var window = new Window(withCalendars: false);
        await window.ToConnectedAsync();

        window.Model.Calendars.Should().BeNull();
        window.Model.ConnectedContinueCommand.Execute(null);
        window.Model.Step.Should().Be(WelcomeStep.Handy);
    }
}
