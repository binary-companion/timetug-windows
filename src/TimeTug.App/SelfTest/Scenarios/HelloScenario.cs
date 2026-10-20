#if SELFTEST
using TimeTug.App.Views;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>Scenario <c>hello</c>: the packaged app starts with package identity, shows a window and the window can be captured.</summary>
internal static class HelloScenario
{
    public const string Name = "hello";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        var window = new PlaceholderWindow();
        window.Activate();
        await SelfTestRunner.WaitForFrameAsync(window, cancellation);
        await run.ShotAsync(window, "hello");
        return run.Finish();
    }
}
#endif
