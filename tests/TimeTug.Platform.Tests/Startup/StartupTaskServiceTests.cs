using TimeTug.Platform.OsState;
using TimeTug.Platform.Startup;
using TimeTug.Presentation.Startup;
using Windows.ApplicationModel;

namespace TimeTug.Platform.Tests.Startup;

public class StartupTaskServiceTests
{
    [Theory]
    [InlineData(StartupTaskState.Enabled, StartupState.Enabled)]
    [InlineData(StartupTaskState.Disabled, StartupState.Disabled)]
    [InlineData(StartupTaskState.DisabledByUser, StartupState.DisabledByUser)]
    [InlineData(StartupTaskState.DisabledByPolicy, StartupState.DisabledByPolicy)]
    [InlineData(StartupTaskState.EnabledByPolicy, StartupState.EnabledByPolicy)]
    public async Task Every_state_Windows_reports_maps_to_the_apps_state(StartupTaskState windows, StartupState expected)
    {
        var task = new FakeTask(windows);

        (await Service(task).GetStateAsync()).Should().Be(expected);
        StartupTaskService.Map(windows).Should().Be(expected);
    }

    [Fact]
    public void Every_Windows_state_has_a_mapping()
    {
        foreach (var value in Enum.GetValues<StartupTaskState>())
        {
            StartupTaskService.Map(value).Should().BeDefined($"{value} must not throw");
        }
    }

    [Fact]
    public void A_state_Windows_adds_later_is_not_guessed()
    {
        var act = () => StartupTaskService.Map((StartupTaskState)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task The_task_is_opened_every_time_so_a_change_in_Windows_Settings_is_seen()
    {
        var opened = 0;
        var task = new FakeTask(StartupTaskState.Enabled);
        var service = new StartupTaskService(() => { opened++; return Task.FromResult<IStartupTaskHandle>(task); });

        (await service.GetStateAsync()).Should().Be(StartupState.Enabled);
        task.Current = StartupTaskState.DisabledByUser;
        (await service.GetStateAsync()).Should().Be(StartupState.DisabledByUser);

        opened.Should().Be(2);
    }

    [Fact]
    public async Task Requesting_enable_returns_the_state_after_the_request()
    {
        var task = new FakeTask(StartupTaskState.Disabled) { AfterRequest = StartupTaskState.Enabled };

        (await Service(task).RequestEnableAsync()).Should().Be(StartupState.Enabled);
        task.Requests.Should().Be(1);
    }

    [Fact]
    public async Task A_request_the_person_blocked_comes_back_disabled_by_user()
    {
        var task = new FakeTask(StartupTaskState.DisabledByUser) { AfterRequest = StartupTaskState.DisabledByUser };

        (await Service(task).RequestEnableAsync()).Should().Be(StartupState.DisabledByUser);
    }

    [Fact]
    public async Task Disable_turns_the_task_off_and_returns_the_state_read_again()
    {
        var task = new FakeTask(StartupTaskState.Enabled);

        (await Service(task).DisableAsync()).Should().Be(StartupState.Disabled);
        task.Disables.Should().Be(1);
    }

    [Fact]
    public async Task Disable_of_a_task_a_policy_keeps_on_says_it_is_still_on()
    {
        var task = new FakeTask(StartupTaskState.EnabledByPolicy) { IgnoresDisable = true };

        (await Service(task).DisableAsync()).Should().Be(StartupState.EnabledByPolicy);
    }

    [Fact]
    public async Task A_failure_to_open_the_task_reaches_the_caller()
    {
        var service = new StartupTaskService(() => Task.FromException<IStartupTaskHandle>(new InvalidOperationException("no package identity")));

        await service.Invoking(s => s.GetStateAsync()).Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(s => s.RequestEnableAsync()).Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(s => s.DisableAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void The_task_id_is_the_one_the_manifest_declares()
    {
        StartupTaskService.TaskId.Should().Be("TimeTugStartup");
        var manifest = File.ReadAllText(Path.Combine(FindRoot(), "src", "TimeTug.App", "Package.appxmanifest"));
        manifest.Should().Contain("TaskId=\"" + StartupTaskService.TaskId + "\"");
        manifest.Should().Contain("windows.startupTask");
        manifest.Should().Contain("Enabled=\"false\"", "the task is off until the person turns it on");
    }

    [Theory]
    [InlineData("ms-settings:startupapps")]
    [InlineData("ms-windows-store://downloadsandupdates")]
    [InlineData("https://binarycompanion.com/")]
    public async Task The_app_links_are_opened(string url)
    {
        var opened = new List<Uri>();
        var launcher = new AppLinkLauncher(uri => { opened.Add(uri); return ValueTask.FromResult(true); });

        (await launcher.LaunchAsync(url)).Should().BeTrue();

        opened.Should().ContainSingle();
    }

    [Theory]
    [InlineData("http://example.test/")]
    [InlineData("zoommtg://zoom.us/join?confno=1")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("calculator:")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public async Task Other_links_are_refused(string url)
    {
        var called = 0;
        var launcher = new AppLinkLauncher(_ => { called++; return ValueTask.FromResult(true); });

        (await launcher.LaunchAsync(url)).Should().BeFalse();

        called.Should().Be(0);
    }

    private static StartupTaskService Service(FakeTask task) => new(() => Task.FromResult<IStartupTaskHandle>(task));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TimeTug.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("TimeTug.sln not found above " + AppContext.BaseDirectory);
    }

    private sealed class FakeTask(StartupTaskState state) : IStartupTaskHandle
    {
        public StartupTaskState Current { get; set; } = state;

        public StartupTaskState? AfterRequest { get; set; }

        public bool IgnoresDisable { get; set; }

        public int Requests { get; private set; }

        public int Disables { get; private set; }

        public StartupTaskState State => Current;

        public Task<StartupTaskState> RequestEnableAsync()
        {
            Requests++;
            if (AfterRequest is { } after)
            {
                Current = after;
            }

            return Task.FromResult(Current);
        }

        public void Disable()
        {
            Disables++;
            if (!IgnoresDisable)
            {
                Current = StartupTaskState.Disabled;
            }
        }
    }
}
