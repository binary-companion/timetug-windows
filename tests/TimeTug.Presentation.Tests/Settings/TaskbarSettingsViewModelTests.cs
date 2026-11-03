using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

/// <summary>"Show TimeTug in the taskbar": the button on the welcome window's last step and in Settings, General opens the Taskbar page of Windows Settings.</summary>
public sealed class TaskbarSettingsViewModelTests
{
    private readonly FakeSystemLauncher _launcher = new();
    private readonly RecordingTugDiagnostics _diagnostics = new();

    private TaskbarSettingsViewModel Model(ISystemLauncher? launcher = null) => new(launcher ?? _launcher, new ImmediateDispatcher(), new ReswLocalizer(), _diagnostics);

    [Fact]
    public async Task Opening_goes_to_the_Taskbar_page_of_Windows_Settings()
    {
        var model = Model();

        await model.OpenAsync();

        _launcher.Opened.Should().Equal("ms-settings:taskbar");
        TaskbarSettingsViewModel.SettingsUri.Should().Be("ms-settings:taskbar");
        model.HasError.Should().BeFalse();
        _diagnostics.Entries.Should().BeEmpty();
    }

    [Fact]
    public void The_button_says_what_it_does_and_its_name_starts_with_its_label()
    {
        var model = Model();

        model.ButtonText.Should().Be("Show TimeTug in the taskbar");
        model.ButtonAutomationName.Should().StartWith(model.ButtonText);
    }

    [Fact]
    public async Task A_page_that_does_not_open_is_said_plainly_and_logged_without_content()
    {
        _launcher.Succeeds = false;
        var model = Model();

        await model.OpenAsync();

        model.Error.Should().Be("Couldn’t open Windows Settings. Open Settings, then Personalization, then Taskbar.");
        model.HasError.Should().BeTrue();
        _diagnostics.Entries.Should().ContainSingle().Which.Should().Be(("warning", "taskbar", "The Taskbar settings page did not open."));
    }

    [Fact]
    public async Task The_next_press_that_opens_clears_the_error()
    {
        _launcher.Succeeds = false;
        var model = Model();
        await model.OpenAsync();
        _launcher.Succeeds = true;

        await model.OpenAsync();

        model.Error.Should().BeNull();
        model.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task A_launcher_that_throws_does_not_crash_the_app()
    {
        var model = Model(new ThrowingLauncher());

        var act = model.OpenAsync;

        await act.Should().NotThrowAsync();
        model.HasError.Should().BeTrue();
        _diagnostics.Entries.Should().ContainSingle().Which.Message.Should().Be("Opening the Taskbar settings failed: InvalidOperationException");
    }

    [Fact]
    public void The_guide_is_the_animation_unless_Windows_has_animations_off()
    {
        TaskbarGuide.AssetFor(animationsEnabled: true).Should().Be("Assets/taskbar-guide.gif");
        TaskbarGuide.AssetFor(animationsEnabled: false).Should().Be("Assets/taskbar-guide-still.png");
    }

    [Fact]
    public void The_welcome_window_offers_the_button_only_when_the_app_gives_a_model_for_it()
    {
        var text = new ReswLocalizer();
        var without = new WelcomeViewModel(new FakeEngineClient(), new ImmediateDispatcher(), text);
        var taskbar = Model();
        var with = new WelcomeViewModel(new FakeEngineClient(), new ImmediateDispatcher(), text, taskbar: taskbar);

        without.ShowTaskbarButton.Should().BeFalse();
        with.ShowTaskbarButton.Should().BeTrue();
        with.Taskbar.Should().BeSameAs(taskbar);
    }

    [Fact]
    public void The_General_pane_model_carries_the_taskbar_row()
    {
        var text = new ReswLocalizer();
        var taskbar = Model();
        var launch = new LaunchAtLoginViewModel(new FakeStartupTask(), _launcher, new ImmediateDispatcher(), text);
        var recorder = new HotkeyRecorderViewModel(new FakeHotkeys(), new ImmediateDispatcher(), text);
        var about = new AboutViewModel("1.0.0.0", null, isDevelopmentBuild: false, _launcher, new ImmediateDispatcher(), text);

        new GeneralViewModel(launch, recorder, about, taskbar).Taskbar.Should().BeSameAs(taskbar);
        new GeneralViewModel(launch, recorder, about).Taskbar.Should().BeNull();
        GeneralViewModel.Ids.TaskbarIcon.Should().Be("taskbar-icon");
    }

    private sealed class ThrowingLauncher : ISystemLauncher
    {
        public ValueTask<bool> LaunchAsync(string url) => throw new InvalidOperationException("boom");
    }
}
