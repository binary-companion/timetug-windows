using System.Text.RegularExpressions;

namespace TimeTug.Architecture.Tests;

public partial class ArchitectureTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Presentation_has_no_windows_or_native_dependencies()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "TimeTug.Presentation", "TimeTug.Presentation.csproj"));
        project.Should().NotContainEquivalentOf("windows");
        project.Should().NotContain("WindowsAppSDK");
    }

    [Fact]
    public void Only_the_platform_project_may_use_pinvoke_or_the_native_engine()
    {
        foreach (var file in SourceFiles("src/TimeTug.Presentation").Concat(SourceFiles("src/TimeTug.App")))
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("DllImport", file);
            text.Should().NotContain("LibraryImport", file);
            text.Should().NotContain("TimeTug.Engine.NativeMethods", file);
        }
    }

    [Fact]
    public void No_scheduling_or_default_logic_in_csharp()
    {
        // The engine owns lead time, snooze choices and rules. Timers are allowed only in the platform
        // project (notification substitutes such as the midnight rollover), never in the app or the
        // presentation layer where they could schedule takeovers.
        var platform = Path.Combine(Root, "src", "TimeTug.Platform") + Path.DirectorySeparatorChar;
        foreach (var file in SourceFiles("src"))
        {
            var text = File.ReadAllText(file);
            text.Should().NotMatchRegex(@"\bleadTime\s*=\s*\d", file);
            if (file.StartsWith(platform, StringComparison.Ordinal))
            {
                continue;
            }

            TimerUse().IsMatch(text).Should().BeFalse($"{file}: timers belong in src/TimeTug.Platform or in the engine");
        }
    }

    [Fact]
    public void The_tug_state_machine_reads_no_os_clock_and_waits_for_nothing()
    {
        // It reacts to engine events and uses only the injected TimeProvider.
        var files = SourceFiles("src/TimeTug.Presentation/Tug").ToList();
        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            OsClockOrWait().IsMatch(File.ReadAllText(file)).Should().BeFalse($"{file}: the tug state machine must use only the injected TimeProvider and never wait or schedule");
        }
    }

    [Fact]
    public void Selftest_code_is_compiled_only_under_the_SELFTEST_define()
    {
        foreach (var file in SourceFiles("src/TimeTug.App/SelfTest"))
        {
            File.ReadAllText(file).Should().StartWith("#if SELFTEST", file);
        }
    }

    [Fact]
    public void The_apps_own_log_entries_never_name_a_link_a_token_or_a_title()
    {
        // Copy Diagnostics hands the log to the person to share, and says it holds no feed links or event titles. The reader also drops any
        // such line as a safety net, but the app's own logger must not write one in the first place: what a log call interpolates or
        // concatenates (not its fixed words) may not be a title, link, token, secret or the like. A host name is fine ("uri.Host").
        var forbidden = ForbiddenInLog();
        var calls = 0;
        foreach (var file in SourceFiles("src").Where(f => !f.Contains($"{Path.DirectorySeparatorChar}SelfTest{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (Match call in LogCall().Matches(text))
            {
                calls++;
                var end = text.IndexOf(';', call.Index);
                var statement = text[call.Index..(end < 0 ? text.Length : end)];
                var holes = InterpolationHole().Matches(statement).Select(m => m.Groups[1].Value);
                var operands = StringLiteral().Replace(statement, " ");
                var expressions = string.Join(' ', holes.Append(operands)).Replace("uri.Host", string.Empty, StringComparison.Ordinal);
                forbidden.IsMatch(expressions).Should().BeFalse($"{Path.GetRelativePath(Root, file)}: a log entry must not carry {forbidden.Match(expressions).Value}: {statement.Trim()}");
            }
        }

        calls.Should().BeGreaterThan(30, "the scan found the app's log calls");
    }

    [GeneratedRegex(@"\b(Record|Note)\(")]
    private static partial Regex LogCall();

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex InterpolationHole();

    // Fixed words: "a, b" or $"a {b} c" or @"..." (the app has no raw strings in log calls).
    [GeneratedRegex("\\$?@?\"(?:[^\"\\\\]|\\\\.)*\"")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"\b(title|titles|url|urls|uri|absoluteuri|query|token|tokens|secret|password|summary|description|attendee|attendees|location|body|content)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenInLog();

    [GeneratedRegex(@"\b(DispatcherTimer|DispatcherQueueTimer|PeriodicTimer|System\.Timers|System\.Threading\.Timer|new\s+Timer\s*\(|CreateTimer\s*\()")]
    private static partial Regex TimerUse();

    [GeneratedRegex(@"\b(DateTime(Offset)?\.(Now|UtcNow|Today)|Stopwatch|Environment\.TickCount|TimeProvider\.System|Task\.Delay|Thread\.Sleep|\.Delay\()")]
    private static partial Regex OsClockOrWait();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TimeTug.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("TimeTug.sln not found above " + AppContext.BaseDirectory);
    }

    // Every .cs file (including *.xaml.cs) under a repository-relative folder, skipping build output.
    private static IEnumerable<string> SourceFiles(string relativeFolder)
    {
        var folder = Path.Combine(Root, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(folder, file).Split(separators).Any(part => part is "obj" or "bin"))
            .Order(StringComparer.Ordinal);
    }
}
