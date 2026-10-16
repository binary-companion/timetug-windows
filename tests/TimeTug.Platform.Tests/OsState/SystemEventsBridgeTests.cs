using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.OsState;

namespace TimeTug.Platform.Tests.OsState;

public sealed class BridgeRig : IDisposable
{
    public BridgeRig(DateTimeOffset? now = null, TimeZoneInfo? local = null)
    {
        Time.SetLocalTimeZone(local ?? TimeZoneInfo.Utc);
        Time.SetUtcNow(now ?? new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        Bridge = new SystemEventsBridge(Engine, Source, Network, Time, Diagnostics, () => Zone);
    }

    internal RecordingEngineClient Engine { get; } = new();

    internal FakeMessageSource Source { get; } = new();

    internal FakeNetwork Network { get; } = new();

    internal RecordingDiagnostics Diagnostics { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public SystemEventsBridge Bridge { get; }

    /// <summary>What the "current IANA zone" reader returns.</summary>
    public string? Zone { get; set; } = "Europe/London";

    /// <summary>
    /// Sends a marker event through the same ordered queue, waits for it, and returns everything that was sent before
    /// it (earlier markers left out), so a test can assert that nothing else was sent.
    /// </summary>
    public async Task<IReadOnlyList<string>> SettledAsync()
    {
        var before = Engine.Events.Count(e => e == "networkReturned");
        Network.Raise(true);
        await Wait.UntilAsync(() => Engine.Events.Count(e => e == "networkReturned") == before + 1, "the marker event");
        _markers.Add(Engine.Events.Count - 1);
        return Engine.Events.Where((_, index) => !_markers.Contains(index)).ToList();
    }

    private readonly HashSet<int> _markers = [];

    public void Dispose() => Bridge.Dispose();
}

public class SystemEventsBridgeTests
{
    [Fact]
    public async Task Power_resume_sends_wake_once_and_other_power_messages_send_nothing()
    {
        using var rig = new BridgeRig();

        rig.Source.Raise(WindowMessage.PowerBroadcast, 0x4);    // PBT_APMSUSPEND
        rig.Source.Raise(WindowMessage.PowerBroadcast, 0x7);    // PBT_APMRESUMESUSPEND (follows the automatic one)
        rig.Source.Raise(WindowMessage.PowerBroadcast, WindowMessage.PowerResumeAutomatic);

        (await rig.SettledAsync()).Should().Equal("wake");
    }

    [Fact]
    public async Task A_clock_change_sends_clock_changed_and_no_time_zone_event_when_the_zone_is_unchanged()
    {
        using var rig = new BridgeRig();

        rig.Source.Raise(WindowMessage.TimeChange);

        (await rig.SettledAsync()).Should().Equal("clockChanged");
    }

    [Fact]
    public async Task A_clock_change_that_comes_with_a_new_zone_sends_clock_changed_then_time_zone_changed_with_the_iana_id()
    {
        using var rig = new BridgeRig();
        rig.Zone = "Europe/Berlin";

        rig.Source.Raise(WindowMessage.TimeChange);

        (await rig.SettledAsync()).Should().Equal("clockChanged", "timeZoneChanged:Europe/Berlin");
    }

    [Fact]
    public async Task A_settings_change_for_the_time_zone_sends_one_time_zone_changed_and_only_when_the_zone_differs()
    {
        using var rig = new BridgeRig();

        rig.Source.Raise(WindowMessage.SettingChange, text: "TimeZoneInformation");   // same zone: nothing
        rig.Zone = "America/New_York";
        rig.Source.Raise(WindowMessage.SettingChange, text: "TimeZoneInformation");
        rig.Source.Raise(WindowMessage.SettingChange, text: "intl");                   // same new zone: nothing more
        rig.Source.Raise(WindowMessage.SettingChange, text: "WindowMetrics");          // unrelated

        (await rig.SettledAsync()).Should().Equal("timeZoneChanged:America/New_York");
    }

    [Fact]
    public async Task A_zone_that_cannot_be_converted_sends_nothing()
    {
        using var rig = new BridgeRig();
        rig.Zone = null;

        rig.Source.Raise(WindowMessage.SettingChange, text: "TimeZoneInformation");
        rig.Source.Raise(WindowMessage.TimeChange);

        (await rig.SettledAsync()).Should().Equal("clockChanged");
    }

    [Fact]
    public async Task A_display_change_sends_display_changed_once()
    {
        using var rig = new BridgeRig();
        rig.Source.Raise(WindowMessage.DisplayChange);
        (await rig.SettledAsync()).Should().Equal("displayChanged");
    }

    [Fact]
    public async Task Network_access_returning_sends_network_returned_and_losing_it_sends_nothing()
    {
        using var rig = new BridgeRig();
        rig.Network.Raise(false);
        rig.Network.Raise(true);

        await Wait.UntilAsync(() => rig.Engine.Events.Count == 1, "network returned");
        var settled = await rig.SettledAsync();

        settled.Should().Equal("networkReturned");
    }

    [Fact]
    public async Task Session_lock_and_unlock_are_app_local_and_send_nothing_to_the_engine()
    {
        using var rig = new BridgeRig();
        rig.Source.Raise(WindowMessage.SessionChange, WindowMessage.SessionLock);
        rig.Source.Raise(WindowMessage.SessionChange, WindowMessage.SessionUnlock);
        (await rig.SettledAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_day_changes_at_local_midnight_once_and_the_timer_is_rearmed_for_the_next_day()
    {
        using var rig = new BridgeRig(new DateTimeOffset(2026, 10, 16, 23, 59, 0, TimeSpan.Zero));

        rig.Time.Advance(TimeSpan.FromSeconds(59));
        (await rig.SettledAsync()).Should().BeEmpty("one second to go");

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await Wait.UntilAsync(() => rig.Engine.Events.Contains("dayChanged"), "dayChanged");
        rig.Time.Advance(TimeSpan.FromHours(23));
        (await rig.SettledAsync()).Should().Equal("dayChanged");

        rig.Time.Advance(TimeSpan.FromHours(1));
        await Wait.UntilAsync(() => rig.Engine.Events.Count(e => e == "dayChanged") == 2, "the second dayChanged");
        (await rig.SettledAsync()).Should().Equal("dayChanged", "dayChanged");
    }

    [Fact]
    public async Task Midnight_follows_the_local_time_zone_not_utc()
    {
        var plusFive = TimeZoneInfo.CreateCustomTimeZone("plus5", TimeSpan.FromHours(5), "plus5", "plus5");
        using var rig = new BridgeRig(new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.Zero), plusFive);   // 23:00 local

        rig.Time.Advance(TimeSpan.FromMinutes(59));
        (await rig.SettledAsync()).Should().BeEmpty();
        rig.Time.Advance(TimeSpan.FromMinutes(1));

        await Wait.UntilAsync(() => rig.Engine.Events.Contains("dayChanged"), "dayChanged at 00:00 local");
    }

    [Fact]
    public async Task A_clock_or_zone_change_moves_the_midnight_timer()
    {
        var plusFive = TimeZoneInfo.CreateCustomTimeZone("plus5", TimeSpan.FromHours(5), "plus5", "plus5");
        using var rig = new BridgeRig(new DateTimeOffset(2026, 10, 16, 23, 0, 0, TimeSpan.Zero));   // midnight in 1 hour (UTC)

        rig.Time.SetLocalTimeZone(plusFive);                      // now 04:00 local: midnight is 20 hours away
        rig.Source.Raise(WindowMessage.TimeChange);
        rig.Time.Advance(TimeSpan.FromHours(1));
        (await rig.SettledAsync()).Should().Equal("clockChanged");

        rig.Time.Advance(TimeSpan.FromHours(19));
        await Wait.UntilAsync(() => rig.Engine.Events.Contains("dayChanged"), "dayChanged after the move");
    }

    [Fact]
    public async Task Wake_rearms_the_timer_so_midnight_still_announces_the_day_exactly_once()
    {
        using var rig = new BridgeRig(new DateTimeOffset(2026, 10, 16, 23, 0, 0, TimeSpan.Zero));

        rig.Source.Raise(WindowMessage.PowerBroadcast, WindowMessage.PowerResumeAutomatic);
        rig.Time.Advance(TimeSpan.FromHours(1));
        await Wait.UntilAsync(() => rig.Engine.Events.Contains("dayChanged"), "dayChanged");

        (await rig.SettledAsync()).Should().Equal("wake", "dayChanged");
    }

    [Fact]
    public async Task An_engine_that_rejects_an_event_is_recorded_and_later_events_still_go_out()
    {
        using var rig = new BridgeRig();
        rig.Engine.Fail = true;
        rig.Source.Raise(WindowMessage.DisplayChange);
        await Wait.UntilAsync(() => rig.Diagnostics.Entries.Count == 1, "the recorded failure");
        rig.Diagnostics.Entries[0].Message.Should().Contain("displayChanged").And.NotContain("engine gone");

        rig.Engine.Fail = false;
        rig.Source.Raise(WindowMessage.TimeChange);

        (await rig.SettledAsync()).Should().Equal("clockChanged");
    }

    [Fact]
    public void Disposing_stops_listening_to_every_source()
    {
        var rig = new BridgeRig();
        rig.Source.Subscribers.Should().Be(1);

        rig.Dispose();
        rig.Source.Raise(WindowMessage.DisplayChange);

        rig.Source.Subscribers.Should().Be(0);
        rig.Engine.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Pacific Standard Time", "America/Los_Angeles")]
    [InlineData("W. Europe Standard Time", "Europe/Berlin")]
    [InlineData("UTC", "Etc/UTC")]
    [InlineData("Europe/Berlin", "Europe/Berlin")]
    [InlineData("Not A Real Zone", null)]
    public void Windows_zone_ids_convert_to_iana_ids(string windowsId, string? expected)
    {
        SystemEventsBridge.ToIana(windowsId).Should().Be(expected);
    }
}
