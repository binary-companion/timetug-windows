#if SELFTEST
using System.Globalization;
using TimeTug.App.Views;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Abstractions;
using WinRT.Interop;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>broker-probe</c>: the real MSAL broker client loads in this package (the managed files and the native <c>msalruntime</c> for this
/// architecture) and lists the accounts the broker knows, with no window and no token request. A hosted runner may have no Windows account and no broker, so what
/// the listing finds is recorded (a count only: never an email or an id) and never fails the run; the VM run reads it. The check that does fail the run is
/// that the client builds and the native library loads, which is what a broken package would break.
/// </summary>
internal static class BrokerProbeScenario
{
    public const string Name = "broker-probe";

    // Not a real registration: building the client and listing accounts need only a well-formed id, and nothing here asks for a token.
    private const string ProbeClientId = "00000000-0000-0000-0000-000000000000";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        var window = new PlaceholderWindow();
        window.Activate();
        await SelfTestRunner.WaitForFrameAsync(window, cancellation);

        var loads = MsalBrokerClient.NativeRuntimeLoads(out var file);
        run.Check(loads, $"the native runtime {file} loads from the package");

        var log = new NoteDiagnostics(run);
        var client = MsalBrokerClient.TryCreate(ProbeClientId, new FixedParent(WindowNative.GetWindowHandle(window)), log);
        run.Check(client is not null, "the MSAL broker client builds");
        if (client is not null)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            var accounts = await client.ListAccountsAsync(cancellation);
            run.Info(string.Create(CultureInfo.InvariantCulture, $"broker loaded; listed {accounts.Count} account(s) in {started.ElapsedMilliseconds} ms"));
            run.Info("accounts with an email-shaped name: " + accounts.Count(a => a.Email is { } e && e.Contains('@', StringComparison.Ordinal)));
        }

        return run.Finish();
    }

    private sealed class FixedParent(nint handle) : IBrokerParentWindow
    {
        public nint Handle => handle;
    }

    /// <summary>The broker client's own log lines (step and code only) into the result file.</summary>
    private sealed class NoteDiagnostics(ScenarioRun run) : IDiagnostics
    {
        public void Record(string level, string category, string message) => run.Info($"{category}: {message}");
    }
}
#endif
