using TimeTug.Platform.Tray;

namespace TimeTug.Platform.Tests.Tray;

public class TrayIconMetricsTests
{
    [Fact]
    public void The_small_icon_size_is_a_usable_pixel_size()
    {
        // 16 to 32 at 100 to 200 percent scaling; larger on very high scaling. Never zero.
        TrayIconMetrics.SmallIconSize().Should().BeInRange(16, 64);
    }
}
