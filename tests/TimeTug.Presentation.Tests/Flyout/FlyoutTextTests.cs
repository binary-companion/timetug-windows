using System.Globalization;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Flyout;

/// <summary>The wording rules, with the expected values of the Mac's PopupLogicTests and TimeFormattingTests.</summary>
public sealed class FlyoutTextTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private readonly FlyoutText _text = new(new ReswLocalizer(), CultureInfo.GetCultureInfo("en-US"));

    /// <summary>2026-09-18 at the given UTC hour and minute (a Friday), as the Mac tests use.</summary>
    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 9, 18, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Weekday_is_the_full_day_name_in_the_given_zone()
    {
        _text.Weekday(At(10), Utc).Should().Be("Friday");
        _text.Weekday(At(23, 30), TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2")).Should().Be("Saturday");
    }

    [Theory]
    [InlineData(3, 4, "Sep 18 · 3 meetings left")]
    [InlineData(1, 4, "Sep 18 · 1 meeting left")]
    [InlineData(0, 4, "Sep 18 · No meetings left")]
    [InlineData(0, 0, "Sep 18 · No meetings today")]
    public void Summary_has_the_short_date_and_how_many_meetings_are_left(int left, int total, string expected) =>
        _text.Summary(At(10), left, total, Utc).Should().Be(expected);

    [Fact]
    public void Summary_orders_day_and_month_the_way_the_culture_does()
    {
        var german = new FlyoutText(new ReswLocalizer(), CultureInfo.GetCultureInfo("de-DE"));

        german.Summary(At(10), 2, 2, Utc).Should().StartWith("18. Sep");
    }

    [Theory]
    [InlineData(45, "45s")]
    [InlineData(60, "1m")]
    [InlineData(299, "4m")]
    [InlineData(3900, "1h 5m")]
    [InlineData(10800, "3h 0m")]
    [InlineData(-3, "0s")]
    public void Compact_truncates_like_the_Macs_menu_bar_countdown(int seconds, string expected) =>
        _text.Compact(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Fact]
    public void Countdown_is_in_followed_by_the_compact_time()
    {
        _text.Countdown(At(19, 15) - At(15, 35)).Should().Be("in 3h 40m");
        _text.Countdown(At(15, 40) - At(15, 35)).Should().Be("in 5m");
    }

    [Theory]
    [InlineData(0, "Tugs you at start")]
    [InlineData(60, "Tugs you 1 min before")]
    [InlineData(300, "Tugs you 5 min before")]
    [InlineData(90, "Tugs you 2 min before")]
    [InlineData(29, "Tugs you at start")]
    [InlineData(30, "Tugs you 1 min before")]
    public void The_footer_says_how_long_before_a_meeting_the_tug_fires(int seconds, string expected) =>
        _text.TugFooter(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Theory]
    [InlineData(3600, "1 h")]
    [InlineData(5400, "1 h 30 min")]
    [InlineData(2700, "45 min")]
    [InlineData(1800, "30 min")]
    [InlineData(0, "0 min")]
    [InlineData(-60, "0 min")]
    public void Duration_is_rounded_to_whole_minutes(int seconds, string expected) =>
        _text.Duration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Theory]
    [InlineData(13200, "3 hours 40 minutes")]
    [InlineData(3600, "1 hour")]
    [InlineData(60, "1 minute")]
    [InlineData(30, "less than a minute")]
    public void Spoken_duration_is_written_out_for_a_screen_reader(int seconds, string expected) =>
        _text.SpokenDuration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Fact]
    public void Progress_is_the_fraction_elapsed_clamped_to_zero_and_one()
    {
        FlyoutText.Progress(At(10), At(11), At(9)).Should().Be(0);
        FlyoutText.Progress(At(10), At(11), At(10, 30)).Should().BeApproximately(0.5, 0.0001);
        FlyoutText.Progress(At(10), At(11), At(12)).Should().Be(1);
        FlyoutText.Progress(At(11), At(11), At(11)).Should().Be(0);
        FlyoutText.Progress(At(11), At(10), At(12)).Should().Be(0);
    }

    [Fact]
    public void Clock_uses_a_plain_space_before_AM_or_PM_whatever_the_ICU_data_writes()
    {
        var clock = _text.Clock(At(19, 15), Utc);

        clock.Should().Be("7:15 PM");
        clock.Should().NotContain(" ").And.NotContain(" ");
    }

    [Fact]
    public void Clock_is_in_the_given_zone()
    {
        _text.Clock(At(19, 15), TimeZoneInfo.CreateCustomTimeZone("minus5", TimeSpan.FromHours(-5), "minus5", "minus5")).Should().Be("2:15 PM");
    }

    [Theory]
    [InlineData(19, 15, 20, 0, "7:15 – 8:00 PM")]
    [InlineData(11, 30, 12, 15, "11:30 AM – 12:15 PM")]
    [InlineData(9, 0, 9, 30, "9:00 – 9:30 AM")]
    [InlineData(21, 0, 22, 30, "9:00 – 10:30 PM")]
    public void Range_writes_AM_or_PM_once_when_both_ends_share_it(int h1, int m1, int h2, int m2, string expected) =>
        _text.Range(At(h1, m1), At(h2, m2), Utc).Should().Be(expected);

    [Fact]
    public void Range_in_a_24_hour_culture_has_no_marker()
    {
        var german = new FlyoutText(new ReswLocalizer(), CultureInfo.GetCultureInfo("de-DE"));

        german.Range(At(19, 15), At(20, 0), Utc).Should().Be("19:15 – 20:00");
    }

    [Fact]
    public void Spoken_range_says_to_instead_of_a_dash()
    {
        _text.SpokenRange(At(19, 15), At(20, 0), Utc).Should().Be("7:15 to 8:00 PM");
    }

    [Theory]
    [InlineData("https://us02web.zoom.us/j/1", "Join Zoom")]
    [InlineData("https://zoom.com/j/1", "Join Zoom")]
    [InlineData("zoommtg://zoom.us/join?confno=1", "Join Zoom")]
    [InlineData("https://meet.google.com/aaa-bbbb-ccc", "Join Google Meet")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/1", "Join Teams")]
    [InlineData("https://teams.live.com/meet/1", "Join Teams")]
    [InlineData("https://acme.webex.com/meet/x", "Join Webex")]
    [InlineData("https://app.slack.com/huddle/T1/C1", "Join Slack huddle")]
    [InlineData("https://example.com/room", "Join meeting")]
    [InlineData("https://notzoom.us.evil.example/x", "Join meeting")]
    public void The_join_label_names_the_provider_by_host(string url, string expected) =>
        _text.JoinLabel(new Uri(url)).Should().Be(expected);
}
