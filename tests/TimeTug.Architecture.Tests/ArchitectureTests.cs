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
