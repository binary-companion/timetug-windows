using TimeTug.Platform.OsState;

namespace TimeTug.Platform.Tests.OsState;

public class UrlLauncherTests
{
    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("http://example.test/join")]
    [InlineData("zoommtg://zoom.us/join?confno=123")]
    [InlineData("zoomus://zoom.us/join?confno=123")]
    [InlineData("msteams://teams.microsoft.com/l/meetup-join/x")]
    [InlineData("webex://meet/abc")]
    [InlineData("HTTPS://MEET.EXAMPLE.TEST/UPPER")]
    public async Task Web_links_and_conferencing_schemes_are_opened(string url)
    {
        var opened = new List<Uri>();
        var launcher = new UrlLauncher(launch: uri => { opened.Add(uri); return ValueTask.FromResult(true); });

        (await launcher.LaunchAsync(url)).Should().BeTrue();

        opened.Should().ContainSingle();
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("calculator:")]
    [InlineData("\\\\server\\share\\x.exe")]
    [InlineData("meet.google.com/abc")]
    [InlineData("")]
    public async Task Anything_else_is_refused_and_never_handed_to_windows(string url)
    {
        var called = 0;
        var launcher = new UrlLauncher(launch: _ => { called++; return ValueTask.FromResult(true); });

        (await launcher.LaunchAsync(url)).Should().BeFalse();

        called.Should().Be(0);
    }

    [Fact]
    public async Task A_launch_that_fails_or_throws_returns_false()
    {
        (await new UrlLauncher(launch: _ => ValueTask.FromResult(false)).LaunchAsync("https://a.test/")).Should().BeFalse();
        (await new UrlLauncher(launch: _ => throw new InvalidOperationException("no handler")).LaunchAsync("https://a.test/")).Should().BeFalse();
        (await new UrlLauncher(launch: _ => throw System.Runtime.InteropServices.Marshal.GetExceptionForHR(unchecked((int)0x80004005))!).LaunchAsync("https://a.test/")).Should().BeFalse();
    }

    [Fact]
    public async Task The_allowed_schemes_can_be_narrowed()
    {
        var launcher = new UrlLauncher(new HashSet<string> { "https" }, _ => ValueTask.FromResult(true));
        (await launcher.LaunchAsync("https://a.test/")).Should().BeTrue();
        (await launcher.LaunchAsync("zoommtg://x")).Should().BeFalse();
    }
}
