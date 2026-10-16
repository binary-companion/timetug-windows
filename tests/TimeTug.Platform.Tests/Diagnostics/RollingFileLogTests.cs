using TimeTug.Platform.Diagnostics;

namespace TimeTug.Platform.Tests.Diagnostics;

public sealed class RollingFileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timetug-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void An_entry_has_a_timestamp_level_category_and_message_on_one_line()
    {
        var log = new RollingFileLog(_dir, time: new FixedTime(new DateTimeOffset(2026, 10, 16, 12, 30, 5, 123, TimeSpan.Zero)));
        log.Record("warning", "sync", "first line\nsecond line");
        File.ReadAllLines(log.CurrentPath).Should().Equal("2026-10-16T12:30:05.123Z [warning] sync: first line second line");
    }

    [Fact]
    public void The_log_rolls_at_the_size_limit_and_never_keeps_more_than_five_files()
    {
        var log = new RollingFileLog(_dir, maxBytes: 1000);
        for (var i = 0; i < 400; i++)
        {
            log.Record("info", "test", $"entry {i} " + new string('x', 40));
        }

        var files = Directory.GetFiles(_dir);
        files.Length.Should().Be(5);
        files.Select(Path.GetFileName).Should().BeEquivalentTo("timetug.log", "timetug.1.log", "timetug.2.log", "timetug.3.log", "timetug.4.log");
        files.Should().OnlyContain(f => new FileInfo(f).Length <= 1000);
        File.ReadAllText(log.CurrentPath).Should().Contain("entry 399");
        File.ReadAllText(Path.Combine(_dir, "timetug.1.log")).Should().Contain("entry ");
    }

    [Fact]
    public void The_newest_entries_are_in_the_lowest_numbered_file()
    {
        var log = new RollingFileLog(_dir, maxBytes: 200);
        for (var i = 0; i < 12; i++)
        {
            log.Record("info", "t", $"entry-{i:D2}-" + new string('y', 30));
        }

        var current = File.ReadAllText(log.CurrentPath);
        var first = File.ReadAllText(Path.Combine(_dir, "timetug.1.log"));
        var currentIndex = int.Parse(current.Split("entry-")[^1][..2], System.Globalization.CultureInfo.InvariantCulture);
        var firstIndex = int.Parse(first.Split("entry-")[^1][..2], System.Globalization.CultureInfo.InvariantCulture);
        currentIndex.Should().BeGreaterThan(firstIndex);
    }

    [Fact]
    public void A_very_long_message_is_cut()
    {
        var log = new RollingFileLog(_dir);
        log.Record("info", "t", new string('z', 10_000));
        new FileInfo(log.CurrentPath).Length.Should().BeLessThan(2200);
    }

    [Fact]
    public void Writing_to_an_unusable_directory_does_not_throw()
    {
        var blocker = Path.Combine(_dir, "file");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocker, "x");
        var log = new RollingFileLog(Path.Combine(blocker, "logs"));   // a directory under a file
        log.Invoking(l => l.Record("error", "t", "m")).Should().NotThrow();
    }

    [Fact]
    public void Concurrent_writers_never_lose_the_limit()
    {
        var log = new RollingFileLog(_dir, maxBytes: 2000);
        Parallel.For(0, 500, i => log.Record("info", "t", $"entry {i} " + new string('q', 30)));
        Directory.GetFiles(_dir).Length.Should().BeLessThanOrEqualTo(5);
        Directory.GetFiles(_dir).Should().OnlyContain(f => new FileInfo(f).Length <= 2000);
    }

    [Fact]
    public void The_default_directory_is_under_a_logs_folder()
    {
        RollingFileLog.DefaultDirectory().Should().EndWith("logs");
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
