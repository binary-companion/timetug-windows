using System.Globalization;
using System.Text;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Diagnostics;

/// <summary>
/// A size-limited text log: <c>timetug.log</c> rolls to <c>timetug.1.log</c> and so on when it would pass the size limit,
/// keeping at most <c>maxFiles</c> files in all. A line holds a UTC timestamp, the level, the category and the message,
/// on one line. Callers must not pass event titles or other calendar content (the engine's diagnostics never contain any).
/// </summary>
public sealed class RollingFileLog : IDiagnostics
{
    public const long DefaultMaxBytes = 1_048_576;
    public const int DefaultMaxFiles = 5;
    private const int MaxMessageLength = 2000;

    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    public RollingFileLog(string directory, long maxBytes = DefaultMaxBytes, int maxFiles = DefaultMaxFiles, TimeProvider? time = null)
    {
        _directory = directory;
        _maxBytes = maxBytes;
        _maxFiles = Math.Max(1, maxFiles);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The standard location: <c>LocalFolder\logs</c> of the package, or <c>%LocalAppData%\TimeTug\logs</c> when the process has no package identity.</summary>
    public static string DefaultDirectory()
    {
        try
        {
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "logs");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or FileNotFoundException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTug", "logs");
        }
    }

    public string CurrentPath => Path.Combine(_directory, "timetug.log");

    public void Record(string level, string category, string message)
    {
        var line = Format(_time.GetUtcNow(), level, category, message);
        var bytes = Encoding.UTF8.GetBytes(line);
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var current = new FileInfo(CurrentPath);
                if (current.Exists && current.Length > 0 && current.Length + bytes.Length > _maxBytes)
                {
                    Roll();
                }

                using var stream = new FileStream(CurrentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down; the entry is dropped.
            }
        }
    }

    private static string Format(DateTimeOffset now, string level, string category, string message)
    {
        var text = message.ReplaceLineEndings(" ");
        if (text.Length > MaxMessageLength)
        {
            text = string.Concat(text.AsSpan(0, MaxMessageLength), "...");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{now:yyyy-MM-dd'T'HH:mm:ss.fff'Z'} [{level}] {category}: {text}{Environment.NewLine}");
    }

    private void Roll()
    {
        // timetug.log -> timetug.1.log -> ... ; the oldest beyond the limit is deleted.
        var oldest = Archive(_maxFiles - 1);
        if (_maxFiles > 1)
        {
            File.Delete(oldest);
            for (var index = _maxFiles - 2; index >= 1; index--)
            {
                if (File.Exists(Archive(index)))
                {
                    File.Move(Archive(index), Archive(index + 1));
                }
            }

            File.Move(CurrentPath, Archive(1));
        }
        else
        {
            File.Delete(CurrentPath);
        }
    }

    private string Archive(int index) => Path.Combine(_directory, $"timetug.{index}.log");
}
