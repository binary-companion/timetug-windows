using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Diagnostics;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Diagnostics;

public class DiagnosticsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 30, 0, TimeSpan.Zero);

    private sealed class FakeReader : IDiagnosticsReader
    {
        public List<string> Lines { get; } = [];

        public int? AskedFor { get; private set; }

        public IReadOnlyCollection<string> AskedTitles { get; private set; } = [];

        public int Cleared { get; private set; }

        public bool ClearFails { get; set; }

        public IReadOnlyList<string> ReadLastLines(int maxLines, IReadOnlyCollection<string> eventTitles)
        {
            AskedFor = maxLines;
            AskedTitles = eventTitles;
            return Lines.Count <= maxLines ? Lines : Lines.Skip(Lines.Count - maxLines).ToList();
        }

        public bool Clear()
        {
            Cleared++;
            return !ClearFails;
        }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public List<string> Texts { get; } = [];

        public bool Fails { get; set; }

        public bool TrySetText(string text)
        {
            if (Fails)
            {
                return false;
            }

            Texts.Add(text);
            return true;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (FakeEngineClient Engine, FakeReader Reader, FakeClipboard Clipboard, DiagnosticsViewModel Model) Create()
    {
        var engine = new FakeEngineClient();
        var reader = new FakeReader();
        var clipboard = new FakeClipboard();
        var model = new DiagnosticsViewModel(engine, reader, clipboard, new ImmediateDispatcher(), new ReswLocalizer(), new FixedTime(Now), "1.2.3.0", "Windows 11 10.0.26100");
        return (engine, reader, clipboard, model);
    }

    [Fact]
    public void The_words_are_the_Macs()
    {
        var (_, _, _, model) = Create();
        model.Title.Should().Be("Diagnostics");
        model.CopyText.Should().Be("Copy Diagnostics");
        model.ClearText.Should().Be("Clear");
        model.ExplanationText.Should().Be("A short log of recent calendar activity to share when reporting a problem. It contains no feed links or event titles.");
    }

    [Fact]
    public void Copy_puts_a_header_and_the_last_lines_on_the_clipboard()
    {
        var (_, reader, clipboard, model) = Create();
        reader.Lines.AddRange(Enumerable.Range(1, 700).Select(i => $"2026-10-21T08:00:00.000Z [info] sync: line {i}"));

        model.Copy().Should().BeTrue();

        reader.AskedFor.Should().Be(500);
        var text = clipboard.Texts.Should().ContainSingle().Subject;
        var lines = text.Split(Environment.NewLine);
        lines[..4].Should().Equal("TimeTug diagnostics", "App version: 1.2.3.0", "Windows: Windows 11 10.0.26100", "Generated: 2026-10-21T09:30:00Z");
        lines[4].Should().BeEmpty();
        lines[5].Should().EndWith("line 201");
        lines[^1].Should().EndWith("line 700");
        lines.Length.Should().Be(5 + 500);
        model.StatusText.Should().Be("Copied to the clipboard.");
    }

    [Fact]
    public void Copy_with_an_empty_log_still_copies_the_header()
    {
        var (_, _, clipboard, model) = Create();
        model.Copy().Should().BeTrue();
        clipboard.Texts.Single().Should().StartWith("TimeTug diagnostics").And.NotContain("[info]");
    }

    [Fact]
    public void A_clipboard_that_refuses_says_so_and_copies_nothing()
    {
        var (_, _, clipboard, model) = Create();
        clipboard.Fails = true;

        model.Copy().Should().BeFalse();

        clipboard.Texts.Should().BeEmpty();
        model.StatusText.Should().Be("Couldn’t copy to the clipboard.");
    }

    [Fact]
    public void The_event_and_calendar_titles_the_engine_knows_are_handed_to_the_reader_so_it_can_drop_lines_with_them()
    {
        var (engine, reader, _, model) = Create();
        engine.Push(States.Empty() with
        {
            Events = [new EventSummary("e1", "Design review", Now, Now.AddHours(1), false, true)],
            NextMeeting = new NextMeetingInfo("Dentist", Now, false),
            Agenda = [new AgendaItem("e2", "Lunch with Sam", Now, Now.AddHours(1), false, AgendaPhase.Upcoming, "Home", null, null, 0, false, null)],
            CalendarGroups =
            [
                new CalendarGroup("me@example.com", "google-1", "google", CalendarOrigin.Account, null, false,
                    [new CalendarEntry("google-1/a", "Café Sync", null, true, false, "standard", false, false)]),
            ],
            SystemCalendars = [new CalendarEntry("google-1/h", "Holidays", null, true, false, "subscribed", true, true)],
        });

        model.Copy();

        reader.AskedTitles.Should().BeEquivalentTo("Design review", "Dentist", "Lunch with Sam", "Café Sync", "Holidays", "Home");
    }

    [Fact]
    public void Clear_empties_the_log_and_says_so()
    {
        var (_, reader, _, model) = Create();

        model.Clear().Should().BeTrue();

        reader.Cleared.Should().Be(1);
        model.StatusText.Should().Be("The log was cleared.");
    }

    [Fact]
    public void A_log_that_cannot_be_cleared_says_so()
    {
        var (_, reader, _, model) = Create();
        reader.ClearFails = true;

        model.Clear().Should().BeFalse();

        model.StatusText.Should().Be("Couldn’t clear the log.");
    }

    [Fact]
    public void The_status_is_forgotten_by_the_next_action_and_when_asked()
    {
        var (_, _, _, model) = Create();
        model.Copy();
        model.StatusText.Should().NotBeNull();
        model.Clear();
        model.StatusText.Should().Be("The log was cleared.");
        model.ClearStatus();
        model.StatusText.Should().BeNull();
    }
}
