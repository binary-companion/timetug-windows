using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.Tray;

namespace TimeTug.Platform.Tests.Tray;

public class MinuteTickerTests
{
    [Fact]
    public void The_first_tick_lands_on_the_next_whole_minute_then_every_minute_until_disposed()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 20, TimeSpan.Zero));
        var ticks = 0;
        var subscription = new MinuteTicker(time).Start(() => ticks++);

        time.Advance(TimeSpan.FromSeconds(39));
        ticks.Should().Be(0);
        time.Advance(TimeSpan.FromSeconds(1));
        ticks.Should().Be(1);
        time.GetUtcNow().Second.Should().Be(0);
        time.Advance(TimeSpan.FromMinutes(3));
        ticks.Should().Be(4);

        subscription.Dispose();
        time.Advance(TimeSpan.FromMinutes(5));
        ticks.Should().Be(4);
    }

    [Fact]
    public void On_a_whole_minute_the_first_tick_is_a_full_minute_away()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var ticks = 0;
        using var subscription = new MinuteTicker(time).Start(() => ticks++);

        time.Advance(TimeSpan.FromSeconds(59));
        ticks.Should().Be(0);
        time.Advance(TimeSpan.FromSeconds(1));
        ticks.Should().Be(1);
    }
}
