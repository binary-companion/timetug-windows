#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TimeTug.App.SelfTest;

/// <summary>
/// Reads the menus that are open over a window and checks they are drawn in the theme of the window they belong to: a menu that kept the
/// application's theme under a window forced to the other one is the white menu on a dark window (or the reverse).
/// </summary>
internal static class MenuThemeCheck
{
    /// <summary>
    /// Records one check per open menu (and for the window): the menu's <see cref="FrameworkElement.ActualTheme"/> is the window's, and its text is
    /// the colour that theme gives (light text in the dark theme, dark text in the light one). Records a failure when no menu is open.
    /// </summary>
    public static void Follows(ScenarioRun run, FrameworkElement windowRoot, string label)
    {
        var menus = OpenMenus(windowRoot);
        run.Check(menus.Count > 0, $"{label}: a menu is open to look at");
        var windowTheme = windowRoot.ActualTheme;
        foreach (var menu in menus)
        {
            var text = FirstItemText(menu);
            run.Info($"{label}: {menu.GetType().Name} is {menu.ActualTheme} in a {windowTheme} window, item text {text?.Color}");
            run.Check(menu.ActualTheme == windowTheme, $"{label}: the menu is {menu.ActualTheme}, its window {windowTheme}");
            if (text is { } colour)
            {
                var light = ((0.2126 * colour.Color.R) + (0.7152 * colour.Color.G) + (0.0722 * colour.Color.B)) > 128;
                run.Check(light == (windowTheme == ElementTheme.Dark), $"{label}: the menu's text is {(light ? "light" : "dark")} in a {windowTheme} window");
            }
        }
    }

    /// <summary>The open menus of a window: the popups whose content is a menu presenter, or a combo box's drop-down.</summary>
    public static List<FrameworkElement> OpenMenus(FrameworkElement windowRoot)
    {
        var found = new List<FrameworkElement>();
        if (windowRoot.XamlRoot is not { } xamlRoot)
        {
            return found;
        }

        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
        {
            if (popup.Child is MenuFlyoutPresenter || (popup.Child is Canvas && Find<ComboBoxItem>(popup.Child) is not null))
            {
                found.Add((FrameworkElement)popup.Child);
            }
        }

        return found;
    }

    private static SolidColorBrush? FirstItemText(FrameworkElement menu) =>
        (Find<MenuFlyoutItem>(menu) as Control)?.Foreground as SolidColorBrush ?? (Find<ComboBoxItem>(menu) as Control)?.Foreground as SolidColorBrush;

    internal static T? Find<T>(DependencyObject node)
        where T : DependencyObject
    {
        if (node is T match)
        {
            return match;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (Find<T>(VisualTreeHelper.GetChild(node, i)) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }
}
#endif
