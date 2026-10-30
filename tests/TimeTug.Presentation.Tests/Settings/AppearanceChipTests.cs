using System.ComponentModel;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

public class AppearanceChipTests
{
    private readonly MemoryPresentationSettingsStore _store = new();
    private readonly FakeUiSettings _ui = new();
    private int _storeChanges;

    public AppearanceChipTests()
    {
        _store.Changed += () => _storeChanges++;
    }

    private AppearanceViewModel Model() => new(_store, _ui, new ImmediateDispatcher(), new ReswLocalizer());

    [Fact]
    public void A_fresh_install_has_the_chip_off_and_the_lead_choice_disabled()
    {
        using var model = Model();

        model.ChipMode.Should().Be(ChipMode.Off);
        model.ChipLeadMinutes.Should().Be(15);
        model.ChipLeadEnabled.Should().BeFalse();
    }

    [Fact]
    public void The_pane_offers_three_modes_and_five_lead_times_with_names_a_screen_reader_can_use()
    {
        using var model = Model();

        model.ChipModeChoices.Select(c => c.Value).Should().Equal(ChipMode.Off, ChipMode.BeforeMeetings, ChipMode.Always);
        model.ChipModeChoices.Select(c => c.Title).Should().Equal("Off", "Before meetings", "Always");
        model.ChipLeadChoices.Select(c => c.Value).Should().Equal(5, 10, 15, 30, 60);
        model.ChipLeadChoices.Select(c => c.Title).Should().Equal("5 min", "10 min", "15 min", "30 min", "60 min");
        foreach (var name in model.ChipModeChoices.Select(c => c.AutomationName).Concat(model.ChipLeadChoices.Select(c => c.AutomationName)))
        {
            name.Should().NotBeNullOrWhiteSpace();
        }

        model.ChipNote.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(ChipMode.BeforeMeetings)]
    [InlineData(ChipMode.Always)]
    public void Choosing_a_mode_stores_it_and_raises_changed_once(ChipMode mode)
    {
        using var model = Model();
        _storeChanges = 0;

        model.SetChipMode(mode);

        _store.Load().ChipMode.Should().Be(mode);
        model.ChipMode.Should().Be(mode);
        _storeChanges.Should().Be(1);
    }

    [Fact]
    public void Choosing_the_mode_already_chosen_or_one_that_does_not_exist_changes_nothing()
    {
        using var model = Model();
        _storeChanges = 0;

        model.SetChipMode(ChipMode.Off);
        model.SetChipMode((ChipMode)42);

        _storeChanges.Should().Be(0);
        model.ChipMode.Should().Be(ChipMode.Off);
    }

    [Fact]
    public void The_lead_choice_is_enabled_only_for_before_meetings()
    {
        using var model = Model();

        model.SetChipMode(ChipMode.BeforeMeetings);
        model.ChipLeadEnabled.Should().BeTrue();

        model.SetChipMode(ChipMode.Always);
        model.ChipLeadEnabled.Should().BeFalse();
    }

    [Fact]
    public void Choosing_the_lead_stores_it_and_raises_changed_once_and_it_is_kept_when_the_mode_changes()
    {
        using var model = Model();
        model.SetChipMode(ChipMode.BeforeMeetings);
        _storeChanges = 0;

        model.SetChipLeadMinutes(30);

        _store.Load().ChipLeadMinutes.Should().Be(30);
        _storeChanges.Should().Be(1);

        model.SetChipMode(ChipMode.Always);
        model.ChipLeadMinutes.Should().Be(30);
    }

    [Fact]
    public void The_lead_cannot_be_chosen_while_it_is_disabled_or_to_a_value_the_pane_does_not_offer()
    {
        using var model = Model();
        _storeChanges = 0;

        model.SetChipLeadMinutes(30);
        _storeChanges.Should().Be(0);
        model.ChipLeadMinutes.Should().Be(15);

        model.SetChipMode(ChipMode.BeforeMeetings);
        _storeChanges = 0;
        model.SetChipLeadMinutes(7);
        model.SetChipLeadMinutes(15);
        _storeChanges.Should().Be(0);
    }

    [Fact]
    public void The_pane_tells_its_listeners_what_changed()
    {
        using var model = Model();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        model.SetChipMode(ChipMode.BeforeMeetings);
        model.SetChipLeadMinutes(60);

        raised.Should().Contain([nameof(AppearanceViewModel.ChipMode), nameof(AppearanceViewModel.ChipLeadEnabled), nameof(AppearanceViewModel.ChipLeadMinutes)]);
    }

    [Fact]
    public void Choosing_a_chip_setting_keeps_the_other_presentation_settings()
    {
        _store.Save(PresentationSettings.Defaults with { Theme = AppTheme.Dark, SettingsWindow = new WindowGeometry(1, 2, 300, 400) });
        using var model = Model();

        model.SetChipMode(ChipMode.Always);

        _store.Load().Theme.Should().Be(AppTheme.Dark);
        _store.Load().SettingsWindow.Should().Be(new WindowGeometry(1, 2, 300, 400));
    }

    [Fact]
    public void The_chip_has_a_search_entry_in_the_appearance_pane()
    {
        var search = new SettingsSearch(new SettingsText(new ReswLocalizer()));

        search.Catalog.Should().ContainSingle(i => i.Id == AppearanceViewModel.Ids.Chip).Which.Pane.Should().Be(SettingsPane.Appearance);
        search.Results("countdown", []).Select(i => i.Id).Should().Contain(AppearanceViewModel.Ids.Chip);
        search.Results("always on top", []).Select(i => i.Id).Should().Contain(AppearanceViewModel.Ids.Chip);
    }
}
