using System.Xml.Linq;
using TimeTug.Platform.Auth;

namespace TimeTug.Platform.Tests.Auth;

public class LandingStringsTests
{
    private static readonly string[] Keys = ["Landing_PageTitle", "Landing_ConnectedTitle", "Landing_ConnectedBody", "Landing_FailedTitle", "Landing_FailedBody"];

    private static Dictionary<string, (string Value, string? Comment)> Resw()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TimeTug.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root is above the test binaries");
        var path = Path.Combine(directory!.FullName, "src", "TimeTug.App", "Strings", "en-US", "Resources.resw");
        return XDocument.Load(path).Descendants("data").ToDictionary(
            d => (string)d.Attribute("name")!, d => ((string)d.Element("value")!, (string?)d.Element("comment")), StringComparer.Ordinal);
    }

    [Fact]
    public void Every_landing_key_exists_in_resw_with_a_translator_comment()
    {
        var strings = Resw();
        foreach (var key in Keys)
        {
            strings.Should().ContainKey(key);
            strings[key].Value.Should().NotBeNullOrWhiteSpace(key);
            strings[key].Comment.Should().NotBeNullOrWhiteSpace($"{key} needs a translator note");
        }
    }

    [Fact]
    public void The_english_fallback_matches_the_resw_text()
    {
        var strings = Resw();
        var english = LandingText.English;
        strings["Landing_PageTitle"].Value.Should().Be(english.PageTitle);
        strings["Landing_ConnectedTitle"].Value.Should().Be(english.ConnectedTitle);
        strings["Landing_ConnectedBody"].Value.Should().Be(english.ConnectedBody);
        strings["Landing_FailedTitle"].Value.Should().Be(english.FailedTitle);
        strings["Landing_FailedBody"].Value.Should().Be(english.FailedBody);
    }
}
