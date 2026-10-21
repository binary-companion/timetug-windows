using Microsoft.UI.Xaml.Controls;

namespace TimeTug.App.Settings;

/// <summary>The Calendars pane. Empty until its controls are built; it shows the pane's name.</summary>
public sealed partial class CalendarsPage : UserControl
{
    public CalendarsPage(string title)
    {
        InitializeComponent();
        Heading.Text = title;
    }
}
