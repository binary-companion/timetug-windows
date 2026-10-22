#if SELFTEST
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using TimeTug.Platform.Capture;

namespace TimeTug.App.SelfTest;

/// <summary>One named check and whether it held.</summary>
internal sealed record SelfTestCheck(string Name, bool Ok);

/// <summary>A screenshot a scenario kept: <c>NN-label.png</c> in the output folder.</summary>
internal sealed record SelfTestShot(string File, string Method, int DistinctColours);

/// <summary>One capture technique tried for a screenshot (the result file keeps these for diagnosing a blank picture).</summary>
internal sealed record SelfTestAttempt(string Name, string Method, int DistinctColours, string? Error);

/// <summary>
/// What a scenario writes to <c>result.json</c>. <see cref="Ok"/> is true only when no check failed and nothing went wrong
/// outside the checks; the run script fails on anything else, and on a missing file or a blank screenshot.
/// </summary>
internal sealed record SelfTestResult(
    bool Ok,
    string Scenario,
    string? Error,
    string Architecture,
    string? PackageFullName,
    IReadOnlyList<SelfTestCheck> Checks,
    IReadOnlyList<string> Info,
    IReadOnlyList<SelfTestShot> Screenshots,
    IReadOnlyList<SelfTestAttempt> Attempts);

/// <summary>
/// The record of one scenario run: named checks, notes, and numbered screenshots (<c>01-primary.png</c>, <c>02-mirror.png</c>…).
/// A scenario makes one, calls <see cref="Check"/> and <see cref="ShotAsync"/> as it goes, and returns <see cref="Finish"/>.
/// </summary>
internal sealed class ScenarioRun
{
    private readonly List<SelfTestCheck> _checks = [];
    private readonly List<string> _info = [];
    private readonly List<SelfTestShot> _shots = [];
    private readonly List<SelfTestAttempt> _attempts = [];
    private int _taken;

    public ScenarioRun(SelfTestOptions options)
    {
        Options = options;
        Directory.CreateDirectory(options.OutputFolder);
    }

    public SelfTestOptions Options { get; }

    public string Folder => Options.OutputFolder;

    public IReadOnlyList<SelfTestCheck> Checks => _checks;

    /// <summary>The file of the screenshot taken last (null before the first), for a scenario that reads pixels from it.</summary>
    public string? LastShotPath { get; private set; }

    public bool Failed => _checks.Exists(c => !c.Ok);

    /// <summary>Records a check. The name says what is expected, so a failure reads without the code.</summary>
    public bool Check(bool condition, string name)
    {
        _checks.Add(new SelfTestCheck(name, condition));
        return condition;
    }

    /// <summary>Records a note that is not a check (a value worth reading in the artifact).</summary>
    public void Info(string text) => _info.Add(text);

    /// <summary>
    /// Saves the window as the next numbered screenshot: the window as Windows draws it (dialogs and backdrops included), or the
    /// XAML root when that comes out blank. Records a check that the picture is not blank.
    /// </summary>
    public async Task<bool> ShotAsync(Window window, string label)
    {
        var name = FormattableString.Invariant($"{++_taken:00}-{label}");
        var (best, attempts) = await SelfTestRunner.CaptureAsync(window, Folder, name);
        _attempts.AddRange(attempts);
        if (best is not null)
        {
            _shots.Add(best);
            LastShotPath = Path.Combine(Folder, best.File);
        }

        return Check(best is not null, name + ".png is not blank");
    }

    public SelfTestResult Finish(string? error = null)
    {
        string? package = null;
        try
        {
            package = Windows.ApplicationModel.Package.Current.Id.FullName;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // No package identity: reported as null so the run script can fail on it.
        }

        var failed = _checks.Where(c => !c.Ok).Select(c => c.Name).ToList();
        var problem = error ?? (failed.Count == 0 ? null : string.Join("; ", failed));
        return new SelfTestResult(
            Ok: problem is null,
            Scenario: Options.Scenario,
            Error: problem,
            Architecture: RuntimeInformation.ProcessArchitecture.ToString().ToUpper(CultureInfo.InvariantCulture),
            PackageFullName: package,
            Checks: _checks,
            Info: _info,
            Screenshots: _shots,
            Attempts: _attempts);
    }
}
#endif
