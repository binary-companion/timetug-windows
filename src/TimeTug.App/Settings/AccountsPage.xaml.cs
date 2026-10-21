using Microsoft.UI.Xaml.Controls;

namespace TimeTug.App.Settings;

/// <summary>The Accounts pane. Empty until its controls are built; it shows the pane's name.</summary>
public sealed partial class AccountsPage : UserControl
{
    public AccountsPage(string title)
    {
        InitializeComponent();
        Heading.Text = title;
    }
}
