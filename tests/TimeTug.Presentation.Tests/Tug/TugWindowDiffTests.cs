using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Tug;

namespace TimeTug.Presentation.Tests.Tug;

public class TugWindowDiffTests
{
    private static readonly MonitorInfo M1 = new("m1", 0, 0, 1920, 1080, true);
    private static readonly MonitorInfo M2 = new("m2", 1920, 0, 1920, 1080, false);

    private static TugMonitorPlan Primary(MonitorInfo monitor) => new(monitor with { IsPrimary = true }, TugMonitorRole.Primary);

    private static TugMonitorPlan Mirror(MonitorInfo monitor) => new(monitor with { IsPrimary = false }, TugMonitorRole.Mirror);

    private static TugPlan Plan(string eventId, params TugMonitorPlan[] monitors) => new(
        TugMode.FullScreen, Requests.Meeting(eventId), monitors, false, null, null);

    [Fact]
    public void From_nothing_every_monitor_gets_a_window_with_the_primary_last()
    {
        var changes = TugWindowDiff.Compute(null, [], Plan("e1", Primary(M1), Mirror(M2)));

        changes.Close.Should().BeEmpty();
        changes.Open.Select(w => w.Monitor.Id).Should().Equal("m2", "m1");
        changes.Open[^1].Role.Should().Be(TugMonitorRole.Primary);
    }

    [Fact]
    public void An_unchanged_plan_changes_nothing()
    {
        var open = new[] { Primary(M1), Mirror(M2) };

        TugWindowDiff.Compute("e1", open, Plan("e1", Primary(M1), Mirror(M2))).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void A_monitor_unplugged_while_the_tug_is_up_closes_only_that_window()
    {
        var open = new[] { Primary(M1), Mirror(M2) };

        var changes = TugWindowDiff.Compute("e1", open, Plan("e1", Primary(M1)));

        changes.Close.Select(w => w.Monitor.Id).Should().Equal("m2");
        changes.Open.Should().BeEmpty();
    }

    [Fact]
    public void When_the_primary_monitor_goes_the_controls_move_to_the_new_primary()
    {
        var open = new[] { Primary(M1), Mirror(M2) };

        // m1 unplugged: m2 becomes the primary.
        var changes = TugWindowDiff.Compute("e1", open, Plan("e1", Primary(M2)));

        changes.Close.Select(w => w.Monitor.Id).Should().BeEquivalentTo(["m1", "m2"]);
        changes.Open.Should().ContainSingle().Which.Should().Be(Primary(M2));
    }

    [Fact]
    public void A_monitor_added_gets_a_mirror_and_the_others_stay()
    {
        var changes = TugWindowDiff.Compute("e1", [Primary(M1)], Plan("e1", Primary(M1), Mirror(M2)));

        changes.Close.Should().BeEmpty();
        changes.Open.Should().ContainSingle().Which.Should().Be(Mirror(M2));
    }

    [Fact]
    public void A_resolution_change_rebuilds_that_window_to_the_new_size()
    {
        var smaller = M2 with { Width = 1280, Height = 720 };

        var changes = TugWindowDiff.Compute("e1", [Primary(M1), Mirror(M2)], Plan("e1", Primary(M1), Mirror(smaller)));

        changes.Close.Select(w => w.Monitor.Id).Should().Equal("m2");
        changes.Open.Should().ContainSingle().Which.Monitor.Width.Should().Be(1280);
    }

    [Fact]
    public void A_different_request_rebuilds_everything()
    {
        var open = new[] { Primary(M1), Mirror(M2) };

        var changes = TugWindowDiff.Compute("e1", open, Plan("e2", Primary(M1), Mirror(M2)));

        changes.Close.Should().HaveCount(2);
        changes.Open.Should().HaveCount(2);
    }

    [Fact]
    public void A_plan_with_nothing_to_draw_closes_everything()
    {
        var open = new[] { Primary(M1), Mirror(M2) };

        TugWindowDiff.Compute("e1", open, TugPlan.None).Close.Should().HaveCount(2);
        TugWindowDiff.Compute("e1", open, TugPlan.ForToast(Requests.Meeting())).Close.Should().HaveCount(2);
        TugWindowDiff.Compute("e1", open, Plan("e1")).Close.Should().HaveCount(2);
    }
}
