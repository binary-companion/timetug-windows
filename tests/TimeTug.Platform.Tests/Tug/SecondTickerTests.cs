using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.Tug;

namespace TimeTug.Platform.Tests.Tug;

public class SecondTickerTests
{
    [Fact]
    public void It_ticks_once_a_second_until_disposed()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var ticks = 0;
        var subscription = new SecondTicker(time).Start(() => ticks++);

        time.Advance(TimeSpan.FromMilliseconds(999));
        ticks.Should().Be(0);
        time.Advance(TimeSpan.FromMilliseconds(1));
        ticks.Should().Be(1);
        time.Advance(TimeSpan.FromSeconds(3));
        ticks.Should().Be(4);

        subscription.Dispose();
        time.Advance(TimeSpan.FromSeconds(5));
        ticks.Should().Be(4);
    }

    [Fact]
    public void The_first_tick_lands_on_the_next_whole_second()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, 250, TimeSpan.Zero));
        var ticks = 0;
        using var subscription = new SecondTicker(time).Start(() => ticks++);

        time.Advance(TimeSpan.FromMilliseconds(749));
        ticks.Should().Be(0);
        time.Advance(TimeSpan.FromMilliseconds(1));
        ticks.Should().Be(1);
        time.GetUtcNow().Millisecond.Should().Be(0);
    }
}
