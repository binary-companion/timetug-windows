using System.Xml.Linq;
using TimeTug.Platform.OsState;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tug;

namespace TimeTug.Platform.Tests.OsState;

/// <summary>Returns the key as the text, so tests see which string was asked for.</summary>
internal sealed class KeyLocalizer : ILocalizer
{
    public string GetString(string key) => key switch
    {
        "Toast_SnoozeMinutes" => "{0} min",
        "Toast_SnoozeSeconds" => "{0} sec",
        "Toast_Details" => "{0}-{1}",
        "Toast_DetailsCalendar" => "{0}-{1} / {2}",
        _ => key,
    };

    public string? TryGetString(string key) => GetString(key);
}

internal sealed class FakeNotifier : IAppNotifier
{
    public bool IsEnabled { get; set; } = true;

    public bool Accept { get; set; } = true;

    public bool ThrowOnShow { get; set; }

    public bool ThrowOnRegister { get; set; }

    public int Registered { get; private set; }

    public List<(string Xml, string Tag, string Group)> Shown { get; } = [];

    public event Action<ToastActivation>? Activated;

    public void Register()
    {
        if (ThrowOnRegister)
        {
            throw new InvalidOperationException("no identity");
        }

        Registered++;
    }

    public bool Show(string xml, string tag, string group)
    {
        if (ThrowOnShow)
        {
            throw System.Runtime.InteropServices.Marshal.GetExceptionForHR(unchecked((int)0x80004005))!;
        }

        Shown.Add((xml, tag, group));
        return Accept;
    }

    public void Activate(string arguments, string? snoozeChoice = null)
    {
        var args = arguments.Split(';').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var input = snoozeChoice is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [ToastXml.SnoozeInputId] = snoozeChoice };
        Activated?.Invoke(new ToastActivation(args, input));
    }

    public void Dispose()
    {
    }
}

internal sealed class RecordingActions : ITugActions
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public ValueTask JoinAsync(string eventId) => Add("join:" + eventId);

    public ValueTask SnoozeAsync(string eventId, TimeSpan duration) => Add("snooze:" + eventId + ":" + (int)duration.TotalSeconds);

    public ValueTask DismissAsync(string eventId) => Add("dismiss:" + eventId);

    private ValueTask Add(string call)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }

        return ValueTask.CompletedTask;
    }
}

public class ToastXmlTests
{
    private static TakeoverRequest Request(string id = "e1", string? url = "https://meet.example.test/x", string? calendar = "Work") => new(
        id,
        "Design review",
        new DateTimeOffset(2026, 10, 16, 14, 30, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.Zero),
        false,
        false,
        url,
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)],
        calendar,
        3);

    [Fact]
    public void The_payload_is_well_formed_and_carries_the_title_the_details_and_the_buttons()
    {
        var xml = XDocument.Parse(ToastXml.Build(Request(), new KeyLocalizer()));

        xml.Root!.Name.LocalName.Should().Be("toast");
        xml.Root.Attribute("scenario")!.Value.Should().Be("reminder");
        var texts = xml.Descendants("text").Select(t => t.Value).ToList();
        texts[0].Should().Be("Design review");
        texts[1].Should().Contain("/ Work");
        xml.Descendants("action").Select(a => a.Attribute("content")!.Value).Should().Equal("Toast_Join", "Toast_Snooze", "Toast_Dismiss");
        xml.Descendants("action").Should().OnlyContain(a => a.Attribute("activationType")!.Value == "background");
    }

    [Fact]
    public void The_snooze_choices_are_the_requests_own_options_and_the_first_is_the_default()
    {
        var xml = XDocument.Parse(ToastXml.Build(Request(), new KeyLocalizer()));

        var input = xml.Descendants("input").Single();
        input.Attribute("id")!.Value.Should().Be(ToastXml.SnoozeInputId);
        input.Attribute("defaultInput")!.Value.Should().Be("60");
        input.Elements("selection").Select(s => (s.Attribute("id")!.Value, s.Attribute("content")!.Value))
            .Should().Equal(("60", "1 min"), ("300", "5 min"), ("600", "10 min"));
    }

    [Fact]
    public void Options_that_are_not_whole_minutes_are_shown_in_seconds()
    {
        var request = Request() with { SnoozeOptions = [TimeSpan.FromSeconds(90)] };
        var xml = XDocument.Parse(ToastXml.Build(request, new KeyLocalizer()));
        xml.Descendants("selection").Single().Attribute("content")!.Value.Should().Be("90 sec");
    }

    [Fact]
    public void Without_a_join_link_there_is_no_join_button_and_without_options_no_snooze()
    {
        var request = Request(url: null) with { SnoozeOptions = [] };
        var xml = XDocument.Parse(ToastXml.Build(request, new KeyLocalizer()));

        xml.Descendants("action").Select(a => a.Attribute("content")!.Value).Should().Equal("Toast_Dismiss");
        xml.Descendants("input").Should().BeEmpty();
    }

    [Fact]
    public void Details_without_a_calendar_title_and_for_an_all_day_event()
    {
        var plain = XDocument.Parse(ToastXml.Build(Request(calendar: null), new KeyLocalizer()));
        plain.Descendants("text").ElementAt(1).Value.Should().NotContain("/");

        var allDay = XDocument.Parse(ToastXml.Build(Request() with { IsAllDay = true }, new KeyLocalizer()));
        allDay.Descendants("text").ElementAt(1).Value.Should().Be("Toast_AllDay");
    }

    [Fact]
    public void A_title_with_markup_is_escaped_not_interpreted()
    {
        var xml = ToastXml.Build(Request() with { Title = "Q&A <b>now</b>" }, new KeyLocalizer());

        XDocument.Parse(xml).Descendants("text").First().Value.Should().Be("Q&A <b>now</b>");
        xml.Should().NotContain("<b>now");
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("a=b;c=d")]
    [InlineData("AAMkAGI2=&x<y>;%41")]
    public void Event_ids_with_separators_survive_the_arguments_round_trip(string id)
    {
        var xml = XDocument.Parse(ToastXml.Build(Request(id), new KeyLocalizer()));
        var button = xml.Descendants("action").First(a => a.Attribute("content")!.Value == "Toast_Join").Attribute("arguments")!.Value;
        var arguments = button.Split(';').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);

        arguments.Keys.Should().BeEquivalentTo("action", "eventId");
        ToastXml.Parse(arguments, new Dictionary<string, string>())!.EventId.Should().Be(id);
    }

    [Fact]
    public void Parse_reads_join_dismiss_and_the_selected_snooze()
    {
        static Dictionary<string, string> Args(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);
        var none = new Dictionary<string, string>();

        ToastXml.Parse(Args(("action", "join"), ("eventId", "e1")), none).Should().Be(new ToastAction("join", "e1", null));
        ToastXml.Parse(Args(("action", "dismiss"), ("eventId", "e1")), none).Should().Be(new ToastAction("dismiss", "e1", null));
        ToastXml.Parse(Args(("action", "snooze"), ("eventId", "e1"), ("seconds", "60")), Args((ToastXml.SnoozeInputId, "300")))
            .Should().Be(new ToastAction("snooze", "e1", TimeSpan.FromSeconds(300)));
        ToastXml.Parse(Args(("action", "snooze"), ("eventId", "e1"), ("seconds", "60")), none)
            .Should().Be(new ToastAction("snooze", "e1", TimeSpan.FromSeconds(60)), "the button carries the default");
    }

    [Fact]
    public void Parse_ignores_a_click_on_the_body_and_anything_malformed()
    {
        var none = new Dictionary<string, string>();
        ToastXml.Parse(new Dictionary<string, string> { ["action"] = "open", ["eventId"] = "e1" }, none).Should().BeNull();
        ToastXml.Parse(none, none).Should().BeNull();
        ToastXml.Parse(new Dictionary<string, string> { ["action"] = "join" }, none).Should().BeNull();
        ToastXml.Parse(new Dictionary<string, string> { ["action"] = "snooze", ["eventId"] = "e1" }, none).Should().BeNull();
        ToastXml.Parse(new Dictionary<string, string> { ["action"] = "snooze", ["eventId"] = "e1" }, new Dictionary<string, string> { [ToastXml.SnoozeInputId] = "-5" }).Should().BeNull();
        ToastXml.Parse(new Dictionary<string, string> { ["action"] = "snooze", ["eventId"] = "e1" }, new Dictionary<string, string> { [ToastXml.SnoozeInputId] = "soon" }).Should().BeNull();
    }
}

public class ToastServiceTests
{
    private static TakeoverRequest Request(string id = "e1") => new(
        id, "Design review", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), false, false, "https://meet.example.test/x",
        [TimeSpan.FromMinutes(5)], null, 0);

    private static (ToastService Service, FakeNotifier Notifier, RecordingActions Actions, RecordingDiagnostics Diagnostics) Create()
    {
        var notifier = new FakeNotifier();
        var actions = new RecordingActions();
        var diagnostics = new RecordingDiagnostics();
        return (new ToastService(() => actions, new KeyLocalizer(), diagnostics, notifier), notifier, actions, diagnostics);
    }

    [Fact]
    public async Task A_toast_that_windows_accepts_is_reported_as_accepted()
    {
        var (service, notifier, _, _) = Create();

        (await service.ShowAsync(Request())).Should().BeTrue();

        var shown = notifier.Shown.Should().ContainSingle().Subject;
        shown.Group.Should().Be("takeover");
        shown.Tag.Length.Should().BeLessThanOrEqualTo(64);
        shown.Xml.Should().Contain("Design review");
    }

    [Fact]
    public async Task The_tag_is_stable_per_request_and_short_even_for_a_long_id()
    {
        var (service, notifier, _, _) = Create();
        var longId = new string('x', 300);

        await service.ShowAsync(Request(longId));
        await service.ShowAsync(Request(longId));
        await service.ShowAsync(Request("other"));

        notifier.Shown[0].Tag.Should().Be(notifier.Shown[1].Tag).And.NotBe(notifier.Shown[2].Tag);
        notifier.Shown[0].Tag.Length.Should().BeLessThanOrEqualTo(64);
    }

    [Fact]
    public async Task A_toast_windows_does_not_accept_is_reported_as_refused()
    {
        var (service, notifier, _, diagnostics) = Create();
        notifier.Accept = false;

        (await service.ShowAsync(Request())).Should().BeFalse();

        diagnostics.Entries.Should().Contain(e => e.Category == "toast");
    }

    [Fact]
    public async Task Notifications_turned_off_are_refused_without_trying_and_say_so()
    {
        var (service, notifier, _, diagnostics) = Create();
        notifier.IsEnabled = false;

        (await service.ShowAsync(Request())).Should().BeFalse();

        notifier.Shown.Should().BeEmpty();
        diagnostics.Entries.Should().Contain(e => e.Message.Contains("turned off", StringComparison.Ordinal));
        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_platform_failure_while_showing_is_a_refusal_and_is_recorded()
    {
        var (service, notifier, _, diagnostics) = Create();
        notifier.ThrowOnShow = true;

        (await service.ShowAsync(Request())).Should().BeFalse();

        diagnostics.Entries.Should().Contain(e => e.Level == "error" && e.Category == "toast");
    }

    [Fact]
    public void Starting_registers_once_and_a_failure_to_register_is_recorded_not_thrown()
    {
        var (service, notifier, _, diagnostics) = Create();
        service.Start();
        notifier.Registered.Should().Be(1);

        notifier.ThrowOnRegister = true;
        service.Start();
        diagnostics.Entries.Should().Contain(e => e.Level == "error" && e.Message.Contains("registered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_buttons_are_routed_to_the_controller_with_the_request_they_were_made_for()
    {
        var (service, notifier, actions, _) = Create();
        await service.ShowAsync(Request("e7"));
        var xml = XDocument.Parse(notifier.Shown[0].Xml);
        string Arguments(string content) => xml.Descendants("action").Single(a => a.Attribute("content")!.Value == content).Attribute("arguments")!.Value;

        notifier.Activate(Arguments("Toast_Join"));
        notifier.Activate(Arguments("Toast_Snooze"), snoozeChoice: "600");
        notifier.Activate(Arguments("Toast_Dismiss"));

        await Wait.UntilAsync(() => actions.Calls.Count == 3, "three routed actions");
        actions.Calls.Should().BeEquivalentTo(["join:e7", "snooze:e7:600", "dismiss:e7"]);
    }

    [Fact]
    public async Task A_click_on_the_toast_body_does_nothing()
    {
        var (service, notifier, actions, _) = Create();
        await service.ShowAsync(Request("e7"));

        notifier.Activate("action=open;eventId=e7");
        await Task.Delay(100);

        actions.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task An_action_handler_that_throws_is_recorded_and_does_not_escape()
    {
        var notifier = new FakeNotifier();
        var diagnostics = new RecordingDiagnostics();
        using var service = new ToastService(() => throw new InvalidOperationException("not ready"), new KeyLocalizer(), diagnostics, notifier);

        notifier.Activate("action=dismiss;eventId=e1");

        await Wait.UntilAsync(() => diagnostics.Entries.Any(e => e.Level == "error"), "the recorded failure");
    }
}
