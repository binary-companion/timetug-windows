using TimeTug.Platform.Tray;
using TimeTug.Presentation.Tray;

namespace TimeTug.Platform.Tests.Tray;

public class TrayIconControllerTests
{
    private sealed class RecordingSink : ITrayIconSink
    {
        public List<TrayIconImage> Icons { get; } = [];

        public List<string> Tooltips { get; } = [];

        public void SetIcon(TrayIconImage image) => Icons.Add(image);

        public void SetTooltip(string text) => Tooltips.Add(text);
    }

    private sealed class ThrowingIcons(ITrayBaseIcons inner) : ITrayBaseIcons
    {
        public bool Fail { get; set; }

        public TrayIconImage Load(string variant, int sizePx) => Fail ? throw new IOException("cannot read " + variant) : inner.Load(variant, sizePx);
    }

    private static (TrayIconController Controller, RecordingSink Sink, RecordingDiagnostics Diagnostics, Func<int> SizeBox, int[] Size) Make(ITrayBaseIcons? icons = null)
    {
        var sink = new RecordingSink();
        var diagnostics = new RecordingDiagnostics();
        var size = new[] { 16 };
        var controller = new TrayIconController(new TrayIconRenderer(icons ?? TrayTestAssets.Icons()), sink, () => size[0], diagnostics);
        return (controller, sink, diagnostics, () => size[0], size);
    }

    [Fact]
    public void The_first_plan_sets_the_icon_and_the_tooltip()
    {
        var (controller, sink, _, _, _) = Make();
        controller.Apply(new TrayIconView(TrayIconState.Idle, false), "TimeTug");
        sink.Icons.Should().HaveCount(1);
        sink.Tooltips.Should().Equal("TimeTug");
        controller.Icon.Should().BeSameAs(sink.Icons[0]);
        controller.Tooltip.Should().Be("TimeTug");
    }

    [Fact]
    public void The_same_plan_again_changes_nothing()
    {
        var (controller, sink, _, _, _) = Make();
        var view = new TrayIconView(TrayIconState.Soon, false, 5);
        controller.Apply(view, "Design review in 5 min");
        controller.Apply(view, "Design review in 5 min");
        controller.Apply(view with { }, "Design review in 5 min");
        sink.Icons.Should().HaveCount(1);
        sink.Tooltips.Should().HaveCount(1);
    }

    [Fact]
    public void A_new_digit_swaps_the_icon_and_the_tooltip_follows()
    {
        var (controller, sink, _, _, _) = Make();
        controller.Apply(new TrayIconView(TrayIconState.Soon, false, 9), "Design review in 9 min");
        controller.Apply(new TrayIconView(TrayIconState.Soon, false, 8), "Design review in 8 min");
        sink.Icons.Should().HaveCount(2);
        sink.Icons[0].HasSamePixels(sink.Icons[1]).Should().BeFalse();
        sink.Tooltips.Should().Equal("Design review in 9 min", "Design review in 8 min");
    }

    [Fact]
    public void A_tooltip_alone_changing_leaves_the_icon_alone_and_the_reverse()
    {
        var (controller, sink, _, _, _) = Make();
        controller.Apply(new TrayIconView(TrayIconState.Idle, false), "Design review in 2 hr");
        controller.Apply(new TrayIconView(TrayIconState.Idle, false), "Design review in 1 hr 59 min");
        sink.Icons.Should().HaveCount(1);
        sink.Tooltips.Should().HaveCount(2);

        controller.Apply(new TrayIconView(TrayIconState.Attention, false), "Design review in 1 hr 59 min");
        sink.Icons.Should().HaveCount(2);
        sink.Tooltips.Should().HaveCount(2);
    }

    [Fact]
    public void The_icon_follows_the_taskbar_theme()
    {
        var (controller, sink, _, _, _) = Make();
        controller.Apply(new TrayIconView(TrayIconState.Idle, false), "TimeTug");
        controller.Apply(new TrayIconView(TrayIconState.Idle, true), "TimeTug");
        sink.Icons.Should().HaveCount(2);
        sink.Icons[0].HasSamePixels(sink.Icons[1]).Should().BeFalse("a light taskbar gets the dark-ink puppy");
        sink.Tooltips.Should().HaveCount(1);
    }

    [Fact]
    public void A_change_of_the_icon_size_draws_the_icon_again_at_the_nearest_supported_size()
    {
        var (controller, sink, _, _, size) = Make();
        var view = new TrayIconView(TrayIconState.Soon, false, 3);
        controller.Apply(view, "x");
        size[0] = 24;
        controller.Apply(view, "x");
        size[0] = 27; // an unusual scale: the nearest picture (24) is already showing, so nothing is set again
        controller.Apply(view, "x");
        sink.Icons.Select(i => i.Width).Should().Equal(16, 24);
        size[0] = 48;
        controller.Apply(view, "x");
        sink.Icons.Select(i => i.Width).Should().Equal(16, 24, 32);
    }

    [Fact]
    public void A_picture_that_cannot_be_drawn_is_recorded_keeps_the_old_icon_and_is_tried_again_next_time()
    {
        var icons = new ThrowingIcons(TrayTestAssets.Icons());
        var (controller, sink, diagnostics, _, _) = Make(icons);
        controller.Apply(new TrayIconView(TrayIconState.Idle, false), "TimeTug");
        icons.Fail = true;
        var act = () => controller.Apply(new TrayIconView(TrayIconState.Soon, false, 4), "Design review in 4 min");
        act.Should().NotThrow();
        diagnostics.Entries.Should().ContainSingle(e => e.Level == "error" && e.Category == "tray");
        sink.Icons.Should().HaveCount(1, "the previous icon stays");
        sink.Tooltips.Should().Equal("TimeTug", "Design review in 4 min");

        icons.Fail = false;
        controller.Apply(new TrayIconView(TrayIconState.Soon, false, 4), "Design review in 4 min");
        sink.Icons.Should().HaveCount(2);
        controller.Icon!.HasSamePixels(TrayTestAssets.Renderer().Render(TrayIconState.Soon, 4, false, 16)).Should().BeTrue();
    }

    [Fact]
    public void The_failure_message_names_neither_a_meeting_nor_a_path()
    {
        var icons = new ThrowingIcons(TrayTestAssets.Icons()) { Fail = true };
        var (controller, _, diagnostics, _, _) = Make(icons);
        controller.Apply(new TrayIconView(TrayIconState.Soon, false, 4), "Quarterly secrets review in 4 min");
        diagnostics.Entries.Should().ContainSingle();
        diagnostics.Entries[0].Message.Should().NotContain("secrets").And.NotContain("Quarterly");
    }

    [Fact]
    public void Walking_a_meeting_from_far_off_to_the_tug_gives_one_icon_per_change()
    {
        var (controller, sink, _, _, _) = Make();
        var steps = new (TrayIconView View, string Tooltip)[]
        {
            (new TrayIconView(TrayIconState.Idle, false), "Design review in 25 min"),
            (new TrayIconView(TrayIconState.Idle, false), "Design review in 11 min"),
            (new TrayIconView(TrayIconState.Soon, false), "Design review in 10 min"),
            (new TrayIconView(TrayIconState.Soon, false, 9), "Design review in 9 min"),
            (new TrayIconView(TrayIconState.Soon, false, 5), "Design review in 5 min"),
            (new TrayIconView(TrayIconState.Soon, false, 1), "Design review in 1 min"),
            (new TrayIconView(TrayIconState.Attention, false), "TimeTug: a meeting needs you"),
        };
        foreach (var (view, tooltip) in steps)
        {
            controller.Apply(view, tooltip);
        }

        sink.Tooltips.Should().HaveCount(7);
        // idle, (idle again: no new icon), soon without digit, 9, 5, 1, attention
        sink.Icons.Should().HaveCount(6);
    }
}
