using System.ComponentModel;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using Pkg = TimeTug.Engine;

namespace TimeTug.Presentation.Tests.Settings;

public class CalendarsViewModelTests
{
    private static Pkg.CalendarInfo Cal(string id, string title, string source = "google-g1", string? account = "me@gmail.com", string kind = "standard", string? color = "#112233") =>
        SettingsPaneRig.Cal(source, id, title, account, kind, color);

    private static IReadOnlyList<Pkg.CalendarInfo> Basic => [Cal("work", "Work"), Cal("home", "Home"), Cal("team", "Team")];

    private static EngineSettingsView With(IEnumerable<string>? tug = null, IEnumerable<string>? hidden = null) => EngineSettingsView.Defaults with
    {
        TakeoverCalendarKeys = new HashSet<string>(tug ?? [], StringComparer.Ordinal),
        HiddenCalendarKeys = new HashSet<string>(hidden ?? [], StringComparer.Ordinal),
    };

    private static CalendarsViewModel Model(SettingsPaneRig rig, bool inferenceSupported = true) => new(rig.Engine, rig.Client, rig.Ui, rig.Text, inferenceSupported);

    private static IReadOnlyList<CalendarRowView> Rows(CalendarsViewModel m) => [.. m.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Rows)];

    // ---- what the pane lists --------------------------------------------------------------------------------------------

    [Fact]
    public void Groups_and_calendars_map_from_the_engines_state()
    {
        using var rig = new SettingsPaneRig(With(tug: ["google-g1/work"], hidden: ["google-g1/team"]), Basic);
        using var model = Model(rig);

        var section = model.Sections.Should().ContainSingle().Subject;
        section.Title.Should().Be("Google");
        var group = section.Groups.Should().ContainSingle().Subject;
        group.Rows.Select(r => r.Title).Should().Equal("Home", "Team", "Work");
        var work = group.Rows.Single(r => r.Key == "google-g1/work");
        (work.IsShown, work.Tugs, work.ColorHex).Should().Be((true, true, "#112233"));
        var team = group.Rows.Single(r => r.Key == "google-g1/team");
        (team.IsShown, team.Tugs).Should().Be((false, false));
        group.Rows.Single(r => r.Key == "google-g1/home").Tugs.Should().BeFalse();
    }

    [Fact]
    public void A_calendar_that_disappears_from_the_state_disappears_from_the_list()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);

        rig.SetCalendars([Cal("work", "Work")]);

        Rows(model).Select(r => r.Title).Should().Equal("Work");
    }

    [Fact]
    public void With_no_calendars_at_all_the_empty_state_shows()
    {
        using var rig = new SettingsPaneRig(With(), []);
        using var model = Model(rig);

        model.IsEmpty.Should().BeTrue();
        model.Sections.Should().BeEmpty();
        model.EmptyText.Should().Be("No calendars to show yet. Add an account in Accounts.");
        model.EmptyLinkText.Should().Be("Go to Accounts");
        model.EmptyText.Should().NotContain("System Settings");
    }

    [Fact]
    public void Calendars_only_in_the_system_list_are_not_an_empty_state()
    {
        using var rig = new SettingsPaneRig(With(), [Cal("holidays", "Holidays", kind: "subscribed")]);
        using var model = Model(rig);

        model.IsEmpty.Should().BeFalse();
        model.Sections.Should().BeEmpty();
        model.SystemRows.Select(r => r.Title).Should().Equal("Holidays");
    }

    [Fact]
    public void Section_order_subtitles_and_icons_are_the_macs()
    {
        var accounts = new[]
        {
            new AccountInfo("g1", "google", "me@gmail.com", "ok", 1),
            new AccountInfo("f1", "icalsub", "Team feed", "ok", 1),
        };
        using var rig = new SettingsPaneRig(
            With(),
            [
                Cal("a", "Orphan", source: "caldav-gone", account: null),
                Cal("f", "Feed event", source: "icalsub-f1", account: null),
                Cal("w", "Work"),
                Cal("e", "Local", source: "eventkit", account: "Exchange"),
            ],
            accounts);
        using var model = Model(rig);

        model.Sections.Select(s => s.Title).Should().Equal("This PC", "Google", "iCal link", "Other");
        model.Sections.Select(s => s.Subtitle).Should().Equal(
            "Calendars on this PC", "me@gmail.com · connected directly", "Team feed · connected directly", "Sources that are no longer connected");
        model.Sections.Select(s => s.SubtitleText).Should().Equal(
            "· Calendars on this PC", "· me@gmail.com · connected directly", "· Team feed · connected directly", "· Sources that are no longer connected");
        model.Sections.Select(s => s.Icon).Should().Equal(
            CalendarSectionIcon.Calendar, CalendarSectionIcon.Provider, CalendarSectionIcon.Provider, CalendarSectionIcon.Unknown);
        model.Sections.Select(s => s.KindId).Should().Equal(string.Empty, "google", "icalsub", string.Empty);
    }

    [Fact]
    public void Only_the_system_source_names_its_accounts_and_the_others_say_all_calendars()
    {
        using var rig = new SettingsPaneRig(With(), [Cal("e", "Local", source: "eventkit", account: "Exchange"), Cal("w", "Work")]);
        using var model = Model(rig);

        model.Sections.SelectMany(s => s.Groups).Select(g => g.Label).Should().Equal("Exchange", "All calendars");
    }

    [Fact]
    public void Subscribed_and_birthday_calendars_are_set_apart_unless_they_tug()
    {
        using var rig = new SettingsPaneRig(
            With(tug: ["google-g1/holidays"]),
            [Cal("work", "Work"), Cal("holidays", "Holidays", kind: "subscribed"), Cal("bdays", "Birthdays", kind: "birthdays")]);
        using var model = Model(rig);

        Rows(model).Select(r => r.Title).Should().Equal("Holidays", "Work");
        model.SystemRows.Select(r => r.Title).Should().Equal("Birthdays");
        model.SystemRows.Should().HaveCount(1);
        model.SystemTitle.Should().Be("Subscribed and system calendars");
        model.SystemExpanded.Should().BeFalse();
    }

    [Fact]
    public void The_pane_text_is_the_macs()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);

        model.IntroText.Should().Be("Choose which calendars are allowed to tug you away from your work.");
        (model.ColumnTugCalendars, model.ColumnTug, model.OptionsTitle).Should().Be(("Tug calendars", "Tug", "Options"));
        var row = Rows(model).Single(r => r.Title == "Work");
        row.ShowAutomationName.Should().Be("Show Work in list");
        row.TugAutomationName.Should().Be("Tug for Work");
        model.Sections.Single().Groups.Single().AllAutomationName.Should().Be("Tug for every calendar in me@gmail.com");
    }

    [Fact]
    public void Each_calendar_has_the_search_id_the_catalog_uses_for_it()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);

        Rows(model).Single(r => r.Title == "Work").HighlightId.Should().Be("calendar:google-g1/work");
        CalendarsViewModel.Ids.SkipAllDay.Should().Be("skip-all-day");
        CalendarsViewModel.Ids.Inference.Should().Be("dedup-inference");
    }

    // ---- Tug per calendar and for a group ---------------------------------------------------------------------------------

    [Fact]
    public async Task All_sets_every_calendar_of_the_group_in_one_patch()
    {
        using var rig = new SettingsPaneRig(With(tug: ["other/x"]), Basic);
        using var model = Model(rig);

        await model.SetGroupTugAsync("google-g1", "me@gmail.com", true);

        var patch = rig.Received.Should().ContainSingle().Subject;
        patch.TakeoverCalendarKeys.Should().BeEquivalentTo(["google-g1/work", "google-g1/home", "google-g1/team", "other/x"]);
        patch.HiddenCalendarKeys.Should().BeNull();
        Rows(model).Should().OnlyContain(r => r.Tugs);
        model.Sections.Single().Groups.Single().AllTug.Should().BeTrue();
    }

    [Fact]
    public async Task All_off_clears_the_groups_calendars_and_leaves_the_rest()
    {
        using var rig = new SettingsPaneRig(With(tug: ["google-g1/work", "google-g1/home", "google-g1/team", "other/x"]), Basic);
        using var model = Model(rig);

        await model.SetGroupTugAsync("google-g1", "me@gmail.com", false);

        rig.Received.Should().ContainSingle().Which.TakeoverCalendarKeys.Should().BeEquivalentTo(["other/x"]);
        model.Sections.Single().Groups.Single().AllTug.Should().BeFalse();
    }

    [Fact]
    public async Task All_also_shows_the_hidden_calendars_it_turns_on()
    {
        using var rig = new SettingsPaneRig(With(hidden: ["google-g1/team", "other/y"]), Basic);
        using var model = Model(rig);

        await model.SetGroupTugAsync("google-g1", "me@gmail.com", true);

        var patch = rig.Received.Should().ContainSingle().Subject;
        patch.TakeoverCalendarKeys.Should().HaveCount(3);
        patch.HiddenCalendarKeys.Should().BeEquivalentTo(["other/y"]);
    }

    [Fact]
    public void The_all_switch_is_on_only_when_every_calendar_of_the_group_tugs()
    {
        using var rig = new SettingsPaneRig(With(tug: ["google-g1/work", "google-g1/home"]), Basic);
        using var model = Model(rig);

        model.Sections.Single().Groups.Single().AllTug.Should().BeFalse();
    }

    [Fact]
    public async Task All_for_a_group_that_is_gone_sends_nothing()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);

        await model.SetGroupTugAsync("google-g1", "someone@else", true);

        rig.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task Tug_on_adds_the_key_and_shows_a_hidden_calendar()
    {
        using var rig = new SettingsPaneRig(With(hidden: ["google-g1/work"]), Basic);
        using var model = Model(rig);

        await model.SetCalendarTugAsync("google-g1/work", true);

        var patch = rig.Received.Should().ContainSingle().Subject;
        patch.TakeoverCalendarKeys.Should().BeEquivalentTo(["google-g1/work"]);
        patch.HiddenCalendarKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Tug_on_for_a_shown_calendar_changes_only_the_tug_list()
    {
        using var rig = new SettingsPaneRig(With(tug: ["google-g1/home"]), Basic);
        using var model = Model(rig);

        await model.SetCalendarTugAsync("google-g1/work", true);

        var patch = rig.Received.Should().ContainSingle().Subject;
        patch.TakeoverCalendarKeys.Should().BeEquivalentTo(["google-g1/home", "google-g1/work"]);
        patch.HiddenCalendarKeys.Should().BeNull();
    }

    [Fact]
    public async Task Tug_off_removes_only_that_key_and_leaves_visibility()
    {
        using var rig = new SettingsPaneRig(With(tug: ["google-g1/home", "google-g1/work"]), Basic);
        using var model = Model(rig);

        await model.SetCalendarTugAsync("google-g1/work", false);

        var patch = rig.Received.Should().ContainSingle().Subject;
        patch.TakeoverCalendarKeys.Should().BeEquivalentTo(["google-g1/home"]);
        patch.HiddenCalendarKeys.Should().BeNull();
    }

    [Fact]
    public async Task A_key_the_engine_does_not_list_is_ignored()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);

        await model.SetCalendarTugAsync("google-g1/gone", true);
        await model.SetCalendarShownAsync("google-g1/gone", false);

        rig.Received.Should().BeEmpty();
    }

    // ---- visibility -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Hiding_a_calendar_that_does_not_tug_updates_the_hidden_list_only()
    {
        using var rig = new SettingsPaneRig(With(tug: ["google-g1/home"]), Basic);
        using var model = Model(rig);

        await model.SetCalendarShownAsync("google-g1/work", false);

        var patch = rig.Received.Should().ContainSingle().Subject;
        patch.HiddenCalendarKeys.Should().BeEquivalentTo(["google-g1/work"]);
        patch.TakeoverCalendarKeys.Should().BeNull();
    }

    [Fact]
    public async Task Hiding_a_calendar_that_tugs_also_takes_it_out_of_the_tug_list()
    {
        using var rig = new SettingsPaneRig(With(tug: ["google-g1/work", "google-g1/home"]), Basic);
        using var model = Model(rig);

        await model.SetCalendarShownAsync("google-g1/work", false);

        var patch = rig.Received.Should().ContainSingle().Subject;
        patch.HiddenCalendarKeys.Should().BeEquivalentTo(["google-g1/work"]);
        patch.TakeoverCalendarKeys.Should().BeEquivalentTo(["google-g1/home"]);
    }

    [Fact]
    public async Task Showing_a_calendar_updates_the_hidden_list_only()
    {
        using var rig = new SettingsPaneRig(With(hidden: ["google-g1/work", "google-g1/home"]), Basic);
        using var model = Model(rig);

        await model.SetCalendarShownAsync("google-g1/work", true);

        var patch = rig.Received.Should().ContainSingle().Subject;
        patch.HiddenCalendarKeys.Should().BeEquivalentTo(["google-g1/home"]);
        patch.TakeoverCalendarKeys.Should().BeNull();
    }

    [Fact]
    public void A_calendar_hidden_from_the_list_has_its_tug_switch_disabled_with_the_reason()
    {
        using var rig = new SettingsPaneRig(With(hidden: ["google-g1/team"]), Basic);
        using var model = Model(rig);

        var team = Rows(model).Single(r => r.Title == "Team");
        var work = Rows(model).Single(r => r.Title == "Work");
        team.TugEnabled.Should().BeFalse();
        team.TugToolTip.Should().Be("Show this calendar to turn on Tug for it");
        team.ShowToolTip.Should().Be("Show in the list");
        work.TugEnabled.Should().BeTrue();
        work.TugToolTip.Should().BeEmpty();
        work.ShowToolTip.Should().Be("Hide from the list");
    }

    [Fact]
    public async Task Hiding_a_tugging_subscribed_calendar_opens_the_system_list_so_the_row_does_not_vanish()
    {
        using var rig = new SettingsPaneRig(With(tug: ["google-g1/holidays"]), [Cal("work", "Work"), Cal("holidays", "Holidays", kind: "subscribed")]);
        using var model = Model(rig);
        model.SystemExpanded.Should().BeFalse();

        await model.SetCalendarShownAsync("google-g1/holidays", false);

        model.SystemExpanded.Should().BeTrue();
        model.SystemRows.Select(r => r.Title).Should().Equal("Holidays");
    }

    [Fact]
    public async Task Hiding_an_ordinary_calendar_leaves_the_system_list_closed()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);

        await model.SetCalendarShownAsync("google-g1/work", false);

        model.SystemExpanded.Should().BeFalse();
    }

    // ---- options ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Skip_all_day_patches_one_field()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);
        model.SkipAllDay.Should().BeTrue();

        await model.SetSkipAllDayAsync(false);

        var patch = rig.Received.Should().ContainSingle().Subject;
        (patch.SkipAllDayEvents, patch.InferenceEnabled, patch.HiddenCalendarKeys, patch.TakeoverCalendarKeys).Should().Be((false, null, null, null));
        model.SkipAllDay.Should().BeFalse();
    }

    [Fact]
    public async Task The_inference_switch_patches_one_field_when_it_can_run()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);
        model.InferenceSwitchEnabled.Should().BeTrue();

        await model.SetInferenceAsync(true);

        var patch = rig.Received.Should().ContainSingle().Subject;
        (patch.InferenceEnabled, patch.SkipAllDayEvents).Should().Be((true, null));
        model.InferenceIsOn.Should().BeTrue();
    }

    [Theory]
    [InlineData(InferenceAvailability.NoEngine, null, "No on-device model is available on this PC. Using rules only.")]
    [InlineData(InferenceAvailability.Unavailable, "model missing", "The on-device model isn’t available (model missing). Using rules only.")]
    [InlineData(InferenceAvailability.NotOnDevice, null, "That engine doesn’t run on your device, so it isn’t used. Using rules only.")]
    public async Task When_inference_cannot_run_the_switch_is_off_and_disabled_and_the_status_says_why(InferenceAvailability availability, string? reason, string text)
    {
        using var rig = new SettingsPaneRig(With() with { InferenceEnabled = true }, Basic) { Inference = availability, InferenceReason = reason };
        rig.Publish();
        using var model = Model(rig);

        model.InferenceSwitchEnabled.Should().BeFalse();
        model.InferenceIsOn.Should().BeFalse("the stored value stays, but the switch shows what actually runs");
        model.InferenceStatusText.Should().Be(text);

        await model.SetInferenceAsync(true);
        await model.SetInferenceAsync(false);

        rig.Received.Should().BeEmpty("nothing is persisted from a disabled switch");
        rig.Settings.InferenceEnabled.Should().BeTrue("the engine's stored value is left alone");
    }

    [Fact]
    public async Task A_pc_with_no_text_generator_keeps_the_switch_disabled_even_while_the_engine_says_only_disabled()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig, inferenceSupported: false);

        model.InferenceSwitchEnabled.Should().BeFalse();
        model.InferenceIsOn.Should().BeFalse();
        model.InferenceStatusText.Should().Be("No on-device model is available on this PC. Using rules only.");
        await model.SetInferenceAsync(true);
        rig.Received.Should().BeEmpty();
    }

    [Fact]
    public void An_active_engine_is_named_and_a_disabled_one_says_nothing()
    {
        using var rig = new SettingsPaneRig(With() with { InferenceEnabled = true }, Basic) { Inference = InferenceAvailability.Active, InferenceEngine = "Phi" };
        rig.Publish();
        using var model = Model(rig);

        model.InferenceStatusText.Should().Be("Using Phi.");
        model.InferenceIsOn.Should().BeTrue();

        rig.Inference = InferenceAvailability.Disabled;
        rig.Set(rig.Settings with { InferenceEnabled = false });

        model.InferenceStatusText.Should().BeNull();
        model.InferenceIsOn.Should().BeFalse();
    }

    [Fact]
    public void The_explanation_says_it_runs_on_this_pc()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);

        model.InferenceExplanation.Should().Contain("runs on your PC").And.NotContain("Mac").And.NotContain("Apple");
    }

    // ---- forgetting learned corrections ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Forgetting_asks_first_and_sends_only_after_the_confirmation()
    {
        using var rig = new SettingsPaneRig(With() with { InferenceEnabled = true }, Basic) { Inference = InferenceAvailability.Active };
        rig.Publish();
        using var model = Model(rig);
        model.ShowForget.Should().BeTrue();
        model.ConfirmForget.Should().BeFalse();

        model.BeginForget();

        model.ConfirmForget.Should().BeTrue();
        rig.Engine.Sent.Should().BeEmpty("asking sends nothing");

        await model.ForgetLearnedCorrectionsAsync();

        rig.Engine.Sent.Should().Equal("forget");
        model.ConfirmForget.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelling_the_question_forgets_nothing()
    {
        using var rig = new SettingsPaneRig(With() with { InferenceEnabled = true }, Basic) { Inference = InferenceAvailability.Active };
        rig.Publish();
        using var model = Model(rig);

        model.BeginForget();
        model.CancelForget();
        await model.ForgetLearnedCorrectionsAsync();

        model.ConfirmForget.Should().BeFalse();
        rig.Engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public void The_forget_button_shows_only_while_inference_is_on_and_can_run()
    {
        using var off = new SettingsPaneRig(With(), Basic);
        using var offModel = Model(off);
        offModel.ShowForget.Should().BeFalse();

        using var unavailable = new SettingsPaneRig(With() with { InferenceEnabled = true }, Basic) { Inference = InferenceAvailability.NoEngine };
        unavailable.Publish();
        using var unavailableModel = Model(unavailable);
        unavailableModel.ShowForget.Should().BeFalse();
    }

    // ---- refusals ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_refused_patch_puts_the_switch_back_and_says_so()
    {
        using var rig = new SettingsPaneRig(With(), Basic) { Mode = SettingsPaneRig.AnswerMode.Reject };
        using var model = Model(rig);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)model).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await model.SetCalendarTugAsync("google-g1/work", true);

        Rows(model).Single(r => r.Title == "Work").Tugs.Should().BeFalse();
        raised.Should().Contain(nameof(CalendarsViewModel.Sections), "the page rebuilds its rows from the engine's values");
        model.HasMessage.Should().BeTrue();
        model.Message.Should().Be("That change wasn’t saved. The setting is back to what TimeTug is using.");
    }

    [Fact]
    public async Task A_new_state_rebuilds_the_list_and_the_message_goes_with_the_next_taken_edit()
    {
        using var rig = new SettingsPaneRig(With(), Basic) { Mode = SettingsPaneRig.AnswerMode.Reject };
        using var model = Model(rig);
        await model.SetSkipAllDayAsync(false);
        model.HasMessage.Should().BeTrue();

        rig.Mode = SettingsPaneRig.AnswerMode.Accept;
        await model.SetSkipAllDayAsync(false);

        model.HasMessage.Should().BeFalse();
        model.SkipAllDay.Should().BeFalse();
    }

    [Fact]
    public async Task Two_quick_tug_toggles_both_reach_the_engine()
    {
        using var rig = new SettingsPaneRig(With(), Basic);
        using var model = Model(rig);

        await Task.WhenAll(model.SetCalendarTugAsync("google-g1/work", true), model.SetCalendarTugAsync("google-g1/home", true));

        rig.Settings.TakeoverCalendarKeys.Should().BeEquivalentTo(["google-g1/work", "google-g1/home"]);
    }
}
