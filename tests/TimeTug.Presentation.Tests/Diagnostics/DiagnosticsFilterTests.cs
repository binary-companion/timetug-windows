using TimeTug.Presentation.Diagnostics;

namespace TimeTug.Presentation.Tests.Diagnostics;

/// <summary>
/// The safety net over the log: the app's own logger never writes a link with a query, a token or an event title, and if something did slip in,
/// the reader drops the whole line before it can be copied.
/// </summary>
public class DiagnosticsFilterTests
{
    private static string Line(string message, string category = "sync", string level = "info") => $"2026-10-21T10:15:30.000Z [{level}] {category}: {message}";

    [Theory]
    [InlineData("the engine fetched 3 calendars")]
    [InlineData("Request to www.googleapis.com failed: HttpRequestException")]
    [InlineData("the system instance registry is not available (COMException); using the mutex guard")]
    [InlineData("Windows refused the shortcut (error 1409)")]
    [InlineData("the token refresh finished")]
    [InlineData("see https://www.googleapis.com/calendar/v3 for details")]
    public void An_ordinary_line_is_kept(string message) => DiagnosticsFilter.IsSafe(Line(message)).Should().BeTrue();

    [Theory]
    [InlineData("GET https://accounts.example.com/o/oauth2/auth?client_id=abc&response_type=code")]
    [InlineData("redirected to http://127.0.0.1:5000/?code=4%2F0AbCdEf&state=xyz")]
    [InlineData("opened https://example.com/path?x=1")]
    [InlineData("ftp://host/file?name=a")]
    [InlineData("failed: /callback?code=abc")]
    [InlineData("failed: callback?token=abc")]
    [InlineData("took &access_token=abc123")]
    public void A_link_with_a_query_string_is_dropped(string message) => DiagnosticsFilter.IsSafe(Line(message)).Should().BeFalse();

    [Theory]
    [InlineData("access_token=ya29.A0ARrdaM-abcdef")]
    [InlineData("refresh_token: 1//0gAbCdEfGhIjKlMnOpQrStUv")]
    [InlineData("the header was Authorization: Bearer abcdefghijklmnop")]
    [InlineData("sent Bearer abcdefghijklmnop")]
    [InlineData("sent Basic dXNlcjpwYXNzd29yZA==")]
    [InlineData("id is eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature")]
    [InlineData("a cookie ya29.A0ARrdaM-abcdef was set")]
    [InlineData("client_secret=GOCSPX-abc")]
    [InlineData("password=hunter2")]
    [InlineData("ACCESS_TOKEN=abc")]
    public void A_line_with_a_token_is_dropped(string message) => DiagnosticsFilter.IsSafe(Line(message)).Should().BeFalse();

    [Theory]
    [InlineData("subscribed to webcal://calendars.example.com/team")]
    [InlineData("fetched webcals://calendars.example.com/team")]
    [InlineData("fetched https://calendar.example.com/private/abc123/basic.ics")]
    public void A_feed_link_is_dropped_even_without_a_query(string message) => DiagnosticsFilter.IsSafe(Line(message)).Should().BeFalse();

    [Fact]
    public void A_line_with_a_known_event_title_is_dropped_whatever_the_case()
    {
        string[] titles = ["Design review", "Dentist"];
        DiagnosticsFilter.IsSafe(Line("could not read Design review"), titles).Should().BeFalse();
        DiagnosticsFilter.IsSafe(Line("could not read DESIGN REVIEW"), titles).Should().BeFalse();
        DiagnosticsFilter.IsSafe(Line("event 'dentist' skipped"), titles).Should().BeFalse();
        DiagnosticsFilter.IsSafe(Line("the engine fetched 3 calendars"), titles).Should().BeTrue();
    }

    [Fact]
    public void A_title_matches_whole_words_only()
    {
        DiagnosticsFilter.IsSafe(Line("the dentistry module loaded"), ["Dentist"]).Should().BeTrue();
        DiagnosticsFilter.IsSafe(Line("Dentist."), ["Dentist"]).Should().BeFalse();
    }

    [Fact]
    public void A_title_is_looked_for_in_the_message_not_in_the_category()
    {
        // A meeting called "Sync" must not blank every line of the sync category.
        DiagnosticsFilter.IsSafe(Line("fetched 3 calendars", category: "sync"), ["Sync"]).Should().BeTrue();
        DiagnosticsFilter.IsSafe(Line("Sync"), ["Sync"]).Should().BeFalse();
    }

    [Fact]
    public void A_line_that_does_not_have_the_logs_shape_is_checked_whole()
    {
        DiagnosticsFilter.IsSafe("just some text about Dentist", ["Dentist"]).Should().BeFalse();
        DiagnosticsFilter.IsSafe("just some text", ["Dentist"]).Should().BeTrue();
    }

    [Fact]
    public void Very_short_and_blank_titles_are_ignored_because_they_would_match_everything()
    {
        DiagnosticsFilter.IsSafe(Line("a plain line about the engine"), ["a", "", "  ", "to"]).Should().BeTrue();
    }

    [Fact]
    public void Safe_keeps_the_order_and_drops_only_the_unsafe_lines()
    {
        var lines = new[]
        {
            Line("one"),
            Line("GET https://x.example/a?code=1"),
            Line("two"),
            Line("about Dentist"),
            Line("three"),
        };
        DiagnosticsFilter.Safe(lines, ["Dentist"]).Should().Equal(Line("one"), Line("two"), Line("three"));
    }
}
