using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Startup;

public class InstanceNoticeTests
{
    [Fact]
    public void The_collision_notice_has_the_approved_wording()
    {
        var text = new ReswLocalizer();

        text.GetString("Instance_OtherDistribution").Should().Be("TimeTug is already running from a different installation. Close it to use this copy.");
        text.GetString("Instance_NoticeTitle").Should().Be("TimeTug");
    }
}
