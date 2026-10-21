using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace TimeTug.App.Settings;

/// <summary>
/// The tint behind a control that a search result revealed (the Mac's <c>settingsHighlight</c>). A pane marks the <see cref="Grid"/> around a
/// control with <see cref="IdProperty"/> (the search catalog's id); <see cref="Apply"/> draws a soft accent halo behind the one whose id matches and removes
/// it from the rest. The halo fades in and out unless Windows asks for reduced motion, and the window scrolls to the control.
/// </summary>
internal static class SettingsHighlight
{
    public static readonly DependencyProperty IdProperty =
        DependencyProperty.RegisterAttached("Id", typeof(string), typeof(SettingsHighlight), new PropertyMetadata(null));

    private static readonly DependencyProperty OverlayProperty =
        DependencyProperty.RegisterAttached("Overlay", typeof(Border), typeof(SettingsHighlight), new PropertyMetadata(null));

    /// <summary>How strong the tint is at its fullest.</summary>
    public const double Strength = 0.22;

    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(250);

    public static string? GetId(DependencyObject element) => (string?)element.GetValue(IdProperty);

    public static void SetId(DependencyObject element, string? value) => element.SetValue(IdProperty, value);

    /// <summary>The halo behind the control with this id, or null when it has none now (for the self-test).</summary>
    public static Border? OverlayOf(Grid host) => host.GetValue(OverlayProperty) as Border;

    /// <summary>Every marked control under <paramref name="root"/>.</summary>
    public static IEnumerable<Grid> Hosts(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Grid grid && (GetId(grid) is not null || grid.GetValue(OverlayProperty) is not null))
            {
                yield return grid;
            }

            foreach (var nested in Hosts(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Tints the control whose id is <paramref name="id"/> (none when null) and clears the others.</summary>
    public static void Apply(DependencyObject root, string? id, bool animate)
    {
        foreach (var host in Hosts(root))
        {
            Set(host, id is not null && GetId(host) == id, animate);
        }
    }

    /// <summary>Brings the control with this id into view.</summary>
    public static void BringIntoView(DependencyObject root, string id, bool animate)
    {
        foreach (var host in Hosts(root))
        {
            if (GetId(host) == id)
            {
                host.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = animate });
                return;
            }
        }
    }

    private static void Set(Grid host, bool on, bool animate)
    {
        var overlay = host.GetValue(OverlayProperty) as Border;
        if (!on && overlay is null)
        {
            return;
        }

        if (overlay is null)
        {
            overlay = new Border
            {
                Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(-4),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(overlay, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            host.Children.Insert(0, overlay);
            host.SetValue(OverlayProperty, overlay);
        }

        var target = on ? Strength : 0;
        if (!animate)
        {
            overlay.Opacity = target;
            return;
        }

        var fade = new DoubleAnimation { To = target, Duration = FadeDuration, EnableDependentAnimation = false };
        Storyboard.SetTarget(fade, overlay);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var board = new Storyboard();
        board.Children.Add(fade);
        board.Begin();
    }
}
