#if SELFTEST
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using TimeTug.App.Composition;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using Pkg = TimeTug.Engine;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>upgrade-seed</c> (used only by <c>scripts/vm/vm-upgrade-check.sh</c>, which runs it on the build before an upgrade): starts the app over the
/// package's REAL data folder (the LocalState that survives an in-place upgrade) with the engine's own files already there, changes three engine settings
/// through the engine, and writes <c>state.json</c> (what <c>upgrade-state</c> writes). Nothing but the settings changes; no calendar is contacted
/// (the scripted host answers nothing and the Windows account broker is a fake that lists no account).
/// </summary>
internal static class UpgradeSeedScenario
{
    public const string Name = "upgrade-seed";

    public static Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation) =>
        UpgradeProbe.RunAsync(options, seedSettings: true, cancellation);
}

/// <summary>
/// Scenario <c>upgrade-state</c> (used only by <c>scripts/vm/vm-upgrade-check.sh</c>): starts the app over the package's REAL data folder and writes
/// <c>state.json</c> to the output folder: the engine's accounts (connection id, kind, display name), the account kinds it offers, its settings and the
/// engine's own diagnostics (which include "ledger loaded with N keys"). The check script runs it before and after the upgrade and compares the two files.
/// It contains no calendar content: the files it reads are synthetic, and event titles are never written.
/// </summary>
internal static class UpgradeStateScenario
{
    public const string Name = "upgrade-state";

    public static Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation) =>
        UpgradeProbe.RunAsync(options, seedSettings: false, cancellation);
}

internal static class UpgradeProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, bool seedSettings, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        var dataDirectory = EngineStartup.DefaultDataDirectory();
        run.Info("engine data folder: " + dataDirectory);
        var diagnostics = new RecordingDiagnostics();
        var host = new ScriptedCalendarHost();
        var broker = new FakeBrokerClient(() => DateTimeOffset.UtcNow) { Accounts = [] };
        var shell = new AppShell(DispatcherQueue.GetForCurrentThread(), diagnostics, services =>
        {
            services.AddSingleton(new EngineStartup(dataDirectory, Testing: true, new Dictionary<string, Pkg.OAuthClient> { ["microsoft"] = new("test-client", null) }));
            services.AddSingleton<Pkg.IEngineHost>(sp => new BrokeredEngineHost(host, new BrokeredTokenHost(broker, sp.GetRequiredService<IBrokerSignInIntent>(), diagnostics), new BrokerWindowsAccountSource(broker)));
            services.AddSingleton<INotificationState>(new FakeNotifications());
            services.AddSingleton<IToastService>(new FakeToasts());
            services.AddSingleton<IUrlLauncher>(new FakeLauncher());
            services.AddSingleton<IPresentationSettingsStore>(new MemoryPresentationSettingsStore());
            services.AddSingleton<IWindowsAccountDismissalStore>(new MemoryDismissalStore());
        }, withTray: false);

        await shell.RunAsync().WaitAsync(TimeSpan.FromSeconds(60), cancellation);
        run.Check(!shell.Failed, "the app started with the real engine" + (shell.FailureReason is null ? string.Empty : ": " + shell.FailureReason));
        if (shell.Failed)
        {
            return run.Finish();
        }

        var client = shell.Services.GetRequiredService<IEngineClient>();
        AppState? latest = null;
        var gate = new object();
        using var subscription = client.State.Subscribe(new StateObserver(state =>
        {
            lock (gate)
            {
                latest = state;
            }
        }));

        AppState? Latest()
        {
            lock (gate)
            {
                return latest;
            }
        }

        async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 15000)
        {
            for (var waited = 0; waited < milliseconds; waited += 100)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100, cancellation);
            }

            return condition();
        }

        run.Check(await WaitFor(() => Latest() is { Accounts.Count: > 0 }), "the engine published its accounts from the data folder");
        run.Check(await WaitFor(() => Latest() is { AvailableKinds.Count: > 0 }), "the engine lists the account kinds");

        if (seedSettings)
        {
            // Three settings, each its own patch, as the Settings window sends them. The values differ from the engine's defaults.
            var patches = new EngineSettingsPatch[]
            {
                new() { LeadTime = TimeSpan.FromSeconds(120) },
                new() { RequireConferenceLink = true },
                new() { SkipAllDayEvents = false },
            };
            foreach (var patch in patches)
            {
                await client.ApplySettingsAsync(patch with { Current = (Latest() ?? throw new InvalidOperationException("no state")).Settings });
                run.Check(await WaitFor(() => Latest() is { } now && patch.IsReflectedBy(now.Settings)), "the engine published the changed setting");
            }
        }

        // Let the engine finish loading what it reads at start (ledger, dedup state) and publish its first diagnostics.
        await Task.Delay(3000, cancellation);
        var final = Latest();
        run.Check(final is not null, "a state was published");
        if (final is not null)
        {
            Pkg.EngineDiagnostic[] engineDiagnostics;
            lock (host.Diagnostics)
            {
                engineDiagnostics = [.. host.Diagnostics];
            }

            var settings = final.Settings;
            var dump = new
            {
                accounts = final.Accounts.OrderBy(a => a.ConnectionId, StringComparer.Ordinal)
                    .Select(a => new { a.ConnectionId, a.KindId, a.DisplayName, a.Status }).ToArray(),
                availableKinds = final.AvailableKinds.Select(k => k.Id).Order(StringComparer.Ordinal).ToArray(),
                settings = new
                {
                    settings.Enabled,
                    leadTimeSeconds = settings.LeadTime.TotalSeconds,
                    settings.RequireConferenceLink,
                    settings.RequireOtherAttendees,
                    settings.SkipAllDayEvents,
                    settings.InferenceEnabled,
                    hiddenCalendarKeys = settings.HiddenCalendarKeys.Order(StringComparer.Ordinal).ToArray(),
                    takeoverCalendarKeys = settings.TakeoverCalendarKeys.Order(StringComparer.Ordinal).ToArray(),
                },
                engineDiagnostics = engineDiagnostics.Select(d => new { d.Level, d.Category, d.Message }).ToArray(),
            };
            await File.WriteAllTextAsync(Path.Combine(options.OutputFolder, "state.json"), JsonSerializer.Serialize(dump, JsonOptions), cancellation);
            run.Info(string.Create(CultureInfo.InvariantCulture, $"state.json: {dump.accounts.Length} account(s), {dump.engineDiagnostics.Length} engine diagnostic(s)"));
        }

        await shell.QuitAsync(exitProcess: false).WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        return run.Finish();
    }

    private sealed class StateObserver(Action<AppState> onNext) : IObserver<AppState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(AppState value) => onNext(value);
    }
}
#endif
