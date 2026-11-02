using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Theming;

namespace TimeTug.Presentation.Tests.Theming;

public class NativeMenuThemeTests
{
    [Theory]
    [InlineData(AppTheme.UseSystem, false, NativeMenuMode.Light)]
    [InlineData(AppTheme.UseSystem, true, NativeMenuMode.Dark)]
    [InlineData(AppTheme.Light, true, NativeMenuMode.Light)]
    [InlineData(AppTheme.Dark, false, NativeMenuMode.Dark)]
    public void The_menu_follows_the_apps_choice_and_falls_back_to_the_system(AppTheme chosen, bool systemDark, NativeMenuMode expected)
    {
        var ui = new FakeUiSettings { AppsUseDarkTheme = systemDark };

        AppearanceResolver.NativeMenu(chosen, ui).Should().Be(expected);
    }

    [Theory]
    [InlineData(AppTheme.UseSystem)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void A_contrast_theme_is_left_to_windows(AppTheme chosen)
    {
        var ui = new FakeUiSettings { HighContrast = true, AppsUseDarkTheme = true };

        AppearanceResolver.NativeMenu(chosen, ui).Should().Be(NativeMenuMode.Windows);
    }

    [Fact]
    public void The_follower_sets_the_mode_when_it_starts()
    {
        var native = new RecordingNativeMenuTheme();
        var ui = new FakeUiSettings { AppsUseDarkTheme = true };
        using var follower = new NativeMenuThemeFollower(new MemoryPresentationSettingsStore(), ui, native);

        native.Modes.Should().Equal(NativeMenuMode.Dark);
        follower.Mode.Should().Be(NativeMenuMode.Dark);
    }

    [Fact]
    public void The_follower_follows_a_live_change_of_the_choice_and_of_windows()
    {
        var native = new RecordingNativeMenuTheme();
        var store = new MemoryPresentationSettingsStore();
        var ui = new FakeUiSettings { AppsUseDarkTheme = true };
        using var follower = new NativeMenuThemeFollower(store, ui, native);

        store.Save(store.Load() with { Theme = AppTheme.Light });
        ui.AppsUseDarkTheme = false;
        ui.Raise();
        store.Save(store.Load() with { Theme = AppTheme.UseSystem });
        ui.AppsUseDarkTheme = true;
        ui.Raise();
        ui.HighContrast = true;
        ui.Raise();
        ui.HighContrast = false;
        ui.Raise();

        native.Modes.Should().Equal(NativeMenuMode.Dark, NativeMenuMode.Light, NativeMenuMode.Dark, NativeMenuMode.Windows, NativeMenuMode.Dark);
    }

    [Fact]
    public void A_change_that_leaves_the_mode_alone_does_not_call_windows_again()
    {
        var native = new RecordingNativeMenuTheme();
        var store = new MemoryPresentationSettingsStore();
        var ui = new FakeUiSettings();
        using var follower = new NativeMenuThemeFollower(store, ui, native);

        store.Save(store.Load() with { ChipMode = TimeTug.Presentation.Chip.ChipMode.Always });
        ui.Raise();

        native.Modes.Should().HaveCount(1);
    }

    [Fact]
    public void A_refused_change_is_tried_again_at_the_next_change()
    {
        var native = new RecordingNativeMenuTheme { Accepts = false };
        var store = new MemoryPresentationSettingsStore();
        var ui = new FakeUiSettings();
        using var follower = new NativeMenuThemeFollower(store, ui, native);
        native.Accepts = true;

        ui.Raise();

        native.Modes.Should().Equal(NativeMenuMode.Light, NativeMenuMode.Light);
    }

    [Fact]
    public void A_disposed_follower_stops_listening()
    {
        var native = new RecordingNativeMenuTheme();
        var store = new MemoryPresentationSettingsStore();
        var ui = new FakeUiSettings();
        var follower = new NativeMenuThemeFollower(store, ui, native);
        follower.Dispose();

        store.Save(store.Load() with { Theme = AppTheme.Dark });

        native.Modes.Should().HaveCount(1);
    }

    private sealed class RecordingNativeMenuTheme : INativeMenuTheme
    {
        public List<NativeMenuMode> Modes { get; } = [];

        public bool Accepts { get; set; } = true;

        public bool Apply(NativeMenuMode mode)
        {
            Modes.Add(mode);
            return Accepts;
        }
    }
}
