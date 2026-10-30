using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.Chip;

namespace TimeTug.Platform.Tests.Chip;

public class PollTickerTests
{
    [Fact]
    public void It_ticks_every_interval_until_disposed()
    {
        var time = new FakeTimeProvider();
        var ticks = 0;
        var subscription = new PollTicker(time).Start(TimeSpan.FromSeconds(2), () => ticks++);

        time.Advance(TimeSpan.FromSeconds(1));
        ticks.Should().Be(0);
        time.Advance(TimeSpan.FromSeconds(1));
        ticks.Should().Be(1);
        time.Advance(TimeSpan.FromSeconds(4));
        ticks.Should().Be(3);

        subscription.Dispose();
        time.Advance(TimeSpan.FromSeconds(10));
        ticks.Should().Be(3);
    }

    [Fact]
    public void An_interval_of_nothing_is_refused()
    {
        var act = () => new PollTicker(new FakeTimeProvider()).Start(TimeSpan.Zero, () => { });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
