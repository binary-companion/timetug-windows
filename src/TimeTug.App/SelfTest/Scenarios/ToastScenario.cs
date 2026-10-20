#if SELFTEST
using Microsoft.Windows.AppNotifications;
using TimeTug.Platform.OsState;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>toast</c>: with real package identity, registers the toast service, shows a takeover toast through
/// <c>AppNotificationManager</c> and checks that Windows accepts it, then reads the notification state. A person
/// clicking the buttons is a manual check (<c>docs/manual-tests/windows-checklist.md</c>).
/// </summary>
internal static class ToastScenario
{
    public const string Name = "toast";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        void Check(bool condition, string description) => run.Check(condition, description);

        var diagnostics = new ListDiagnostics();
        using var toasts = new ToastService(() => new NoActions(), new ResourceLoaderLocalizer(), diagnostics);
        toasts.Start();
        Check(!diagnostics.Entries.Any(e => e.Message.Contains("could not be registered", StringComparison.Ordinal)), "the toast service registered for button presses");
        Check(toasts.IsEnabled, "notifications are enabled for TimeTug");

        var now = DateTimeOffset.UtcNow;
        var request = new TakeoverRequest(
            "selftest-toast", "Self-test meeting", now.AddMinutes(1), now.AddMinutes(31), false, false,
            "https://meet.example.test/selftest", [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)], "Self-test calendar", 2);
        var accepted = await toasts.ShowAsync(request);
        Check(accepted, "Windows accepted the toast");
        if (options.HoldSeconds > 0)
        {
            // For a real screen capture: the toast stays up this long.
            await Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds), cancellation);
        }

        using var notifications = new NotificationState(TimeProvider.System, toastsEnabled: () => toasts.IsEnabled);
        run.Info($"CanShowFullScreen={notifications.CanShowFullScreen}, ToastsAllowed={notifications.ToastsAllowed}");
        foreach (var entry in diagnostics.Entries)
        {
            run.Info($"{entry.Level} {entry.Category}: {entry.Message}");
        }

        await Task.Delay(1500, cancellation);
        await AppNotificationManager.Default.RemoveAllAsync();
        return run.Finish();
    }

    private sealed class ListDiagnostics : IDiagnostics
    {
        public List<(string Level, string Category, string Message)> Entries { get; } = [];

        public void Record(string level, string category, string message) => Entries.Add((level, category, message));
    }

    private sealed class NoActions : ITugActions
    {
        public ValueTask JoinAsync(string eventId) => ValueTask.CompletedTask;

        public ValueTask SnoozeAsync(string eventId, TimeSpan duration) => ValueTask.CompletedTask;

        public ValueTask DismissAsync(string eventId) => ValueTask.CompletedTask;
    }
}
#endif
