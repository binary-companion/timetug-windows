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
