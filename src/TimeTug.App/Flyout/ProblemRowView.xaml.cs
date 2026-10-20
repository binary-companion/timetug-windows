using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Flyout;

namespace TimeTug.App.Flyout;

/// <summary>A source the engine cannot read: the warning icon, what is wrong, and (for an expired sign-in) a "Sign in again" button that asks the view model to reconnect.</summary>
internal sealed partial class ProblemRowView : UserControl
{
    private readonly ProblemRow _row;
    private readonly Action<ProblemRow> _action;

    public ProblemRowView(ProblemRow row, FlyoutPalette palette, Action<ProblemRow> action)
    {
        _row = row;
        _action = action;
        InitializeComponent();
        Frame.Background = palette.ProblemFill;
        Frame.BorderBrush = palette.ProblemStroke;
        Icon.Foreground = palette.ProblemIcon;
        Message.Text = row.Message;
        Message.Foreground = palette.Primary;
        AutomationProperties.SetName(this, row.Message);
        if (row.HasAction)
        {
            Action.Content = row.ActionText;
            AutomationProperties.SetName(Action, row.ActionAutomationName ?? row.ActionText);
        }
        else
        {
            Action.Visibility = Visibility.Collapsed;
        }
    }

    public ProblemRow Row => _row;

    /// <summary>The button, for the self-test.</summary>
    public Button? ActionButton => _row.HasAction ? Action : null;

    private void OnActionClick(object sender, RoutedEventArgs e) => _action(_row);
}
