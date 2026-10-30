using TimeTug.Platform.OsState;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Tests.OsState;

public class SessionStateTests
{
    [Fact]
    public void Lock_and_unlock_messages_change_the_state_and_raise_each_event_once_per_transition()
    {
        var source = new FakeMessageSource();
        using var session = new SessionState(source, () => false);
        var events = new List<string>();
        session.Locked += () => events.Add("locked");
        session.Unlocked += () => events.Add("unlocked");

        source.Raise(WindowMessage.SessionChange, WindowMessage.SessionLock);
        source.Raise(WindowMessage.SessionChange, WindowMessage.SessionLock);
        session.IsLocked.Should().BeTrue();
        source.Raise(WindowMessage.SessionChange, WindowMessage.SessionUnlock);
        source.Raise(WindowMessage.SessionChange, WindowMessage.SessionUnlock);

        session.IsLocked.Should().BeFalse();
        events.Should().Equal("locked", "unlocked");
    }

    [Fact]
    public void The_initial_state_comes_from_the_reader_and_other_messages_are_ignored()
    {
        var source = new FakeMessageSource();
        using var session = new SessionState(source, () => true);
        session.IsLocked.Should().BeTrue();

        source.Raise(WindowMessage.TimeChange);
        source.Raise(WindowMessage.SessionChange, 5);   // WTS_SESSION_LOGON

        session.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Disposing_stops_listening()
    {
        var source = new FakeMessageSource();
        var session = new SessionState(source, () => false);
        source.Subscribers.Should().Be(1);

        session.Dispose();

        source.Subscribers.Should().Be(0);
    }
}

public class MonitorServiceTests
{
    private static readonly MonitorInfo Main = new("\\\\.\\DISPLAY1", 0, 0, 1920, 1080, true);
    private static readonly MonitorInfo Side = new("\\\\.\\DISPLAY2", 1920, 0, 1280, 1024, false);

    [Fact]
    public void The_monitors_are_enumerated_at_start_and_the_primary_is_the_flagged_one()
    {
        using var service = new MonitorService(new FakeMessageSource(), () => [Side, Main]);

        service.Monitors.Should().Equal(Side, Main);
        service.Primary.Should().Be(Main);
    }

    [Fact]
    public void With_no_flagged_primary_the_first_monitor_is_used_and_with_no_monitors_a_zero_size_placeholder()
    {
        using var unflagged = new MonitorService(new FakeMessageSource(), () => [Side]);
        unflagged.Primary.Should().Be(Side);

        using var none = new MonitorService(new FakeMessageSource(), () => []);
        none.Monitors.Should().BeEmpty();
        none.Primary.Width.Should().Be(0);
    }

    [Fact]
    public void A_display_change_that_alters_the_layout_re_enumerates_and_raises_changed_once()
    {
        var source = new FakeMessageSource();
        IReadOnlyList<MonitorInfo> layout = [Main, Side];
        using var service = new MonitorService(source, () => layout);
        var changed = 0;
        service.Changed += () => changed++;

        layout = [Main];
        source.Raise(WindowMessage.DisplayChange);

        service.Monitors.Should().Equal(Main);
        changed.Should().Be(1);
    }

    [Fact]
    public void A_display_change_with_the_same_layout_and_unrelated_messages_raise_nothing()
    {
        var source = new FakeMessageSource();
        using var service = new MonitorService(source, () => [Main, Side]);
        var changed = 0;
        service.Changed += () => changed++;

        source.Raise(WindowMessage.DisplayChange);
        source.Raise(WindowMessage.TimeChange);

        changed.Should().Be(0);
    }

    [Fact]
    public void A_moved_taskbar_announced_by_a_setting_change_is_a_change_once()
    {
        var source = new FakeMessageSource();
        IReadOnlyList<MonitorInfo> layout = [Main with { WorkArea = new PixelRect(0, 0, 1920, 1040) }];
        using var service = new MonitorService(source, () => layout);
        var changed = 0;
        service.Changed += () => changed++;

        layout = [Main with { WorkArea = new PixelRect(48, 0, 1872, 1080) }];
        source.Raise(WindowMessage.SettingChange);
        source.Raise(WindowMessage.SettingChange);

        changed.Should().Be(1, "the second message found the same layout");
        service.Monitors.Single().Work.Should().Be(new PixelRect(48, 0, 1872, 1080));
    }

    [Fact]
    public void A_changed_scaling_is_a_change()
    {
        var source = new FakeMessageSource();
        IReadOnlyList<MonitorInfo> layout = [Main];
        using var service = new MonitorService(source, () => layout);
        var changed = 0;
        service.Changed += () => changed++;

        layout = [Main with { Dpi = 144 }];
        source.Raise(WindowMessage.SettingChange);

        changed.Should().Be(1);
        service.Primary.Scale.Should().Be(1.5);
    }

    [Fact]
    public void A_setting_change_that_leaves_the_layout_alone_raises_nothing()
    {
        var source = new FakeMessageSource();
        using var service = new MonitorService(source, () => [Main, Side]);
        var changed = 0;
        service.Changed += () => changed++;

        source.Raise(WindowMessage.SettingChange);

        changed.Should().Be(0);
    }

    [Fact]
    public void A_moved_primary_is_a_change()
    {
        var source = new FakeMessageSource();
        IReadOnlyList<MonitorInfo> layout = [Main, Side];
        using var service = new MonitorService(source, () => layout);
        var changed = 0;
        service.Changed += () => changed++;

        layout = [Main with { IsPrimary = false }, Side with { IsPrimary = true }];
        source.Raise(WindowMessage.DisplayChange);

        changed.Should().Be(1);
        service.Primary.Id.Should().Be(Side.Id);
    }
}

public class UiSettingsTests
{
    private static UiSnapshot Snapshot(bool animations = true, bool transparency = true, bool contrast = false, bool light = true) =>
        new(animations, transparency, contrast, light);

    [Fact]
    public void The_properties_read_the_current_values()
    {
        var current = Snapshot(animations: false, transparency: false, contrast: true, light: false);
        using var settings = new UiSettings(new FakeMessageSource(), () => current);

        settings.AnimationsEnabled.Should().BeFalse();
        settings.TransparencyEffectsEnabled.Should().BeFalse();
        settings.HighContrast.Should().BeTrue();
        settings.TaskbarUsesLightTheme.Should().BeFalse();

        current = Snapshot();
        settings.AnimationsEnabled.Should().BeTrue();
        settings.HighContrast.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    public void A_settings_change_message_raises_changed_when_one_of_the_four_values_differs(bool animations, bool transparency, bool contrast, bool light)
    {
        var source = new FakeMessageSource();
        var current = Snapshot();
        using var settings = new UiSettings(source, () => current);
        var changed = 0;
        settings.Changed += () => changed++;

        current = Snapshot(animations, transparency, contrast, light);
        source.Raise(WindowMessage.SettingChange, text: "WindowMetrics");
        source.Raise(WindowMessage.SettingChange);

        changed.Should().Be(1, "the second message finds nothing new");
    }

    [Fact]
    public void A_change_of_the_apps_theme_raises_changed_and_is_readable()
    {
        var source = new FakeMessageSource();
        var current = Snapshot();
        using var settings = new UiSettings(source, () => current);
        var changed = 0;
        settings.Changed += () => changed++;
        settings.AppsUseDarkTheme.Should().BeFalse();

        current = current with { AppsUseDarkTheme = true };
        source.Raise(WindowMessage.SettingChange, text: "ImmersiveColorSet");

        changed.Should().Be(1);
        settings.AppsUseDarkTheme.Should().BeTrue();
    }

    [Fact]
    public void Settings_messages_that_change_nothing_and_other_messages_raise_nothing()
    {
        var source = new FakeMessageSource();
        using var settings = new UiSettings(source, () => Snapshot());
        var changed = 0;
        settings.Changed += () => changed++;

        source.Raise(WindowMessage.SettingChange);
        source.Raise(WindowMessage.DisplayChange);

        changed.Should().Be(0);
    }

    [Fact]
    public void Disposing_stops_listening()
    {
        var source = new FakeMessageSource();
        var settings = new UiSettings(source, () => Snapshot());
        settings.Dispose();
        source.Subscribers.Should().Be(0);
    }
}
