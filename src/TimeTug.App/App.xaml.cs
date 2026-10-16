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
        _window = new Views.PlaceholderWindow();
        _window.Activate();
    }
}
