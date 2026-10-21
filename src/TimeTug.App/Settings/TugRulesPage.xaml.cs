using Microsoft.UI.Xaml.Controls;

namespace TimeTug.App.Settings;

/// <summary>The TugRules pane. Empty until its controls are built; it shows the pane's name.</summary>
public sealed partial class TugRulesPage : UserControl
{
    public TugRulesPage(string title)
    {
        InitializeComponent();
        Heading.Text = title;
    }
}
