using System.Text.RegularExpressions;
using TimeTug.Presentation.Diagnostics;

namespace TimeTug.Platform.Diagnostics;

/// <summary>
/// Reads the rolling log files (<c>timetug.log</c>, <c>timetug.1.log</c>, ...) for Copy Diagnostics and empties them for Clear. A line that the safety
/// net refuses (<see cref="DiagnosticsFilter"/>) is dropped before it is counted. The reader never holds a file in a way that stops the app from
/// appending to it.
/// </summary>
public sealed partial class DiagnosticsReader : IDiagnosticsReader
{
    private const FileShare Shared = FileShare.ReadWrite | FileShare.Delete;
    private readonly string _directory;

    public DiagnosticsReader(string directory) => _directory = directory;

    public IReadOnlyList<string> ReadLastLines(int maxLines, IReadOnlyCollection<string> eventTitles)
    {
        ArgumentNullException.ThrowIfNull(eventTitles);
        if (maxLines <= 0)
        {
            return [];
        }

        var found = new List<string>();
        // Newest file first; each file's own lines are in time order. Lines are collected newest-first and turned around at the end.
        foreach (var file in LogFiles())
        {
            var lines = ReadFile(file);
            for (var index = lines.Count - 1; index >= 0 && found.Count < maxLines; index--)
            {
                if (DiagnosticsFilter.IsSafe(lines[index], eventTitles))
                {
                    found.Add(lines[index]);
                }
            }

            if (found.Count >= maxLines)
            {
                break;
            }
        }

        found.Reverse();
        return found;
    }

    public bool Clear()
    {
        var all = true;
        foreach (var file in LogFiles())
        {
            all &= TryEmpty(file);
        }

        return all;
    }

    private static bool TryEmpty(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The app is appending to it right now, or something holds it: empty it in place instead.
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Truncate, FileAccess.Write, Shared);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<string> ReadFile(string path)
    {
        var lines = new List<string>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, Shared);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read is skipped: a report with less in it beats no report.
        }

        return lines;
    }

    /// <summary>The log files, newest first: <c>timetug.log</c>, then <c>timetug.1.log</c>, <c>timetug.2.log</c> and so on.</summary>
    private List<string> LogFiles()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_directory, "timetug*.log")
            .Select(path => (Path: path, Match: LogName().Match(Path.GetFileName(path))))
            .Where(item => item.Match.Success)
            .OrderBy(item => item.Match.Groups["n"].Success ? int.Parse(item.Match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0)
            .Select(item => item.Path)
            .ToList();
    }

    [GeneratedRegex(@"^timetug(\.(?<n>\d{1,9}))?\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LogName();
}
