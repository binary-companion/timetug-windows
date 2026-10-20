using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TimeTug.Platform.Diagnostics;
using TimeTug.Platform.Instance;

namespace TimeTug.App;

/// <summary>Application entry point: guards against a second copy, then hands over to <see cref="AppShell"/>.</summary>
public partial class App : Application
{
    private SingleInstanceGuard? _instance;
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
        var diagnostics = FanOutDiagnostics.CreateDefault();
        _instance = SingleInstanceGuard.TryAcquire(SingleInstanceGuard.NameFor(Identity()));
        if (_instance is null)
        {
            // Single instance proper is slice 2. Until then a second launch must not touch the data the first one is using.
            diagnostics.Record("info", "app", "another TimeTug is already running for this package; this launch exits without showing anything");
            Exit();
            return;
        }

        _shell = new AppShell(DispatcherQueue.GetForCurrentThread(), diagnostics);
        // RunAsync records its own failures; this only makes sure a fault that escapes it is logged, not lost.
        _ = _shell.RunAsync().ContinueWith(
            t => FanOutDiagnostics.CreateDefault().Record("error", "startup", "startup faulted " + t.Exception?.GetBaseException().GetType().Name),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>The package family name (it separates the development, Store and website builds), or a fixed name when running without package identity.</summary>
    private static string Identity()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.Id.FamilyName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return "unpackaged";
        }
    }
}
