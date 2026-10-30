using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TimeTug.Platform.Diagnostics;
using TimeTug.Platform.Instance;

namespace TimeTug.App;

/// <summary>Application entry point: hands a second copy over to the first, then hands over to <see cref="AppShell"/>.</summary>
public partial class App : Application
{
    private AppInstanceService? _instance;
    private AppShell? _shell;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // The type only: a message could carry calendar content.
            FanOutDiagnostics.CreateDefault().Record("error", "app", "unhandled exception " + e.Exception?.GetType().Name);
        };
    }

    /// <summary>Quits the app in order (see <see cref="AppShell.QuitAsync"/>); the tray's Quit calls this.</summary>
    internal static Task QuitAsync() => Current is App { _shell: { } shell } ? shell.QuitAsync() : Task.CompletedTask;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
#if SELFTEST
        var selfTest = SelfTest.SelfTestOptions.Parse(Environment.GetCommandLineArgs());
        if (selfTest is not null)
        {
            // A self-test run opens its own windows, uses its own data folder and never takes the single-instance guard.
            _ = SelfTest.SelfTestRunner.RunAndExitAsync(selfTest);
            return;
        }
#endif
        _ = LaunchAsync();
    }

    /// <summary>
    /// The first launch becomes the primary and starts the app. Any later launch hands itself to the primary (which opens the flyout) and
    /// exits at once: it writes only to ETW, never starts the engine and never touches the first copy's files.
    /// </summary>
    private async Task LaunchAsync()
    {
        try
        {
            _instance = AppInstanceService.CreateDefault(PackageFamilyName(), EtwDiagnostics.Log);
            if (!_instance.IsPrimary)
            {
                await _instance.RedirectAsync(ActivationParser.FromArguments(Environment.GetCommandLineArgs()));
                Exit();
                return;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Without knowing whether another copy runs it is safer to end than to start a second one on the same data.
            EtwDiagnostics.Log.Record("error", "app", "the single-instance check faulted " + e.GetType().Name);
            Exit();
            return;
        }

        var diagnostics = FanOutDiagnostics.CreateDefault();
        _instance.Diagnostics = diagnostics;
        // The person chose a jump list item while TimeTug was not running: the action waits for the engine's first state like any early launch.
        _instance.AcceptLaunch(ActivationParser.FromArguments(Environment.GetCommandLineArgs()));
        _shell = new AppShell(DispatcherQueue.GetForCurrentThread(), diagnostics, instance: _instance);
        try
        {
            // RunAsync records its own failures; this only makes sure a fault that escapes it is logged, not lost.
            await _shell.RunAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            diagnostics.Record("error", "startup", "startup faulted " + e.GetType().Name);
        }
    }

    /// <summary>The package family name (it separates the development, Store and website builds), or null when running without package identity.</summary>
    private static string? PackageFamilyName()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.Id.FamilyName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
