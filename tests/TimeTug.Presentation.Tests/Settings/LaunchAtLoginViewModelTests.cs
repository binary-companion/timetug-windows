using System.ComponentModel;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

public sealed class LaunchAtLoginViewModelTests
{
    private readonly FakeStartupTask _startup = new();
    private readonly FakeSystemLauncher _launcher = new();

    private LaunchAtLoginViewModel Model() => new(_startup, _launcher, new ImmediateDispatcher(), new ReswLocalizer());

    [Fact]
    public void Before_Windows_has_been_asked_nothing_is_claimed_and_the_switch_cannot_be_used()
    {
        var model = Model();

        model.State.Should().BeNull();
        model.IsOn.Should().BeFalse();
        model.CanChange.Should().BeFalse("the real state is not known yet");
        _startup.Calls.Should().BeEmpty("a view model asks Windows only when it is told to refresh");
    }

    [Theory]
    [InlineData(StartupState.Enabled, true, true, false)]
    [InlineData(StartupState.Disabled, false, true, false)]
    [InlineData(StartupState.DisabledByUser, false, false, true)]
    [InlineData(StartupState.DisabledByPolicy, false, false, true)]
    [InlineData(StartupState.EnabledByPolicy, true, false, false)]
    public async Task Every_state_maps_to_the_switch_and_the_link(StartupState state, bool on, bool canChange, bool link)
    {
        _startup.State = state;
        var model = Model();

        await model.RefreshAsync();

        model.State.Should().Be(state);
        model.IsOn.Should().Be(on);
        model.CanChange.Should().Be(canChange);
        model.ShowSettingsLink.Should().Be(link);
    }

    [Fact]
    public async Task A_state_the_user_set_in_Windows_is_explained_with_the_Startup_apps_link()
    {
        _startup.State = StartupState.DisabledByUser;
        var model = Model();

        await model.RefreshAsync();

        model.Note.Should().Be("Windows is blocking TimeTug from starting when you sign in. Turn it on in Windows Settings under Startup apps.");
        model.HasNote.Should().BeTrue();
        model.SettingsLinkText.Should().Be("Open Startup apps");

        await model.OpenStartupSettingsAsync();

        _launcher.Opened.Should().Equal("ms-settings:startupapps");
    }

    [Fact]
    public async Task A_state_set_by_policy_says_so()
    {
        _startup.State = StartupState.DisabledByPolicy;
        var model = Model();
        await model.RefreshAsync();
        model.Note.Should().Be("A policy on this PC (set by your organization) stops TimeTug from starting when you sign in.");

        _startup.State = StartupState.EnabledByPolicy;
        await model.RefreshAsync();
        model.Note.Should().Be("A policy on this PC (set by your organization) starts TimeTug when you sign in.");
        model.IsOn.Should().BeTrue();
        model.CanChange.Should().BeFalse("only the policy can turn it off");
    }

    [Fact]
    public async Task The_settings_link_is_the_one_that_opens_Startup_apps_and_a_refused_launch_is_reported()
    {
        _startup.State = StartupState.DisabledByUser;
        _launcher.Succeeds = false;
        var model = Model();
        await model.RefreshAsync();

        await model.OpenStartupSettingsAsync();

        LaunchAtLoginViewModel.StartupSettingsUri.Should().Be("ms-settings:startupapps");
        model.Error.Should().Be("Couldn’t open Windows Settings. Open Settings, then Apps, then Startup.");
    }

    [Fact]
    public async Task The_state_is_read_from_Windows_every_time()
    {
        var model = Model();
        await model.RefreshAsync();
        model.IsOn.Should().BeFalse();

        _startup.State = StartupState.Enabled;
        await model.RefreshAsync();
        model.IsOn.Should().BeTrue("someone turned it on outside the app");

        _startup.State = StartupState.DisabledByUser;
        await model.RefreshAsync();
        model.IsOn.Should().BeFalse("and the person turned it off again in Task Manager");
        model.ShowSettingsLink.Should().BeTrue();
        _startup.Calls.Should().Equal("get", "get", "get");
    }

    [Fact]
    public async Task Turning_it_on_asks_Windows_and_shows_what_Windows_says()
    {
        var model = Model();
        await model.RefreshAsync();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await model.SetAsync(true);

        _startup.Calls.Should().Equal("get", "enable");
        model.State.Should().Be(StartupState.Enabled);
        model.IsOn.Should().BeTrue();
        model.Error.Should().BeNull();
        raised.Should().Contain(nameof(LaunchAtLoginViewModel.IsOn));
    }

    [Fact]
    public async Task Turning_it_on_never_shows_on_when_the_person_has_blocked_it()
    {
        var model = Model();
        await model.RefreshAsync();
        _startup.StateAfterRequest = StartupState.DisabledByUser;

        await model.SetAsync(true);

        model.IsOn.Should().BeFalse("Windows left it disabled, and the switch says what Windows says");
        model.State.Should().Be(StartupState.DisabledByUser);
        model.ShowSettingsLink.Should().BeTrue("with the way to turn it on");
        model.Note.Should().Contain("Startup apps");
    }

    [Fact]
    public async Task Turning_it_on_when_Windows_answers_disabled_without_a_reason_says_it_did_not_work()
    {
        var model = Model();
        await model.RefreshAsync();
        _startup.StateAfterRequest = StartupState.Disabled;

        await model.SetAsync(true);

        model.IsOn.Should().BeFalse();
        model.Error.Should().Be("Windows didn’t turn on launch at login.");
    }

    [Fact]
    public async Task When_the_system_refuses_the_request_the_switch_stays_off_and_the_reason_is_shown()
    {
        var model = Model();
        await model.RefreshAsync();
        _startup.ChangeThrows = new InvalidOperationException("The system cannot find the file specified.");

        await model.SetAsync(true);

        model.IsOn.Should().BeFalse();
        model.Error.Should().Be("Couldn’t change launch at login: The system cannot find the file specified.");
        _startup.Calls.Should().Contain("enable");
    }

    [Fact]
    public async Task A_failed_request_reads_the_real_state_again_so_the_switch_never_lies()
    {
        var model = Model();
        _startup.State = StartupState.Enabled;
        await model.RefreshAsync();
        model.IsOn.Should().BeTrue();
        _startup.ChangeThrows = new InvalidOperationException("boom");
        _startup.Calls.Clear();

        await model.SetAsync(false);

        model.Error.Should().StartWith("Couldn’t change launch at login");
        _startup.Calls.Should().Equal("disable", "get");
        model.State.Should().Be(StartupState.Enabled, "the state is what Windows says now, read again");
    }

    [Fact]
    public async Task Turning_it_off_asks_Windows_to_disable_it()
    {
        _startup.State = StartupState.Enabled;
        var model = Model();
        await model.RefreshAsync();

        await model.SetAsync(false);

        _startup.Calls.Should().Equal("get", "disable");
        model.IsOn.Should().BeFalse();
        model.State.Should().Be(StartupState.Disabled);
    }

    [Fact]
    public async Task Turning_it_off_when_it_stays_on_says_so()
    {
        _startup.State = StartupState.Enabled;
        var model = Model();
        await model.RefreshAsync();
        _startup.Calls.Clear();
        // Windows refuses to disable (for example a policy appeared since).
        var stuck = new StuckEnabled(_startup);
        var stubborn = new LaunchAtLoginViewModel(stuck, _launcher, new ImmediateDispatcher(), new ReswLocalizer());
        await stubborn.RefreshAsync();

        await stubborn.SetAsync(false);

        stubborn.IsOn.Should().BeTrue();
        stubborn.Error.Should().Be("Windows didn’t turn off launch at login.");
    }

    [Theory]
    [InlineData(StartupState.DisabledByUser)]
    [InlineData(StartupState.DisabledByPolicy)]
    [InlineData(StartupState.EnabledByPolicy)]
    public async Task A_change_the_person_cannot_make_sends_nothing_to_Windows(StartupState state)
    {
        _startup.State = state;
        var model = Model();
        await model.RefreshAsync();
        _startup.Calls.Clear();

        await model.SetAsync(!model.IsOn);

        _startup.Calls.Should().BeEmpty();
        model.State.Should().Be(state);
    }

    [Fact]
    public async Task A_change_before_the_state_is_known_sends_nothing()
    {
        var model = Model();

        await model.SetAsync(true);

        _startup.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refresh_that_finishes_after_a_newer_change_does_not_overwrite_it()
    {
        var model = Model();
        await model.RefreshAsync();
        model.State.Should().Be(StartupState.Disabled);
        _startup.GetGate = new TaskCompletionSource();
        var slowRefresh = model.RefreshAsync();

        await model.SetAsync(true);
        model.IsOn.Should().BeTrue();
        _startup.GetGate.SetResult();
        await slowRefresh;

        model.State.Should().Be(StartupState.Enabled, "the older read of Disabled is stale");
        model.IsOn.Should().BeTrue();
        model.Error.Should().BeNull();
    }

    [Fact]
    public async Task A_refresh_that_fails_after_a_newer_change_does_not_wipe_it()
    {
        var model = Model();
        await model.RefreshAsync();
        _startup.GetGate = new TaskCompletionSource();
        var slowRefresh = model.RefreshAsync();
        await model.SetAsync(true);
        _startup.GetGate.SetException(new InvalidOperationException("late failure"));

        await slowRefresh;

        model.State.Should().Be(StartupState.Enabled);
        model.Error.Should().BeNull();
    }

    [Fact]
    public async Task Of_two_refreshes_the_later_one_wins_even_when_the_earlier_finishes_last()
    {
        var model = Model();
        await model.RefreshAsync();
        var oldGate = new TaskCompletionSource();
        _startup.GetGate = oldGate;
        var older = model.RefreshAsync();
        _startup.State = StartupState.DisabledByUser;
        _startup.GetGate = null;
        await model.RefreshAsync();
        model.State.Should().Be(StartupState.DisabledByUser);

        oldGate.SetResult();
        await older;

        model.State.Should().Be(StartupState.DisabledByUser);
    }

    [Fact]
    public async Task A_second_change_while_one_is_pending_is_ignored()
    {
        var model = Model();
        await model.RefreshAsync();
        _startup.Gate = new TaskCompletionSource();

        var first = model.SetAsync(true);
        model.IsBusy.Should().BeTrue();
        model.CanChange.Should().BeFalse();
        await model.SetAsync(true);
        _startup.Gate.SetResult();
        await first;

        _startup.Calls.Count(c => c == "enable").Should().Be(1);
        model.IsBusy.Should().BeFalse();
        model.IsOn.Should().BeTrue();
    }

    [Fact]
    public async Task When_the_state_cannot_be_read_nothing_is_claimed_and_the_reason_is_shown()
    {
        _startup.Throws = new InvalidOperationException("no package identity");
        var model = Model();

        await model.RefreshAsync();

        model.State.Should().BeNull();
        model.IsOn.Should().BeFalse();
        model.CanChange.Should().BeFalse();
        model.Error.Should().Be("Couldn’t read launch at login: no package identity");
    }

    [Fact]
    public async Task A_good_read_clears_an_earlier_error()
    {
        _startup.Throws = new InvalidOperationException("x");
        var model = Model();
        await model.RefreshAsync();
        model.Error.Should().NotBeNull();
        _startup.Throws = null;

        await model.RefreshAsync();

        model.Error.Should().BeNull();
        model.CanChange.Should().BeTrue();
    }

    /// <summary>A task that stays enabled whatever it is asked.</summary>
    private sealed class StuckEnabled(FakeStartupTask inner) : IStartupTaskService
    {
        public Task<StartupState> GetStateAsync() => inner.GetStateAsync();

        public Task<StartupState> RequestEnableAsync() => inner.RequestEnableAsync();

        public Task<StartupState> DisableAsync() => Task.FromResult(StartupState.Enabled);
    }
}
