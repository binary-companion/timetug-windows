using System.Globalization;
using System.Text.Json;
using Axe.Windows.Automation;

namespace TimeTug.App.A11y;

/// <summary>
/// Scans the windows of the running development build with axe-windows while the <c>a11y-hold</c> scenario holds them open.
/// <c>TimeTug.App.A11y --out &lt;folder&gt; [--rounds N] [--wait &lt;seconds&gt;]</c>: for each round it waits for the scenario's
/// <c>ready-N.json</c> (it names the process; round 0 is a canary window whose violation must be found, the others are the real
/// windows), scans every window of that process, writes <c>a11y-report.json</c> and
/// <c>a11y-report.txt</c> into the folder, then writes <c>done-N.txt</c> so the scenario carries on. The exit code is 1 when any error
/// remains after the suppressions in <c>suppressions.json</c> (each one needs a reason), 2 when a round could not be scanned.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static async Task<int> Main(string[] args)
    {
        var folder = Option(args, "--out") ?? throw new ArgumentException("--out <folder> is required");
        var rounds = int.Parse(Option(args, "--rounds") ?? "2", CultureInfo.InvariantCulture);
        var wait = TimeSpan.FromSeconds(int.Parse(Option(args, "--wait") ?? "120", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var suppressions = Suppression.Load(Path.Combine(AppContext.BaseDirectory, "suppressions.json"));

        var report = new List<RoundReport>();
        var problem = false;
        for (var round = 0; round <= rounds; round++)
        {
            var ready = Path.Combine(folder, $"ready-{round}.json");
            var done = Path.Combine(folder, $"done-{round}.txt");
            try
            {
                var (processId, what) = await WaitForReadyAsync(ready, wait);
                Console.WriteLine($"round {round}: scanning process {processId} ({what})");
                var scanned = Scan(folder, round, what, processId, suppressions);
                report.Add(scanned);
                if (round == 0 && scanned.Errors.Count + scanned.Suppressed.Count == 0)
                {
                    // The canary window has a button with no name. A scan that does not flag it is not seeing the app's controls.
                    Console.Error.WriteLine("the canary violation was not found: the scanner is not seeing the app's controls");
                    problem = true;
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Console.Error.WriteLine($"round {round} could not be scanned: {e.GetType().Name}: {e.Message}");
                report.Add(new RoundReport(round, string.Empty, [], [], $"{e.GetType().Name}: {e.Message}"));
                problem = true;
            }
            finally
            {
                // Always release the scenario, so a failed scan fails here and not as a hang.
                await File.WriteAllTextAsync(done, "done");
            }
        }

        await File.WriteAllTextAsync(Path.Combine(folder, "a11y-report.json"), JsonSerializer.Serialize(report, Json));
        await File.WriteAllTextAsync(Path.Combine(folder, "a11y-report.txt"), Describe(report));
        Console.WriteLine(Describe(report));
        var errors = report.Where(r => r.Round > 0).Sum(r => r.Errors.Count);
        return problem ? 2 : errors > 0 ? 1 : 0;
    }

    private static RoundReport Scan(string folder, int round, string what, int processId, List<Suppression> suppressions)
    {
        var config = Config.Builder.ForProcessId(processId)
            .WithOutputDirectory(Path.Combine(folder, "axe", $"round-{round}"))
            .WithOutputFileFormat(OutputFileFormat.A11yTest)
            .WithAlwaysSaveTestFile()
            .Build();
        var output = ScannerFactory.CreateScanner(config).Scan(null);

        var windows = new List<string>();
        var errors = new List<Finding>();
        var accepted = new List<Finding>();
        foreach (var window in output.WindowScanOutputs)
        {
            windows.Add($"{window.ErrorCount} error(s)");
            foreach (var error in window.Errors)
            {
                var properties = error.Element.Properties;
                var finding = new Finding(
                    error.Rule.ID.ToString(),
                    error.Rule.Description,
                    properties.GetValueOrDefault("Name") ?? string.Empty,
                    properties.GetValueOrDefault("ControlType") ?? string.Empty,
                    properties.GetValueOrDefault("AutomationId") ?? string.Empty,
                    properties.GetValueOrDefault("BoundingRectangle") ?? string.Empty,
                    suppressions.FirstOrDefault(s => s.Matches(error.Rule.ID.ToString(), properties.GetValueOrDefault("Name"), round))?.Reason);
                (finding.SuppressedBecause is null ? errors : accepted).Add(finding);
            }
        }

        return new RoundReport(round, what, errors, accepted, null) { WindowsScanned = output.WindowScanOutputs.Count };
    }

    private static async Task<(int ProcessId, string What)> WaitForReadyAsync(string ready, TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(ready))
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(ready));
                    return (document.RootElement.GetProperty("processId").GetInt32(), document.RootElement.GetProperty("what").GetString() ?? string.Empty);
                }
                catch (IOException)
                {
                    // The scenario is still writing it.
                }
                catch (JsonException)
                {
                    // The same.
                }
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"{Path.GetFileName(ready)} did not appear within {wait.TotalSeconds:n0} seconds (the app did not reach the scenario's hold)");
    }

    private static string Describe(IReadOnlyList<RoundReport> rounds)
    {
        var lines = new List<string>();
        foreach (var round in rounds)
        {
            lines.Add(round.Round == 0 ? $"Canary: {round.What} (errors here are expected)" : $"Round {round.Round}: {round.What}");
            if (round.Failure is not null)
            {
                lines.Add("  NOT SCANNED: " + round.Failure);
                continue;
            }

            lines.Add($"  {round.WindowsScanned} top-level window(s) scanned, {round.Errors.Count} error(s), {round.Suppressed.Count} accepted");
            foreach (var error in round.Errors)
            {
                lines.Add($"  ERROR {error.RuleId} {error.Rule}: {error.ControlType} \"{error.Name}\" id={error.AutomationId} at {error.Bounds}");
            }

            foreach (var error in round.Suppressed)
            {
                lines.Add($"  accepted {error.RuleId} {error.Rule}: \"{error.Name}\" because {error.SuppressedBecause}");
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed record Finding(string RuleId, string Rule, string Name, string ControlType, string AutomationId, string Bounds, string? SuppressedBecause);

    private sealed record RoundReport(int Round, string What, List<Finding> Errors, List<Finding> Suppressed, string? Failure)
    {
        public int WindowsScanned { get; init; }
    }

    private sealed record Suppression(string Rule, string? Name, string Reason, int? Round = null)
    {
        public static List<Suppression> Load(string path)
        {
            if (!File.Exists(path))
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<Suppression>();
            foreach (var item in document.RootElement.GetProperty("suppressions").EnumerateArray())
            {
                var reason = item.GetProperty("reason").GetString();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new InvalidOperationException("every suppression needs a reason");
                }

                list.Add(new Suppression(item.GetProperty("rule").GetString() ?? string.Empty, item.TryGetProperty("name", out var name) ? name.GetString() : null, reason, item.TryGetProperty("round", out var round) ? round.GetInt32() : null));
            }

            return list;
        }

        public bool Matches(string rule, string? elementName, int round) => Rule == rule && (Name is null || Name == (elementName ?? string.Empty)) && (Round is null || Round == round);
    }
}
