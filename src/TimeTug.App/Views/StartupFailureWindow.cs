using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using Windows.Graphics;

namespace TimeTug.App.Views;

/// <summary>
/// Shown when the engine could not start. The app has no tray yet and keeps a hidden window alive, so without this a failed start
/// would leave an invisible process that never shows a reminder. Built in code because it must not depend on anything that failed.
/// </summary>
internal sealed class StartupFailureWindow : Window
{
    public StartupFailureWindow(ILocalizer text, Action close)
    {
        Title = text.GetString("StartupFailure_Title");
        var closeButton = new Button
        {
            Content = text.GetString("StartupFailure_Close"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(closeButton, text.GetString("StartupFailure_CloseAutomation"));
        closeButton.Click += (_, _) => close();
        Content = new StackPanel
        {
            Padding = new Thickness(32),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = text.GetString("StartupFailure_Title"), Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] },
                new TextBlock { Text = text.GetString("StartupFailure_Body"), TextWrapping = TextWrapping.Wrap },
                closeButton,
            },
        };
        Closed += (_, _) => close();

        // A small centred window, not the full-screen default.
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var size = new SizeInt32(Math.Min(900, work.Width), Math.Min(420, work.Height));
        AppWindow.Resize(size);
        AppWindow.Move(new PointInt32(work.X + ((work.Width - size.Width) / 2), work.Y + ((work.Height - size.Height) / 2)));
    }
}
