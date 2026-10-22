using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Tug;

namespace TimeTug.Presentation.Tests.Settings;

public class AppearanceResolverTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Use_system_follows_the_apps_theme_windows_reports(bool systemDark, bool expected)
    {
        var ui = new FakeUiSettings { AppsUseDarkTheme = systemDark };

        AppearanceResolver.IsDark(AppTheme.UseSystem, ui).Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Light_and_Dark_ignore_what_windows_says(bool systemDark)
    {
        var ui = new FakeUiSettings { AppsUseDarkTheme = systemDark };

        AppearanceResolver.IsDark(AppTheme.Light, ui).Should().BeFalse();
        AppearanceResolver.IsDark(AppTheme.Dark, ui).Should().BeTrue();
    }

    [Theory]
    [InlineData(FlyoutBackdrop.Material)]
    [InlineData(FlyoutBackdrop.Solid)]
    public void The_chosen_backdrop_is_used_when_windows_allows_it(FlyoutBackdrop chosen)
    {
        var ui = new FakeUiSettings();

        AppearanceResolver.Backdrop(chosen, ui).Should().Be(chosen);
        AppearanceResolver.BackdropOverridden(ui).Should().BeFalse();
    }

    [Theory]
    [InlineData(FlyoutBackdrop.Material)]
    [InlineData(FlyoutBackdrop.Solid)]
    public void Reduced_transparency_gives_a_solid_backdrop_whatever_was_chosen(FlyoutBackdrop chosen)
    {
        var ui = new FakeUiSettings { TransparencyEffectsEnabled = false };

        AppearanceResolver.Backdrop(chosen, ui).Should().Be(FlyoutBackdrop.Solid);
        AppearanceResolver.BackdropOverridden(ui).Should().BeTrue();
    }

    [Theory]
    [InlineData(FlyoutBackdrop.Material)]
    [InlineData(FlyoutBackdrop.Solid)]
    public void A_contrast_theme_gives_a_solid_backdrop_whatever_was_chosen(FlyoutBackdrop chosen)
    {
        var ui = new FakeUiSettings { HighContrast = true };

        AppearanceResolver.Backdrop(chosen, ui).Should().Be(FlyoutBackdrop.Solid);
        AppearanceResolver.BackdropOverridden(ui).Should().BeTrue();
    }
}
