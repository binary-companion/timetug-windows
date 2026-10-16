using System.Diagnostics.Tracing;
using TimeTug.Platform.Diagnostics;

namespace TimeTug.Platform.Tests.Diagnostics;

public class EtwDiagnosticsTests
{
    private sealed class Listener : EventListener
    {
        public List<(string Name, string?[] Payload, EventLevel Level)> Events { get; } = [];

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "BinaryCompanion-TimeTug")
            {
                EnableEvents(source, EventLevel.Verbose);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            lock (Events)
            {
                Events.Add((e.EventName ?? "", [.. e.Payload!.Select(p => p?.ToString())], e.Level));
            }
        }
    }

    [Fact]
    public void The_provider_is_named_for_binary_companion_timetug()
    {
        EtwDiagnostics.Log.Name.Should().Be("BinaryCompanion-TimeTug");
    }

    [Fact]
    public void Entries_arrive_with_the_matching_event_level()
    {
        using var listener = new Listener();
        EtwDiagnostics.Log.Record("error", "credentials", "failed");
        EtwDiagnostics.Log.Record("warning", "http", "slow");
        EtwDiagnostics.Log.Record("info", "engine", "ready");

        lock (listener.Events)
        {
            listener.Events.Should().Contain(e => e.Name == "Error" && e.Level == EventLevel.Error && e.Payload[0] == "credentials" && e.Payload[1] == "failed");
            listener.Events.Should().Contain(e => e.Name == "Warning" && e.Level == EventLevel.Warning);
            listener.Events.Should().Contain(e => e.Name == "Info" && e.Level == EventLevel.Informational);
        }
    }

    [Fact]
    public void The_fan_out_reaches_every_sink_even_when_one_throws()
    {
        var recorder = new RecordingDiagnostics();
        var fan = new FanOutDiagnostics(new Throwing(), recorder);
        fan.Record("info", "c", "m");
        recorder.Entries.Should().ContainSingle();
    }

    private sealed class Throwing : TimeTug.Presentation.Abstractions.IDiagnostics
    {
        public void Record(string level, string category, string message) => throw new IOException("disk full");
    }
}
