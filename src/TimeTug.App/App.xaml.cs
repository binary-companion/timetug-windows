using Microsoft.UI.Xaml;

namespace TimeTug.App;

/// <summary>Application entry point. Slice 1 grows this into the host that starts the engine.</summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
#if SELFTEST
        var selfTest = SelfTest.SelfTestOptions.Parse(Environment.GetCommandLineArgs());
        if (selfTest is { Scenario: not "hello" })
        {
            // Scenarios other than hello open their own windows against a scripted engine.
            _ = SelfTest.SelfTestRunner.RunAndExitAsync(null, selfTest);
            return;
        }
#endif
        var window = new Views.PlaceholderWindow();
        _window = window;
        window.Activate();
#if SELFTEST
        if (selfTest is not null)
        {
            _ = SelfTest.SelfTestRunner.RunAndExitAsync(window, selfTest);
        }
#endif
    }
}
