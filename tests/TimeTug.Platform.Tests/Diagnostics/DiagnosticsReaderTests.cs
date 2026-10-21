using TimeTug.Platform.Diagnostics;

namespace TimeTug.Platform.Tests.Diagnostics;

public sealed class DiagnosticsReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timetug-reader-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static string Line(int i, string message = "fetched") => $"2026-10-21T08:{i / 60 % 60:D2}:{i % 60:D2}.000Z [info] sync: {message} {i}";

    [Fact]
    public void A_folder_that_does_not_exist_reads_as_an_empty_log_and_clears_without_error()
    {
        var reader = new DiagnosticsReader(_dir);
        reader.ReadLastLines(500, []).Should().BeEmpty();
        reader.Clear().Should().BeTrue();
    }

    [Fact]
    public void It_returns_the_last_lines_oldest_first_across_the_rolled_files()
    {
        // The rolling log's own writer, rolled by a small size limit: the newest entries are in timetug.log, older ones in timetug.1.log and so on.
        var log = new RollingFileLog(_dir, maxBytes: 600);
        for (var i = 1; i <= 60; i++)
        {
            log.Record("info", "sync", $"entry {i}");
        }

        Directory.GetFiles(_dir).Length.Should().BeGreaterThan(1);
        var lines = new DiagnosticsReader(_dir).ReadLastLines(25, []);

        lines.Should().HaveCount(25);
        lines[0].Should().EndWith("entry 36");
        lines[^1].Should().EndWith("entry 60");
        lines.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Fewer_lines_than_asked_for_returns_them_all()
    {
        var log = new RollingFileLog(_dir);
        log.Record("info", "sync", "one");
        log.Record("info", "sync", "two");
        new DiagnosticsReader(_dir).ReadLastLines(500, []).Should().HaveCount(2);
    }

    [Fact]
    public void A_line_with_a_query_string_a_token_or_an_event_title_never_comes_back_and_does_not_count_towards_the_limit()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "timetug.log");
        File.WriteAllLines(path,
        [
            Line(1),
            Line(2, "GET https://accounts.example.com/auth?code=abc&x=1"),
            Line(3, "refresh_token=1//0gAbCdEfGhIjKlMnOpQrStUv"),
            Line(4, "could not read Design review"),
            Line(5),
            Line(6, "webcal://feeds.example.com/team"),
            Line(7),
        ]);

        var lines = new DiagnosticsReader(_dir).ReadLastLines(3, ["Design review"]);

        lines.Should().Equal(Line(1), Line(5), Line(7));
        string.Join('\n', lines).Should().NotContain("code=").And.NotContain("refresh_token").And.NotContain("Design review").And.NotContain("webcal");
    }

    [Fact]
    public void The_log_can_be_read_while_the_app_is_writing_it()
    {
        var log = new RollingFileLog(_dir);
        log.Record("info", "sync", "first");
        using var writer = new FileStream(log.CurrentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        new DiagnosticsReader(_dir).ReadLastLines(10, []).Should().ContainSingle();
    }

    [Fact]
    public void Files_that_are_not_the_log_are_left_alone()
    {
        var log = new RollingFileLog(_dir);
        log.Record("info", "sync", "first");
        var other = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(other, "mine");

        var reader = new DiagnosticsReader(_dir);
        reader.ReadLastLines(10, []).Should().ContainSingle();
        reader.Clear().Should().BeTrue();

        File.Exists(other).Should().BeTrue();
    }

    [Fact]
    public void Clear_empties_every_log_file_and_the_app_can_keep_logging()
    {
        var log = new RollingFileLog(_dir, maxBytes: 600);
        for (var i = 1; i <= 60; i++)
        {
            log.Record("info", "sync", $"entry {i}");
        }

        var reader = new DiagnosticsReader(_dir);
        reader.Clear().Should().BeTrue();

        reader.ReadLastLines(500, []).Should().BeEmpty();
        log.Record("info", "sync", "after");
        reader.ReadLastLines(500, []).Should().ContainSingle().Which.Should().EndWith("after");
    }

    [Fact]
    public void Clear_empties_a_file_the_app_holds_open()
    {
        var log = new RollingFileLog(_dir);
        log.Record("info", "sync", "first");
        using var holder = new FileStream(log.CurrentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        new DiagnosticsReader(_dir).Clear().Should().BeTrue();

        new FileInfo(log.CurrentPath).Length.Should().Be(0);
    }

    [Fact]
    public void The_apps_own_logger_writes_only_what_it_is_given_and_so_the_log_holds_no_link_with_a_query()
    {
        // What the app logs (see ArchitectureTests): exception types, ids, host names. A host name is not a link with a query.
        var log = new RollingFileLog(_dir);
        log.Record("warning", "http", "Request to www.googleapis.com failed: HttpRequestException");
        log.Record("warning", "credentials", "The stored credentials of connection conn-1 are damaged and were ignored.");

        var raw = File.ReadAllText(log.CurrentPath);
        raw.Should().NotMatchRegex(@"://\S*\?").And.NotContain("code=").And.NotContain("token=");
        new DiagnosticsReader(_dir).ReadLastLines(10, []).Should().HaveCount(2);
    }
}
