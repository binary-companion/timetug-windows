using System.ComponentModel;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Tests.Settings;

public class TugRulesViewModelTests
{
    private static EngineSettingsView Custom => EngineSettingsView.Defaults with
    {
        Enabled = false,
        LeadTime = TimeSpan.FromMinutes(5),
        RequireConferenceLink = true,
        RequireOtherAttendees = true,
    };

    private static TugRulesViewModel Model(SettingsPaneRig rig) => new(rig.Engine, rig.Client, rig.Ui, rig.Text);

    // ---- the controls show the engine's settings ---------------------------------------------------------------------

    [Fact]
    public void Each_control_reflects_the_engines_settings()
    {
        using var rig = new SettingsPaneRig(Custom);
        using var model = Model(rig);

        model.Enabled.Should().BeFalse();
        model.SelectedLeadTime.Should().Be(new LeadTimeOption(5, "5 minutes"));
        model.RequireConferenceLink.Should().BeTrue();
        model.RequireOtherAttendees.Should().BeTrue();
    }

    [Fact]
    public void Before_the_first_state_the_controls_show_the_engines_defaults()
    {
        var engine = new Welcome.FakeEngineClient();
        using var client = new SettingsClient(engine);
        using var model = new TugRulesViewModel(engine, client, new Welcome.ImmediateDispatcher(), new Welcome.ReswLocalizer());

        model.Enabled.Should().BeTrue();
        model.SelectedLeadTime.Minutes.Should().Be(1);
        model.RequireConferenceLink.Should().BeFalse();
        model.RequireOtherAttendees.Should().BeFalse();
        model.HasMessage.Should().BeFalse();
    }

    [Fact]
    public void A_new_state_updates_the_controls_and_names_only_what_changed()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        rig.Set(rig.Settings with { LeadTime = TimeSpan.FromMinutes(10) });

        model.SelectedLeadTime.Minutes.Should().Be(10);
        raised.Should().Contain(nameof(TugRulesViewModel.SelectedLeadTime)).And.NotContain(nameof(TugRulesViewModel.Enabled));
    }

    [Fact]
    public void After_it_is_disposed_a_state_changes_nothing()
    {
        using var rig = new SettingsPaneRig();
        var model = Model(rig);
        model.Dispose();

        rig.Set(rig.Settings with { Enabled = false });

        model.Enabled.Should().BeTrue();
    }

    // ---- an edit sends one patch with only that field ----------------------------------------------------------------

    [Fact]
    public async Task Enable_Tug_sends_only_enabled()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        await model.SetEnabledAsync(false);

        var patch = rig.Received.Should().ContainSingle().Subject;
        (patch.Enabled, patch.LeadTime, patch.RequireConferenceLink, patch.RequireOtherAttendees).Should().Be((false, null, null, null));
        model.Enabled.Should().BeFalse("the engine published the new value");
    }

    [Fact]
    public async Task The_lead_time_is_sent_in_seconds_and_alone()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        await model.SetLeadMinutesAsync(10);

        var patch = rig.Received.Should().ContainSingle().Subject;
        patch.LeadTime.Should().Be(TimeSpan.FromSeconds(600));
        (patch.Enabled, patch.RequireConferenceLink, patch.RequireOtherAttendees).Should().Be((null, null, null));
        model.SelectedLeadTime.Minutes.Should().Be(10);
    }

    [Fact]
    public async Task At_start_sends_zero()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        await model.SetLeadMinutesAsync(0);

        rig.Received.Single().LeadTime.Should().Be(TimeSpan.Zero);
        model.SelectedLeadTime.Title.Should().Be("At start");
    }

    [Fact]
    public async Task The_video_link_requirement_sends_only_that_field()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        await model.SetRequireConferenceLinkAsync(true);

        var patch = rig.Received.Should().ContainSingle().Subject;
        (patch.RequireConferenceLink, patch.RequireOtherAttendees, patch.Enabled, patch.LeadTime).Should().Be((true, null, null, null));
    }

    [Fact]
    public async Task The_attendees_requirement_sends_only_that_field()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        await model.SetRequireOtherAttendeesAsync(true);

        var patch = rig.Received.Should().ContainSingle().Subject;
        (patch.RequireOtherAttendees, patch.RequireConferenceLink, patch.Enabled, patch.LeadTime).Should().Be((true, null, null, null));
    }

    [Fact]
    public async Task Setting_a_control_to_the_value_it_already_has_sends_nothing()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        await model.SetEnabledAsync(true);
        await model.SetLeadMinutesAsync(1);

        rig.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task Two_quick_edits_to_different_controls_both_reach_the_engine()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        await Task.WhenAll(model.SetLeadMinutesAsync(15), model.SetRequireConferenceLinkAsync(true), model.SetEnabledAsync(false));

        rig.Settings.LeadTime.Should().Be(TimeSpan.FromMinutes(15));
        rig.Settings.RequireConferenceLink.Should().BeTrue();
        rig.Settings.Enabled.Should().BeFalse();
        rig.Received.Should().HaveCount(3);
    }

    // ---- a patch the engine refuses or never answers ------------------------------------------------------------------

    [Fact]
    public async Task A_refused_patch_puts_the_control_back_and_says_so()
    {
        using var rig = new SettingsPaneRig { Mode = SettingsPaneRig.AnswerMode.Reject };
        using var model = Model(rig);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await model.SetEnabledAsync(false);

        model.Enabled.Should().BeTrue("the control shows what the engine still holds");
        raised.Should().Contain(nameof(TugRulesViewModel.Enabled), "the page is told to read the value again, so its switch goes back");
        model.HasMessage.Should().BeTrue();
        model.Message.Should().Be("That change wasn’t saved. The setting is back to what TimeTug is using.");
        rig.Engine.Sent.Should().Equal("settings");
    }

    [Fact]
    public async Task A_patch_the_engine_never_answers_is_given_up_and_the_control_goes_back()
    {
        using var rig = new SettingsPaneRig { Mode = SettingsPaneRig.AnswerMode.Ignore };
        using var model = Model(rig);

        var pending = model.SetLeadMinutesAsync(20);
        rig.Time.Advance(TimeSpan.FromSeconds(6));
        await pending;

        model.SelectedLeadTime.Minutes.Should().Be(1);
        model.Message.Should().NotBeNull();
    }

    [Fact]
    public async Task The_message_goes_away_with_the_next_edit_that_is_taken()
    {
        using var rig = new SettingsPaneRig { Mode = SettingsPaneRig.AnswerMode.Reject };
        using var model = Model(rig);
        await model.SetRequireConferenceLinkAsync(true);
        model.HasMessage.Should().BeTrue();

        rig.Mode = SettingsPaneRig.AnswerMode.Accept;
        await model.SetRequireConferenceLinkAsync(true);

        model.HasMessage.Should().BeFalse();
        model.RequireConferenceLink.Should().BeTrue();
    }

    // ---- the lead-time list ------------------------------------------------------------------------------------------

    [Fact]
    public void The_lead_time_list_is_the_macs()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        LeadTimeOptions.BaseMinutes.Should().Equal(0, 1, 2, 3, 5, 10, 15, 20, 30);
        model.LeadTimeOptions.Select(o => o.Minutes).Should().Equal(0, 1, 2, 3, 5, 10, 15, 20, 30);
        model.LeadTimeOptions.Select(o => o.Title).Should().Equal(
            "At start", "1 minute", "2 minutes", "3 minutes", "5 minutes", "10 minutes", "15 minutes", "20 minutes", "30 minutes");
    }

    [Fact]
    public void A_saved_lead_time_that_is_not_in_the_list_is_added_in_order()
    {
        using var rig = new SettingsPaneRig(EngineSettingsView.Defaults with { LeadTime = TimeSpan.FromMinutes(7) });
        using var model = Model(rig);

        model.LeadTimeOptions.Select(o => o.Minutes).Should().Equal(0, 1, 2, 3, 5, 7, 10, 15, 20, 30);
        model.SelectedLeadTime.Should().Be(new LeadTimeOption(7, "7 minutes"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(60, 1)]
    [InlineData(89, 1)]
    [InlineData(90, 2)]
    [InlineData(300, 5)]
    [InlineData(1800, 30)]
    public void Minutes_of_a_lead_time_round_to_whole_minutes(int seconds, int minutes) =>
        LeadTimeOptions.MinutesOf(TimeSpan.FromSeconds(seconds)).Should().Be(minutes);

    // ---- the hero and the warnings -------------------------------------------------------------------------------------

    [Fact]
    public void The_hero_text_follows_Enable_Tug()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        model.HeroTitle.Should().Be("Tug");
        model.HeroSubtitle.Should().Be("Pulls your attention to meetings before they start.");

        rig.Set(rig.Settings with { Enabled = false });

        model.HeroSubtitle.Should().Be("Off — your agenda and tray icon keep working.");
        model.HeroSubtitle.Should().NotContainEquivalentOf("widget").And.NotContainEquivalentOf("menu bar");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Each_warning_shows_exactly_when_its_requirement_is_on(bool link, bool attendees)
    {
        using var rig = new SettingsPaneRig(EngineSettingsView.Defaults with { RequireConferenceLink = link, RequireOtherAttendees = attendees });
        using var model = Model(rig);

        model.ShowLinkWarning.Should().Be(link);
        model.ShowAttendeesWarning.Should().Be(attendees);
    }

    [Fact]
    public void The_warnings_and_the_caption_use_the_macs_words()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        model.LinkWarning.Should().Be("Meetings without a link won’t tug you.");
        model.AttendeesWarning.Should().Be("Meetings without other attendees won’t tug you.");
        model.LeadTimeCaption.Should().Be("How long before a meeting starts TimeTug tugs you.");
    }

    [Fact]
    public void The_controls_have_the_search_ids_the_catalog_uses()
    {
        TugRulesViewModel.Ids.EnableTug.Should().Be("enable-tug");
        TugRulesViewModel.Ids.LeadTime.Should().Be("lead-time");
        TugRulesViewModel.Ids.VideoLink.Should().Be("video-link");
        TugRulesViewModel.Ids.RequireAttendees.Should().Be("require-attendees");
        TugRulesViewModel.Ids.TestTug.Should().Be("test-takeover");
        TugRulesViewModel.Ids.LinkWarning.Should().Be("video-link-warning");
        TugRulesViewModel.Ids.AttendeesWarning.Should().Be("require-attendees-warning");
        var catalog = new SettingsSearch(new SettingsText(new Welcome.ReswLocalizer())).Catalog.Select(c => c.Id).ToList();
        catalog.Should().Contain([TugRulesViewModel.Ids.EnableTug, TugRulesViewModel.Ids.LeadTime, TugRulesViewModel.Ids.VideoLink, TugRulesViewModel.Ids.RequireAttendees, TugRulesViewModel.Ids.TestTug]);
    }

    // ---- Test tug --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Test_tug_sends_the_engines_test_command_with_the_sample_title()
    {
        using var rig = new SettingsPaneRig();
        using var model = Model(rig);

        await model.TestTugAsync();

        rig.Engine.TestTugs.Should().Equal(("Test meeting", "TimeTug"));
        rig.Received.Should().BeEmpty("a test changes no setting");
    }

    [Fact]
    public async Task Test_tug_works_when_Enable_Tug_is_off()
    {
        using var rig = new SettingsPaneRig(Custom);
        using var model = Model(rig);
        model.Enabled.Should().BeFalse();

        await model.TestTugAsync();

        rig.Engine.TestTugs.Should().HaveCount(1);
        rig.Engine.Sent.Should().Equal("testTug");
    }
}
