using Microsoft.UI.Xaml.Controls;

namespace TimeTug.App.Settings;

/// <summary>The Appearance pane. Empty until its controls are built; it shows the pane's name.</summary>
public sealed partial class AppearancePage : UserControl
{
    public AppearancePage(string title)
    {
        InitializeComponent();
        Heading.Text = title;
    }
}
