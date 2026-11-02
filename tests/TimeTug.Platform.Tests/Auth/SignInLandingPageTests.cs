using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using TimeTug.Platform.Auth;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Tests.Auth;

public partial class SignInLandingPageTests
{
    private sealed class SuffixLocalizer : ILocalizer
    {
        public string GetString(string key) => key + "!";

        public string? TryGetString(string key) => key + "!";
    }

    private static string Visible(string html, string tag)
    {
        var match = Regex.Match(html, $"<{tag}(?: [^>]*)?>(.*?)</{tag}>", RegexOptions.Singleline);
        match.Success.Should().BeTrue($"the page has a <{tag}>");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [Fact]
    public void A_code_alone_is_a_connected_redirect() =>
        SignInLandingPage.Classify("/?code=abc&state=xyz").Should().Be(LandingOutcome.Connected);

    [Fact]
    public void An_error_alone_is_a_sign_in_that_did_not_finish() =>
        SignInLandingPage.Classify("/?error=access_denied&state=xyz").Should().Be(LandingOutcome.DidNotFinish);

    [Fact]
    public void An_error_wins_over_a_code() =>
        SignInLandingPage.Classify("/?code=abc&error=access_denied").Should().Be(LandingOutcome.DidNotFinish);

    [Theory]
    [InlineData("/?code=")]
    [InlineData("/?code=&state=x")]
    [InlineData("/?error=&code=")]
    public void An_empty_code_is_not_a_redirect(string url) =>
        SignInLandingPage.Classify(url).Should().Be(LandingOutcome.NotARedirect);

    [Theory]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("/favicon.ico")]
    [InlineData("/favicon.ico?code=abc")]
    public void A_bare_root_and_the_favicon_are_not_a_redirect(string url) =>
        SignInLandingPage.Classify(url).Should().Be(LandingOutcome.NotARedirect);

    [Fact]
    public void An_unrelated_query_is_not_a_redirect() =>
        SignInLandingPage.Classify("/?state=x").Should().Be(LandingOutcome.NotARedirect);

    [Fact]
    public void An_empty_error_does_not_hide_a_real_code() =>
        SignInLandingPage.Classify("/?error=&code=abc").Should().Be(LandingOutcome.Connected);

    [Fact]
    public void Render_connected_has_the_title_body_lang_main_and_viewport()
    {
        var html = SignInLandingPage.Render(LandingOutcome.Connected, LandingText.English);

        html.Should().StartWith("<!doctype html>");
        html.Should().Contain("<html lang=\"en\"");
        Regex.Matches(html, "<h1[ >]").Should().HaveCount(1);
        Visible(html, "h1").Should().Be("You're connected");
        Visible(html, "p").Should().Be("Return to TimeTug.");
        html.Should().Contain("<main");
        Visible(html, "title").Should().Be("TimeTug");
        html.Should().Contain("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Should().Contain("<meta charset=\"utf-8\">");
    }

    [Fact]
    public void Render_did_not_finish_has_the_failed_strings()
    {
        var html = SignInLandingPage.Render(LandingOutcome.DidNotFinish, LandingText.English);

        Visible(html, "h1").Should().Be("Sign-in didn't finish");
        Visible(html, "p").Should().Be("Return to TimeTug and try again.");
        html.Should().NotContain("You're connected");
    }

    [Fact]
    public void Render_sets_dir_rtl_for_arabic()
    {
        var arabic = LandingText.English with { LanguageTag = "ar", RightToLeft = true };
        var html = SignInLandingPage.Render(LandingOutcome.Connected, arabic);
        html.Should().Contain("<html lang=\"ar\" dir=\"rtl\"");

        SignInLandingPage.Render(LandingOutcome.Connected, LandingText.English).Should().Contain("dir=\"ltr\"");
    }

    [Fact]
    public void Render_encodes_every_string_as_html()
    {
        const string hostile = "<script>alert(1)</script>&\"";
        var text = new LandingText(hostile, hostile, hostile, hostile, hostile, "en\"><script>", false);

        foreach (var outcome in new[] { LandingOutcome.Connected, LandingOutcome.DidNotFinish })
        {
            var html = SignInLandingPage.Render(outcome, text);
            html.Should().NotContain("<script");
            html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;&amp;&quot;");
            html.Should().NotContain("lang=\"en\"><script>");
        }
    }

    [Fact]
    public void Render_refuses_to_describe_a_request_that_is_not_a_redirect()
    {
        var act = () => SignInLandingPage.Render(LandingOutcome.NotARedirect, LandingText.English);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Output_echoes_nothing_from_the_query()
    {
        var url = "/?code=<img src=x onerror=1>&state=abc";
        var outcome = SignInLandingPage.Classify(url);
        outcome.Should().Be(LandingOutcome.Connected);

        var html = SignInLandingPage.Render(outcome, LandingText.English);

        html.Should().NotContain("onerror");
        html.Should().NotContain("abc");
        html.Should().NotContain("<img");
    }

    [Theory]
    [InlineData(LandingOutcome.Connected)]
    [InlineData(LandingOutcome.DidNotFinish)]
    public void Output_has_no_script_no_external_url_and_no_form_or_button(LandingOutcome outcome)
    {
        var html = SignInLandingPage.Render(outcome, LandingText.English);

        foreach (var forbidden in new[] { "<script", "<button", "<form", "<link", "<img", "<iframe", "<object", "<embed", "http-equiv", "url(", "@import", "javascript:" })
        {
            html.Should().NotContainEquivalentOf(forbidden);
        }

        // The only absolute URL allowed is the SVG namespace.
        Regex.Matches(html, @"https?://[^\s""'<>]*").Select(m => m.Value).Should().OnlyContain(u => u == "http://www.w3.org/2000/svg");
    }

    [Fact]
    public void Output_has_light_dark_contrast_and_forced_colors_rules()
    {
        var html = SignInLandingPage.Render(LandingOutcome.Connected, LandingText.English);

        html.Should().Contain("prefers-color-scheme");
        html.Should().Contain("prefers-contrast");
        html.Should().Contain("forced-colors");
        html.Should().Contain("<svg").And.Contain("role=\"img\"").And.Contain("aria-label=\"TimeTug\"");
    }

    [Fact]
    public void Headers_are_exactly_the_four_from_the_spec()
    {
        SignInLandingPage.Headers.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Cache-Control"] = "no-store",
            ["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'",
            ["X-Content-Type-Options"] = "nosniff",
            ["Referrer-Policy"] = "no-referrer",
        });
    }

    [Fact]
    public void The_page_uses_no_feature_the_content_security_policy_forbids()
    {
        // default-src 'none' allows only inline style: no image, font, frame or script source of any kind.
        var html = SignInLandingPage.Render(LandingOutcome.Connected, LandingText.English);
        html.Should().NotContainEquivalentOf("src=");
        html.Should().NotContainEquivalentOf("href=");
    }

    [Fact]
    public void Contrast_of_the_page_colours_is_at_least_four_point_five()
    {
        var html = SignInLandingPage.Render(LandingOutcome.Connected, LandingText.English);
        var blocks = RootBlocks().Matches(html).Select(m => m.Groups[1].Value).ToList();
        blocks.Count.Should().BeGreaterThanOrEqualTo(3, "light, dark and high contrast each define the colours");

        var checkedPairs = 0;
        var defaults = Variables(blocks[0]);
        foreach (var block in blocks)
        {
            var colours = defaults.ToDictionary(p => p.Key, p => p.Value);
            foreach (var (name, value) in Variables(block))
            {
                colours[name] = value;
            }

            if (colours.Values.Any(v => !HexColour().IsMatch(v)))
            {
                continue;   // forced colours use system colours, which the browser guarantees
            }

            Ratio(colours["--fg"], colours["--bg"]).Should().BeGreaterThanOrEqualTo(4.5, "the heading on the page");
            Ratio(colours["--muted"], colours["--bg"]).Should().BeGreaterThanOrEqualTo(4.5, "the paragraph on the page");
            Ratio(colours["--accent"], colours["--bg"]).Should().BeGreaterThanOrEqualTo(3.0, "the mark is a graphical object");
            checkedPairs++;
        }

        checkedPairs.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void English_text_matches_the_spec_strings()
    {
        var english = LandingText.English;
        english.PageTitle.Should().Be("TimeTug");
        english.ConnectedTitle.Should().Be("You're connected");
        english.ConnectedBody.Should().Be("Return to TimeTug.");
        english.FailedTitle.Should().Be("Sign-in didn't finish");
        english.FailedBody.Should().Be("Return to TimeTug and try again.");
        english.LanguageTag.Should().Be("en");
        english.RightToLeft.Should().BeFalse();
    }

    [Fact]
    public void From_localizer_reads_the_five_keys_and_the_culture()
    {
        var text = LandingText.From(new SuffixLocalizer(), new CultureInfo("ar-SA"));

        text.PageTitle.Should().Be("Landing_PageTitle!");
        text.ConnectedTitle.Should().Be("Landing_ConnectedTitle!");
        text.ConnectedBody.Should().Be("Landing_ConnectedBody!");
        text.FailedTitle.Should().Be("Landing_FailedTitle!");
        text.FailedBody.Should().Be("Landing_FailedBody!");
        text.LanguageTag.Should().Be("ar-SA");
        text.RightToLeft.Should().BeTrue();

        var invariant = LandingText.From(new SuffixLocalizer(), CultureInfo.InvariantCulture);
        invariant.LanguageTag.Should().Be("en");
        invariant.RightToLeft.Should().BeFalse();
    }

    // ---- WCAG contrast -----------------------------------------------------------------------------------------

    [GeneratedRegex(@":root\s*\{([^}]*)\}")]
    private static partial Regex RootBlocks();

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();

    private static Dictionary<string, string> Variables(string block) =>
        Regex.Matches(block, @"(--[a-z]+)\s*:\s*([^;]+);?").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());

    private static double Luminance(string hex)
    {
        double Channel(int shift)
        {
            var value = Convert.ToInt32(hex.Substring(1 + shift, 2), 16) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }

    private static double Ratio(string a, string b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }
}
