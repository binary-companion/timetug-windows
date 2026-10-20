using System.Windows.Input;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Tray;

namespace TimeTug.App.Tray;

/// <summary>Turns the view model's rows into the flyout the tray icon shows (the library draws it as a native menu).</summary>
internal static class TrayMenu
{
    /// <param name="commandFor">The command a row runs when chosen.</param>
    public static MenuFlyout Build(TrayView view, Func<TrayCommand, ICommand> commandFor)
    {
        var flyout = new MenuFlyout();
        foreach (var row in view.Rows)
        {
            switch (row.Kind)
            {
                case TrayRowKind.Separator:
                    flyout.Items.Add(new MenuFlyoutSeparator());
                    break;
                case TrayRowKind.Toggle:
                    flyout.Items.Add(new ToggleMenuFlyoutItem
                    {
                        Text = row.Text,
                        IsChecked = row.IsChecked,
                        IsEnabled = row.IsEnabled,
                        Command = commandFor(row.Command!.Value),
                    });
                    break;
                default:
                    flyout.Items.Add(new MenuFlyoutItem
                    {
                        Text = row.Text,
                        IsEnabled = row.IsEnabled,
                        Command = row.Kind == TrayRowKind.Information ? null : commandFor(row.Command!.Value),
                    });
                    break;
            }
        }

        return flyout;
    }
}
