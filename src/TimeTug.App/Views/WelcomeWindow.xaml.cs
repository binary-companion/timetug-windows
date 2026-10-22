using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Welcome;
using Windows.Graphics;

namespace TimeTug.App.Views;

/// <summary>
/// The first-run window: a normal centred window with its own taskbar button (not attached to the tray), Mica behind it.
/// Everything it shows comes from <see cref="WelcomeViewModel"/>; this file only places the window and moves focus.
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    private const int WidthInDips = 560;
    private const int HeightInDips = 700;
    private readonly AccountFormService _forms;
    private bool _syncing;

    public WelcomeWindow(WelcomeViewModel viewModel, AccountFormService forms, ILocalizer text)
    {
        ViewModel = viewModel;
        _forms = forms;
        InitializeComponent();
        Title = text.GetString("WelcomeWindowTitle");
        ViewModel.CloseRequested += (_, _) => Close();
        ViewModel.PropertyChanged += OnViewModelChanged;
        Closed += OnClosed;
        Root.Loaded += OnLoaded;
        if (viewModel.LaunchAtLogin is { } launch)
        {
            LaunchLink.Content = launch.SettingsLinkText;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LaunchLink, launch.SettingsLinkAutomationName);
            launch.PropertyChanged += (_, _) => SyncLaunch();
            SyncLaunch();
        }
    }

    /// <summary>The launch-at-login checkbox, its note, link and error (for the self-test).</summary>
    internal CheckBox LaunchCheckBox => LaunchCheck;

    internal TextBlock LaunchNoteText => LaunchNote;

    internal HyperlinkButton LaunchSettingsLink => LaunchLink;

    internal TextBlock LaunchErrorText => LaunchError;

    /// <summary>Puts the checkbox, its note, link and error to the view model's values (what Windows says) without raising the person's-change handler.</summary>
    private void SyncLaunch()
    {
        if (ViewModel.LaunchAtLogin is not { } launch)
        {
            return;
        }

        _syncing = true;
        try
        {
            LaunchCheck.IsChecked = launch.IsOn;
            LaunchCheck.IsEnabled = launch.CanChange;
            LaunchNote.Text = launch.Note ?? string.Empty;
            LaunchNote.Visibility = launch.HasNote ? Visibility.Visible : Visibility.Collapsed;
            LaunchLink.Visibility = launch.ShowSettingsLink ? Visibility.Visible : Visibility.Collapsed;
            LaunchError.Text = launch.Error ?? string.Empty;
            LaunchError.Visibility = launch.HasError ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _syncing = false;
        }
    }

    private async void OnLaunchChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        await ViewModel.SetLaunchAtLoginAsync(LaunchCheck.IsChecked == true);
        // The box shows what Windows says, which can differ from what the person asked for.
        SyncLaunch();
    }

    private async void OnLaunchLink(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LaunchAtLogin is { } launch)
        {
            await launch.OpenStartupSettingsAsync();
        }
    }

    public WelcomeViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _forms.XamlRoot = Root.XamlRoot;
        var scale = Root.XamlRoot.RasterizationScale;
        var size = new SizeInt32((int)(WidthInDips * scale), (int)(HeightInDips * scale));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        size = new SizeInt32(Math.Min(size.Width, work.Width), Math.Min(size.Height, work.Height));
        AppWindow.Resize(size);
        AppWindow.Move(new PointInt32(work.X + ((work.Width - size.Width) / 2), work.Y + ((work.Height - size.Height) / 2)));
        FocusFirstControl();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WelcomeViewModel.Step))
        {
            // The new step's first control gets focus so the keyboard (and Narrator) continue where the person is looking.
            DispatcherQueue.TryEnqueue(FocusFirstControl);
        }
    }

    private void FocusFirstControl()
    {
        if (ViewModel.IsConnectedStep)
        {
            DoneButton.Focus(FocusState.Programmatic);
            return;
        }

        var first = FocusManager.FindFirstFocusableElement(ConnectPanel) as Control;
        first?.Focus(FocusState.Programmatic);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _forms.XamlRoot = null;
        // Closing the window mid sign-in cancels it, so the engine is not left waiting for a browser or a form.
        _ = ViewModel.Accounts.CancelAsync();
        ViewModel.Dispose();
    }
}
