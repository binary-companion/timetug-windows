using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Tests.Chip;

public class ChipSettingsTests
{
    private static PresentationSettings Read(Dictionary<string, object> values) =>
        PresentationSettingsCodec.Read(key => values.GetValueOrDefault(key));

    private static Dictionary<string, object> Written(PresentationSettings settings)
    {
        var values = new Dictionary<string, object>();
        foreach (var (key, value) in PresentationSettingsCodec.Write(settings))
        {
            if (value is not null)
            {
                values[key] = value;
            }
        }

        return values;
    }

    [Fact]
    public void A_fresh_install_has_the_chip_off_fifteen_minutes_ahead_and_no_remembered_place()
    {
        var settings = Read([]);

        settings.ChipMode.Should().Be(ChipMode.Off);
        settings.ChipLeadMinutes.Should().Be(15);
        settings.ChipPositions.Should().BeEmpty();
        settings.Should().Be(PresentationSettings.Defaults);
    }

    [Fact]
    public void The_lead_choices_are_five_ten_fifteen_thirty_and_sixty()
    {
        ChipSettings.LeadChoices.Should().Equal(5, 10, 15, 30, 60);
        ChipSettings.LeadChoices.Should().Contain(ChipSettings.DefaultLeadMinutes);
    }

    [Fact]
    public void Every_chip_field_round_trips()
    {
        var all = PresentationSettings.Defaults with
        {
            ChipMode = ChipMode.BeforeMeetings,
            ChipLeadMinutes = 30,
            ChipPositions = [new ChipPosition(@"\\.\DISPLAY1", 12.5, 40), new ChipPosition("second;monitor|1", -300.25, 8)],
        };

        Read(Written(all)).Should().Be(all);
    }

    [Fact]
    public void No_remembered_place_is_written_as_no_value_so_the_key_can_be_removed()
    {
        var pairs = PresentationSettingsCodec.Write(PresentationSettings.Defaults);

        pairs.Single(p => p.Key == PresentationSettingsCodec.ChipPositionsKey).Value.Should().BeNull();
    }

    [Theory]
    [InlineData("Purple")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("99")]
    [InlineData("always")]
    public void A_chip_mode_that_is_not_one_of_the_names_is_off_and_the_other_fields_survive(string corrupt)
    {
        var values = Written(PresentationSettings.Defaults with { Theme = AppTheme.Dark, ChipMode = ChipMode.Always, ChipLeadMinutes = 60 });
        values[PresentationSettingsCodec.ChipModeKey] = corrupt;

        var settings = Read(values);

        settings.ChipMode.Should().Be(ChipMode.Off);
        settings.Theme.Should().Be(AppTheme.Dark);
        settings.ChipLeadMinutes.Should().Be(60);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("fifteen")]
    [InlineData("")]
    [InlineData("15.5")]
    [InlineData("999999999999")]
    public void A_lead_time_that_is_not_one_of_the_choices_is_fifteen(string corrupt)
    {
        var values = Written(PresentationSettings.Defaults with { ChipLeadMinutes = 30 });
        values[PresentationSettingsCodec.ChipLeadKey] = corrupt;

        Read(values).ChipLeadMinutes.Should().Be(15);
    }

    [Fact]
    public void A_value_of_the_wrong_type_is_the_default()
    {
        var settings = Read(new Dictionary<string, object>
        {
            [PresentationSettingsCodec.ChipModeKey] = 2,
            [PresentationSettingsCodec.ChipLeadKey] = 30,
            [PresentationSettingsCodec.ChipPositionsKey] = 7,
        });

        settings.Should().Be(PresentationSettings.Defaults);
    }

    [Fact]
    public void A_corrupt_position_is_dropped_and_the_good_ones_are_kept()
    {
        var values = new Dictionary<string, object>
        {
            [PresentationSettingsCodec.ChipPositionsKey] = "m1|10|20;garbage;m2|x|5;|1|2;m3|1e999|2;m4|100001|2;m5|1|2|3;m6|-4.5|9",
        };

        Read(values).ChipPositions.Should().Equal(new ChipPosition("m1", 10, 20), new ChipPosition("m6", -4.5, 9));
    }

    [Fact]
    public void A_monitor_is_remembered_once_and_the_last_entry_wins()
    {
        var values = new Dictionary<string, object> { [PresentationSettingsCodec.ChipPositionsKey] = "m1|1|2;m2|3|4;m1|5|6" };

        Read(values).ChipPositions.Should().Equal(new ChipPosition("m2", 3, 4), new ChipPosition("m1", 5, 6));
    }

    [Fact]
    public void Only_the_most_recent_sixteen_places_are_kept()
    {
        var many = Enumerable.Range(0, 20).Select(i => new ChipPosition("m" + i, i, i)).ToArray();

        var read = Read(Written(PresentationSettings.Defaults with { ChipPositions = new ValueList<ChipPosition>(many) }));

        read.ChipPositions.Should().HaveCount(16);
        read.ChipPositions[0].MonitorId.Should().Be("m4");
        read.ChipPositions[15].MonitorId.Should().Be("m19");
    }

    [Fact]
    public void Positions_are_written_in_invariant_digits_with_at_most_two_decimals()
    {
        var pairs = PresentationSettingsCodec.Write(PresentationSettings.Defaults with { ChipPositions = [new ChipPosition("a", 1.005, -2.5)] });

        pairs.Single(p => p.Key == PresentationSettingsCodec.ChipPositionsKey).Value.Should().Match("a|1*|-2.5");
    }
}
