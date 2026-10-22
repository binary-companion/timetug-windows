using System.ComponentModel;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;
using TimeTug.Presentation.Tray;

namespace TimeTug.Presentation.Tests.Settings;

public class AppearanceViewModelTests
{
    private readonly MemoryPresentationSettingsStore _store = new();
    private readonly FakeUiSettings _ui = new();
    private int _storeChanges;

    public AppearanceViewModelTests()
    {
        _store.Changed += () => _storeChanges++;
    }

    private AppearanceViewModel Model(IPresentationSettingsStore? store = null) => new(store ?? _store, _ui, new ImmediateDispatcher(), new ReswLocalizer());

    // ---- defaults and what is offered ---------------------------------------------------------------------------

    [Fact]
    public void A_fresh_install_follows_the_system_shows_the_minute_digit_and_uses_acrylic()
    {
        using var model = Model();

        model.Theme.Should().Be(AppTheme.UseSystem);
        model.TrayStyle.Should().Be(TrayIconStyle.IconWithMinuteDigit);
        model.Backdrop.Should().Be(FlyoutBackdrop.Material);
    }

    [Fact]
    public void Each_setting_offers_its_choices_in_the_order_of_the_pane_with_names_a_screen_reader_can_use()
    {
        using var model = Model();

        model.ThemeChoices.Select(c => c.Value).Should().Equal(AppTheme.UseSystem, AppTheme.Light, AppTheme.Dark);
        model.ThemeChoices.Select(c => c.Title).Should().Equal("Use system", "Light", "Dark");
        model.TrayChoices.Select(c => c.Value).Should().Equal(TrayIconStyle.IconOnly, TrayIconStyle.IconWithMinuteDigit);
        model.TrayChoices.Select(c => c.Title).Should().Equal("Icon only", "Icon with minute digit");
        model.BackdropChoices.Select(c => c.Value).Should().Equal(FlyoutBackdrop.Material, FlyoutBackdrop.Solid);
        model.BackdropChoices.Select(c => c.Title).Should().Equal("Acrylic", "Solid");
        foreach (var name in model.ThemeChoices.Select(c => c.AutomationName).Concat(model.TrayChoices.Select(c => c.AutomationName)).Concat(model.BackdropChoices.Select(c => c.AutomationName)))
        {
            name.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void The_pane_shows_what_was_stored()
    {
        _store.Save(new PresentationSettings(AppTheme.Dark, TrayIconStyle.IconOnly, FlyoutBackdrop.Solid, null));
        using var model = Model();

        model.Theme.Should().Be(AppTheme.Dark);
        model.TrayStyle.Should().Be(TrayIconStyle.IconOnly);
        model.Backdrop.Should().Be(FlyoutBackdrop.Solid);
    }

    // ---- each choice is kept once --------------------------------------------------------------------------------

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Choosing_a_theme_stores_it_and_raises_changed_once(AppTheme theme)
    {
        using var model = Model();
        _storeChanges = 0;

        model.SetTheme(theme);

        _store.Load().Theme.Should().Be(theme);
        model.Theme.Should().Be(theme);
        _storeChanges.Should().Be(1);
    }

    [Theory]
    [InlineData(TrayIconStyle.IconOnly)]
    public void Choosing_a_tray_style_stores_it_and_raises_changed_once(TrayIconStyle style)
    {
        using var model = Model();
        _storeChanges = 0;

        model.SetTrayStyle(style);

        _store.Load().TrayStyle.Should().Be(style);
        model.TrayStyle.Should().Be(style);
        _storeChanges.Should().Be(1);
    }

    [Fact]
    public void Choosing_a_backdrop_stores_it_and_raises_changed_once()
    {
        using var model = Model();
        _storeChanges = 0;

        model.SetBackdrop(FlyoutBackdrop.Solid);

        _store.Load().Backdrop.Should().Be(FlyoutBackdrop.Solid);
        model.Backdrop.Should().Be(FlyoutBackdrop.Solid);
        _storeChanges.Should().Be(1);
    }

    [Fact]
    public void Choosing_what_is_already_chosen_stores_nothing()
    {
        using var model = Model();
        _storeChanges = 0;

        model.SetTheme(AppTheme.UseSystem);
        model.SetTrayStyle(TrayIconStyle.IconWithMinuteDigit);
        model.SetBackdrop(FlyoutBackdrop.Material);

        _storeChanges.Should().Be(0);
    }

    [Fact]
    public void A_choice_leaves_the_other_settings_and_the_settings_window_place_alone()
    {
        var place = new WindowGeometry(-1280, 24, 900, 700);
        _store.Save(new PresentationSettings(AppTheme.Dark, TrayIconStyle.IconOnly, FlyoutBackdrop.Solid, place));
        using var model = Model();

        model.SetBackdrop(FlyoutBackdrop.Material);

        _store.Load().Should().Be(new PresentationSettings(AppTheme.Dark, TrayIconStyle.IconOnly, FlyoutBackdrop.Material, place));
    }

    [Fact]
    public void A_choice_raises_property_changed_for_that_setting_only()
    {
        using var model = Model();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        model.SetTheme(AppTheme.Dark);

        raised.Should().Contain(nameof(AppearanceViewModel.Theme)).And.Contain(nameof(AppearanceViewModel.PreviewDark));
        raised.Should().NotContain(nameof(AppearanceViewModel.Backdrop)).And.NotContain(nameof(AppearanceViewModel.TrayStyle));
    }

    [Fact]
    public void A_change_made_elsewhere_reaches_the_pane()
    {
        using var model = Model();

        _store.Save(PresentationSettings.Defaults with { Theme = AppTheme.Light, Backdrop = FlyoutBackdrop.Solid });

        model.Theme.Should().Be(AppTheme.Light);
        model.Backdrop.Should().Be(FlyoutBackdrop.Solid);
    }

    [Fact]
    public void After_it_is_disposed_the_pane_no_longer_follows_the_store()
    {
        var model = Model();
        model.Dispose();

        _store.Save(PresentationSettings.Defaults with { Theme = AppTheme.Dark });

        model.Theme.Should().Be(AppTheme.UseSystem);
    }

    // ---- corrupt stored values -----------------------------------------------------------------------------------

    [Fact]
    public void Corrupt_stored_values_show_the_defaults()
    {
        var values = new Dictionary<string, object>
        {
            [PresentationSettingsCodec.ThemeKey] = "Neon",
            [PresentationSettingsCodec.TrayStyleKey] = 7,
            [PresentationSettingsCodec.BackdropKey] = "Glass",
        };
        var store = new StoreOver(values);
        using var model = Model(store);

        model.Theme.Should().Be(AppTheme.UseSystem);
        model.TrayStyle.Should().Be(TrayIconStyle.IconWithMinuteDigit);
        model.Backdrop.Should().Be(FlyoutBackdrop.Material);
    }

    // ---- the preview follows the theme, and the backdrop override is said ----------------------------------------

    [Theory]
    [InlineData(AppTheme.UseSystem, true, true)]
    [InlineData(AppTheme.UseSystem, false, false)]
    [InlineData(AppTheme.Light, true, false)]
    [InlineData(AppTheme.Dark, false, true)]
    public void The_previews_are_drawn_in_the_theme_that_applies(AppTheme theme, bool systemDark, bool expectedDark)
    {
        _ui.AppsUseDarkTheme = systemDark;
        _store.Save(PresentationSettings.Defaults with { Theme = theme });
        using var model = Model();

        model.PreviewDark.Should().Be(expectedDark);
    }

    [Fact]
    public void The_pane_always_says_that_reduced_transparency_and_contrast_override_the_backdrop()
    {
        using var model = Model();

        model.BackdropNote.Should().Contain("transparency").And.Contain("contrast");
        model.BackdropOverridden.Should().BeFalse();
        model.BackdropOverrideText.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false, "transparency")]
    [InlineData(false, true, "contrast")]
    public void When_windows_overrides_the_choice_the_pane_says_what_is_in_effect(bool reducedTransparency, bool contrast, string word)
    {
        _ui.TransparencyEffectsEnabled = !reducedTransparency;
        _ui.HighContrast = contrast;
        using var model = Model();

        model.BackdropOverridden.Should().BeTrue();
        model.EffectiveBackdrop.Should().Be(FlyoutBackdrop.Solid);
        model.BackdropOverrideText.Should().Contain(word);
        model.Backdrop.Should().Be(FlyoutBackdrop.Material, "the choice itself is kept");
    }

    [Fact]
    public void A_change_of_the_windows_settings_updates_the_override_and_the_previews()
    {
        using var model = Model();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _ui.TransparencyEffectsEnabled = false;
        _ui.AppsUseDarkTheme = true;
        _ui.Raise();

        model.BackdropOverridden.Should().BeTrue();
        model.PreviewDark.Should().BeTrue();
        raised.Should().Contain(nameof(AppearanceViewModel.BackdropOverridden)).And.Contain(nameof(AppearanceViewModel.PreviewDark));
    }

    [Fact]
    public void The_tray_preview_follows_the_taskbar_theme_not_the_app_theme()
    {
        _ui.TaskbarUsesLightTheme = true;
        _ui.AppsUseDarkTheme = true;
        using var model = Model();

        model.TaskbarLight.Should().BeTrue();
    }

    /// <summary>A store over a plain dictionary, read and written through the codec the app uses.</summary>
    private sealed class StoreOver(Dictionary<string, object> values) : IPresentationSettingsStore
    {
        public event Action? Changed;

        public PresentationSettings Load() => PresentationSettingsCodec.Read(key => values.GetValueOrDefault(key));

        public void Save(PresentationSettings settings)
        {
            foreach (var (key, value) in PresentationSettingsCodec.Write(settings))
            {
                if (value is null)
                {
                    values.Remove(key);
                }
                else
                {
                    values[key] = value;
                }
            }

            Changed?.Invoke();
        }
    }
}
