using System.Text.Json;
using System.Text.Json.Nodes;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using Pkg = TimeTug.Engine;

namespace TimeTug.Presentation.Tests.Settings;

public class EngineSettingsMappingTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private static JsonNode GoldenPayload()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "state.json");
        return JsonNode.Parse(File.ReadAllText(path))!["payload"]!;
    }

    private static Pkg.EngineState Read(JsonNode payload) => payload.Deserialize<Pkg.EngineState>(WireOptions)!;

    // ---- engine state -> view ---------------------------------------------------------------------------------

    [Fact]
    public void Every_engine_setting_maps_from_the_golden_state()
    {
        var settings = EngineMapping.ToAppState(Read(GoldenPayload())).Settings;

        settings.Enabled.Should().BeTrue();
        settings.LeadTime.Should().Be(TimeSpan.FromSeconds(120));
        settings.RequireConferenceLink.Should().BeFalse();
        settings.RequireOtherAttendees.Should().BeFalse();
        settings.SkipAllDayEvents.Should().BeTrue();
        settings.InferenceEnabled.Should().BeTrue();
        settings.HiddenCalendarKeys.Should().BeEquivalentTo("google-1/holidays");
        settings.TakeoverCalendarKeys.Should().BeEquivalentTo("google-1/primary");
    }

    [Fact]
    public void Each_setting_follows_the_engine_when_it_changes()
    {
        var state = new Pkg.EngineState
        {
            Settings = new Pkg.EngineSettings
            {
                InferenceEnabled = false,
                Takeover = new Pkg.TakeoverSettings
                {
                    Enabled = false,
                    LeadTime = 300,
                    RequireConferenceLink = true,
                    RequireOtherAttendees = true,
                    SkipAllDayEvents = false,
                    HiddenCalendarKeys = ["a/2", "a/3"],
                    TakeoverCalendarKeys = ["a/1"],
                },
            },
        };

        var mapped = EngineMapping.ToAppState(state);

        mapped.Settings.Enabled.Should().BeFalse();
        mapped.Enabled.Should().BeFalse();
        mapped.Settings.LeadTime.Should().Be(TimeSpan.FromMinutes(5));
        mapped.Settings.RequireConferenceLink.Should().BeTrue();
        mapped.Settings.RequireOtherAttendees.Should().BeTrue();
        mapped.Settings.SkipAllDayEvents.Should().BeFalse();
        mapped.Settings.InferenceEnabled.Should().BeFalse();
        mapped.Settings.HiddenCalendarKeys.Should().BeEquivalentTo("a/2", "a/3");
        mapped.Settings.TakeoverCalendarKeys.Should().BeEquivalentTo("a/1");
    }

    [Fact]
    public void Fields_the_engine_adds_later_are_ignored()
    {
        var payload = GoldenPayload();
        payload["settings"]!["futureSwitch"] = true;
        payload["settings"]!["takeover"]!["futureRule"] = "x";
        payload["inferenceStatus"]!["futureField"] = 7;

        var mapped = EngineMapping.ToAppState(Read(payload));

        mapped.Settings.LeadTime.Should().Be(TimeSpan.FromSeconds(120));
        mapped.Inference.Should().Be(InferenceAvailability.Active);
    }

    [Fact]
    public void A_state_built_without_settings_carries_the_engines_defaults_and_agrees_with_itself()
    {
        var state = new AppState([], [], [], null, null, false, false) { LeadTime = TimeSpan.FromMinutes(3) };

        state.Settings.Enabled.Should().BeFalse("the Enabled the state was built with is the one Settings reports");
        state.Settings.LeadTime.Should().Be(TimeSpan.FromMinutes(3));
        state.Settings.SkipAllDayEvents.Should().BeTrue();
        state.Settings.TakeoverCalendarKeys.Should().BeEmpty();
        state.Settings.HiddenCalendarKeys.Should().BeEmpty();
        EngineSettingsView.Defaults.LeadTime.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void The_revision_comes_from_the_engine()
    {
        EngineMapping.ToAppState(new Pkg.EngineState { Revision = 41 }).Revision.Should().Be(41UL);
    }

    // ---- inference ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("disabled", InferenceAvailability.Disabled)]
    [InlineData("noEngine", InferenceAvailability.NoEngine)]
    [InlineData("unavailable", InferenceAvailability.Unavailable)]
    [InlineData("notOnDevice", InferenceAvailability.NotOnDevice)]
    [InlineData("active", InferenceAvailability.Active)]
    public void Each_inference_status_the_engine_names_maps_to_its_availability(string kind, InferenceAvailability expected)
    {
        var state = new Pkg.EngineState { InferenceStatus = new Pkg.InferenceStatus { Kind = kind } };

        EngineMapping.ToAppState(state).Inference.Should().Be(expected);
    }

    [Fact]
    public void A_status_word_a_later_engine_adds_counts_as_unavailable_so_the_switch_stays_off()
    {
        var state = new Pkg.EngineState { InferenceStatus = new Pkg.InferenceStatus { Kind = "quantum" } };

        EngineMapping.ToAppState(state).Inference.Should().Be(InferenceAvailability.Unavailable);
    }

    [Fact]
    public void The_engines_reason_and_model_name_travel_with_the_status()
    {
        var unavailable = EngineMapping.ToAppState(new Pkg.EngineState
        {
            InferenceStatus = new Pkg.InferenceStatus { Kind = "unavailable", Reason = "model not downloaded" },
        });
        var active = EngineMapping.ToAppState(new Pkg.EngineState
        {
            InferenceStatus = new Pkg.InferenceStatus { Kind = "active", EngineName = "Apple Intelligence", Reason = "ignored" },
        });
        var none = EngineMapping.ToAppState(new Pkg.EngineState { InferenceStatus = new Pkg.InferenceStatus { Kind = "noEngine", Reason = "  " } });

        unavailable.InferenceReason.Should().Be("model not downloaded");
        active.InferenceEngineName.Should().Be("Apple Intelligence");
        none.InferenceReason.Should().BeNull();
        none.InferenceEngineName.Should().BeNull();
    }

    [Fact]
    public void Only_disabled_and_active_can_run_here()
    {
        InferenceAvailability.Disabled.CanRunHere().Should().BeTrue();
        InferenceAvailability.Active.CanRunHere().Should().BeTrue();
        InferenceAvailability.NoEngine.CanRunHere().Should().BeFalse();
        InferenceAvailability.Unavailable.CanRunHere().Should().BeFalse();
        InferenceAvailability.NotOnDevice.CanRunHere().Should().BeFalse();
    }

    // ---- patch -> command ----------------------------------------------------------------------------------------

    [Fact]
    public void An_empty_patch_sends_no_field()
    {
        var command = EngineSettingsMapping.ToCommand(new EngineSettingsPatch());

        command.Should().Be(new Pkg.EngineCommand.ApplySettings());
        new EngineSettingsPatch().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_patch_carries_only_the_fields_that_were_set_and_lead_time_goes_as_seconds()
    {
        var command = EngineSettingsMapping.ToCommand(new EngineSettingsPatch { LeadTime = TimeSpan.FromMinutes(5), RequireConferenceLink = true });

        command.Should().Be(new Pkg.EngineCommand.ApplySettings { LeadTime = 300, RequireConferenceLink = true });
    }

    [Fact]
    public void A_patch_maps_every_field_to_its_engine_name()
    {
        var command = EngineSettingsMapping.ToCommand(new EngineSettingsPatch
        {
            Enabled = false,
            LeadTime = TimeSpan.Zero,
            RequireConferenceLink = true,
            RequireOtherAttendees = true,
            SkipAllDayEvents = false,
            InferenceEnabled = true,
            HiddenCalendarKeys = new HashSet<string> { "a/2" },
            TakeoverCalendarKeys = new HashSet<string> { "a/1" },
        });

        command.Enabled.Should().BeFalse();
        command.LeadTime.Should().Be(0);
        command.RequireConferenceLink.Should().BeTrue();
        command.RequireOtherAttendees.Should().BeTrue();
        command.SkipAllDayEvents.Should().BeFalse();
        command.InferenceEnabled.Should().BeTrue();
        command.HiddenCalendarKeys.Should().Equal("a/2");
        command.TakeoverCalendarKeys.Should().Equal("a/1");
        command.SystemCalendarEnabled.Should().BeNull("the app never touches the system-calendar switch");
    }

    [Fact]
    public void Key_sets_are_sent_in_a_stable_order_and_an_empty_set_is_sent_as_an_empty_list()
    {
        var command = EngineSettingsMapping.ToCommand(new EngineSettingsPatch
        {
            TakeoverCalendarKeys = new HashSet<string> { "b/1", "a/9", "a/10" },
            HiddenCalendarKeys = new HashSet<string>(),
        });

        command.TakeoverCalendarKeys.Should().Equal("a/10", "a/9", "b/1");
        command.HiddenCalendarKeys.Should().NotBeNull().And.BeEmpty("an empty set clears the list; null would leave it alone");
    }

    [Fact]
    public void A_patch_that_repeats_the_current_values_changes_nothing()
    {
        var current = EngineSettingsView.Defaults with { TakeoverCalendarKeys = new HashSet<string> { "a/1" } };

        new EngineSettingsPatch { Current = current, TakeoverCalendarKeys = new HashSet<string> { "a/1" }, LeadTime = TimeSpan.FromSeconds(60) }
            .ChangesNothing.Should().BeTrue();
        new EngineSettingsPatch { Current = current, TakeoverCalendarKeys = new HashSet<string> { "a/1", "a/2" } }
            .ChangesNothing.Should().BeFalse();
    }
}
