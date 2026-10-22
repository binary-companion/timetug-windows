using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tests.Settings;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Tests.Welcome;

/// <summary>The "Start TimeTug when I sign in" checkbox on the welcome window's second step.</summary>
public sealed class WelcomeLaunchAtLoginTests
{
    private readonly FakeEngineClient _engine = new();
    private readonly FakeStartupTask _startup = new();

    private WelcomeViewModel Model(bool withLaunchAtLogin = true)
    {
        var text = new ReswLocalizer();
        var launch = withLaunchAtLogin ? new LaunchAtLoginViewModel(_startup, new FakeSystemLauncher(), new ImmediateDispatcher(), text) : null;
        var model = new WelcomeViewModel(_engine, new ImmediateDispatcher(), text, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(States.Noon), launchAtLogin: launch);
        _engine.Push(States.Empty());
        return model;
    }

    private async Task ReachConnectedAsync(WelcomeViewModel model)
    {
        await model.ConnectAsync("google");
        _engine.Push(States.With([States.Google()]));
    }

    [Fact]
    public async Task The_checkbox_is_unchecked_and_asks_nothing_of_Windows_until_the_person_ticks_it()
    {
        var model = Model();

        await ReachConnectedAsync(model);

        model.Step.Should().Be(WelcomeStep.Connected);
        model.ShowLaunchAtLogin.Should().BeTrue();
        model.LaunchAtLogin!.IsOn.Should().BeFalse("it starts unchecked");
        model.LaunchAtLogin.CanChange.Should().BeTrue();
        _startup.Calls.Should().Equal("get");
        _startup.State.Should().Be(StartupState.Disabled, "and nothing was enabled");
    }

    [Fact]
    public async Task The_checkbox_is_not_shown_on_the_first_step()
    {
        var model = Model();

        model.Step.Should().Be(WelcomeStep.Connect);
        model.ShowLaunchAtLogin.Should().BeFalse();
        _startup.Calls.Should().BeEmpty("the state is read when the second step appears");
        await ReachConnectedAsync(model);
        model.ShowLaunchAtLogin.Should().BeTrue();
    }

    [Fact]
    public async Task A_tick_requests_enabling()
    {
        var model = Model();
        await ReachConnectedAsync(model);

        await model.SetLaunchAtLoginAsync(true);

        _startup.Calls.Should().Equal("get", "enable");
        model.LaunchAtLogin!.IsOn.Should().BeTrue();
        _startup.State.Should().Be(StartupState.Enabled);
    }

    [Fact]
    public async Task Unticking_turns_it_off_again()
    {
        var model = Model();
        await ReachConnectedAsync(model);
        await model.SetLaunchAtLoginAsync(true);

        await model.SetLaunchAtLoginAsync(false);

        _startup.Calls.Should().Equal("get", "enable", "disable");
        model.LaunchAtLogin!.IsOn.Should().BeFalse();
    }

    [Fact]
    public async Task A_tick_that_Windows_refuses_leaves_the_checkbox_unchecked_with_the_reason()
    {
        var model = Model();
        await ReachConnectedAsync(model);
        _startup.StateAfterRequest = StartupState.DisabledByUser;

        await model.SetLaunchAtLoginAsync(true);

        model.LaunchAtLogin!.IsOn.Should().BeFalse();
        model.LaunchAtLogin.ShowSettingsLink.Should().BeTrue();
        model.LaunchAtLogin.Note.Should().Contain("Startup apps");
    }

    [Fact]
    public async Task When_the_task_is_already_on_the_checkbox_says_so_and_is_not_asked_again()
    {
        _startup.State = StartupState.Enabled;
        var model = Model();

        await ReachConnectedAsync(model);

        model.LaunchAtLogin!.IsOn.Should().BeTrue("the checkbox shows what Windows has");
        _startup.Calls.Should().Equal("get");
    }

    [Fact]
    public async Task Without_a_launch_at_login_model_the_window_has_no_checkbox()
    {
        var model = Model(withLaunchAtLogin: false);

        await ReachConnectedAsync(model);

        model.ShowLaunchAtLogin.Should().BeFalse();
        await model.SetLaunchAtLoginAsync(true);
        _startup.Calls.Should().BeEmpty();
    }
}
