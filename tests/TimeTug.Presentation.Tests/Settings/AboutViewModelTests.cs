using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

public sealed class AboutViewModelTests
{
    private readonly FakeSystemLauncher _launcher = new();

    private AboutViewModel Model(string version = "1.2.3.4", string? commit = null, bool development = false) =>
        new(version, commit, development, _launcher, new ImmediateDispatcher(), new ReswLocalizer());

    [Fact]
    public void About_shows_the_package_version()
    {
        Model("1.2.3.4").VersionText.Should().Be("Version 1.2.3.4");
        Model("0.1.0.0").VersionText.Should().Be("Version 0.1.0.0");
    }

    [Fact]
    public void About_carries_the_Macs_lines_and_the_Windows_copyright()
    {
        var model = Model();

        model.Name.Should().Be("TimeTug");
        model.LockupAutomationName.Should().Be("TimeTug. A tug when time needs your attention.");
        model.Tagline.Should().Be("Never hyperfocus through another meeting.");
        model.CreditText.Should().Be("Created by Scott O’Bryan");
        model.LicenceNote.Should().Be("Source available under MIT with the Commons Clause.");
        model.CopyrightText.Should().Be("© 2026 Binary Companion LLC");
    }

    [Fact]
    public async Task The_website_and_licence_links_open_the_right_pages()
    {
        var model = Model();

        await model.OpenWebsiteAsync();
        await model.OpenLicenceAsync();

        _launcher.Opened.Should().Equal("https://binarycompanion.com/", "https://github.com/binary-companion/timetug-windows/blob/main/LICENSE");
        model.Error.Should().BeNull();
    }

    [Fact]
    public async Task A_link_that_cannot_be_opened_says_so_and_the_next_one_clears_it()
    {
        var model = Model();
        _launcher.Succeeds = false;

        await model.OpenWebsiteAsync();

        model.Error.Should().Be("Couldn’t open the link.");
        model.HasError.Should().BeTrue();
        _launcher.Succeeds = true;
        await model.OpenLicenceAsync();
        model.Error.Should().BeNull();
    }

    [Fact]
    public void The_update_card_says_updates_come_from_the_Store()
    {
        var model = Model("1.2.3.4");

        model.UpdateTitle.Should().Be("TimeTug 1.2.3.4");
        model.UpdateStatusText.Should().Be("Updates come from the Microsoft Store. Windows installs new versions for you.");
        model.ShowStoreLink.Should().BeTrue();
        model.StoreLinkText.Should().Be("Open Microsoft Store updates");
    }

    [Fact]
    public void A_development_build_shows_its_commit_instead()
    {
        var model = Model("0.1.0.0", commit: "8060ec0a1", development: true);

        model.UpdateTitle.Should().Be("TimeTug 0.1.0.0");
        model.UpdateStatusText.Should().Be("Development build 8060ec0a1. Updates are installed by hand, not from the Store.");
        model.ShowStoreLink.Should().BeFalse("a development package is not in the Store");
    }

    [Fact]
    public void A_development_build_without_a_commit_still_says_it_is_one()
    {
        Model(development: true).UpdateStatusText.Should().Be("Development build. Updates are installed by hand, not from the Store.");
        Model(commit: "8060ec0a1", development: false).UpdateStatusText.Should().NotContain("8060ec0a1", "a Store build never shows a commit");
    }

    [Fact]
    public void The_hub_row_shows_the_version_under_Software_Update_as_the_Mac_does()
    {
        Model("1.2.3.4").HubSubtitle.Should().Be("TimeTug 1.2.3.4");
    }

    [Fact]
    public async Task The_Store_link_opens_the_Store_updates_page()
    {
        var model = Model();

        await model.OpenStoreUpdatesAsync();

        _launcher.Opened.Should().Equal("ms-windows-store://downloadsandupdates");
    }

    [Theory]
    [InlineData("1.0.0+c7e2971c5c9e3109aabbccddeeff00112233aabb", "c7e2971c5")]
    [InlineData("1.0.0+c7e2971c5c9e3109aabbccddeeff00112233aabb-dirty", "c7e2971c5-dirty")]
    [InlineData("1.0.0+8060ec0a1", "8060ec0a1")]
    [InlineData("1.0.0+8060ec0a1-dirty", "8060ec0a1-dirty")]
    [InlineData("1.0.0+ab", "ab")]
    [InlineData("1.0.0", null)]
    [InlineData("1.0.0+", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_commit_is_what_follows_the_plus_in_the_informational_version(string? informational, string? expected)
    {
        BuildCommit.From(informational).Should().Be(expected);
    }

    [Fact]
    public void A_revision_that_is_not_a_hash_is_cut_to_sixteen_characters()
    {
        BuildCommit.From("1.0.0+" + new string('x', 60))!.Length.Should().Be(16);
    }

    [Fact]
    public void The_General_pane_model_owns_launch_at_login_the_recorder_and_About_and_disposes_the_recorder()
    {
        var hotkeys = new FakeHotkeys();
        var text = new ReswLocalizer();
        var recorder = new HotkeyRecorderViewModel(hotkeys, new ImmediateDispatcher(), text);
        var launch = new LaunchAtLoginViewModel(new FakeStartupTask(), _launcher, new ImmediateDispatcher(), text);
        var about = Model();
        var general = new GeneralViewModel(launch, recorder, about);

        general.LaunchAtLogin.Should().BeSameAs(launch);
        general.Recorder.Should().BeSameAs(recorder);
        general.About.Should().BeSameAs(about);
        GeneralViewModel.Ids.LaunchAtLogin.Should().Be("launch-at-login");
        GeneralViewModel.Ids.PopupShortcut.Should().Be("popup-shortcut");

        general.Dispose();
        hotkeys.RaiseStateChanged();
    }
}
