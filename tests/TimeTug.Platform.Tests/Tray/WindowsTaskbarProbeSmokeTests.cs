using TimeTug.Platform.OsState;
using TimeTug.Platform.Tests.OsState;
using TimeTug.Platform.Tray;
using Xunit.Abstractions;

namespace TimeTug.Platform.Tests.Tray;

/// <summary>The real taskbar, monitors and UI Automation. They run only with <c>TIMETUG_OS_SMOKE=1</c> in an interactive user session (<c>prlctl exec --current-user</c> on the VM).</summary>
public sealed class WindowsTaskbarProbeSmokeTests(ITestOutputHelper output)
{
    [OsSmokeFact]
    public void Every_monitor_has_a_work_area_inside_it_and_a_scale()
    {
        var monitors = MonitorService.Enumerate();

        monitors.Should().NotBeEmpty();
        foreach (var monitor in monitors)
        {
            output.WriteLine($"{monitor.Id}: bounds {monitor.Bounds}, work {monitor.WorkArea}, dpi {monitor.Dpi}");
            monitor.WorkArea.Should().NotBeNull();
            monitor.Work.X.Should().BeGreaterThanOrEqualTo(monitor.X);
            monitor.Work.Y.Should().BeGreaterThanOrEqualTo(monitor.Y);
            monitor.Work.Right.Should().BeLessThanOrEqualTo(monitor.Bounds.Right);
            monitor.Work.Bottom.Should().BeLessThanOrEqualTo(monitor.Bounds.Bottom);
            monitor.Dpi.Should().BeGreaterThanOrEqualTo(96);
        }
    }

    [OsSmokeFact]
    public void The_notification_area_is_found_on_a_monitor()
    {
        var area = new WindowsTaskbarProbe().NotificationAreaRect();

        output.WriteLine($"notification area: {area}");
        area.Should().NotBeNull("the taskbar exists even when it hides itself");
        area!.Value.IsEmpty.Should().BeFalse();
        MonitorService.Enumerate().Should().Contain(m => m.Bounds.Contains(area.Value.CenterX, area.Value.CenterY));
    }

    [OsSmokeFact]
    public void The_overflow_button_is_inside_the_notification_area()
    {
        var probe = new WindowsTaskbarProbe();

        var chevron = probe.OverflowButtonRect();
        var area = probe.NotificationAreaRect();

        output.WriteLine($"overflow button {chevron}, notification area {area}");
        chevron.Should().NotBeNull("the VM has icons in the overflow, so Windows shows the button");
        area!.Value.Contains(chevron!.Value.CenterX, chevron.Value.CenterY).Should().BeTrue();
    }

    [OsSmokeFact]
    public void An_icon_that_does_not_exist_has_no_rectangle()
    {
        new WindowsTaskbarProbe().IconRect(Guid.NewGuid()).Should().BeNull();
    }

    [OsSmokeFact]
    public void The_taskbar_can_be_read_through_ui_automation()
    {
        var text = new WindowsTaskbarProbe().Describe();

        output.WriteLine(text);
        text.Should().NotContain("did not answer");
        text.Trim().Should().NotBeEmpty("the taskbar has buttons");
    }
}
