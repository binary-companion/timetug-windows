using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TimeTug.Presentation.Settings;
using Windows.UI;

namespace TimeTug.App.Settings;

/// <summary>
/// The tile that shows a theme: a miniature Settings window on a wallpaper in the light theme, the dark theme, or (Use system) the light one
/// with the dark one over its right half. The colours are fixed: the tile shows the option, whatever theme the pane is in now.
/// </summary>
internal sealed partial class ThemePreview : UserControl
{
    private const double StageWidth = 152;
    private const double StageHeight = 96;

    public ThemePreview(AppTheme theme)
    {
        InitializeComponent();
        switch (theme)
        {
            case AppTheme.Light:
                Stage.Children.Add(Desktop(dark: false));
                break;
            case AppTheme.Dark:
                Stage.Children.Add(Desktop(dark: true));
                break;
            default:
                Stage.Children.Add(Desktop(dark: false));
                var right = Desktop(dark: true);
                right.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(StageWidth / 2, 0, StageWidth / 2, StageHeight) };
                Stage.Children.Add(right);
                break;
        }
    }

    private static Grid Desktop(bool dark)
    {
        var grid = new Grid { Width = StageWidth, Height = StageHeight };
        grid.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Offset = 0, Color = dark ? Rgb(64, 41, 153) : Rgb(166, 222, 240) },
                    new GradientStop { Offset = 1, Color = dark ? Rgb(5, 13, 48) : Rgb(5, 82, 166) },
                },
            },
        });

        // The settings window: title bar, a navigation column and a few cards.
        var window = new Grid
        {
            Width = 112, Height = 70, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 14, 0, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brush(dark ? Rgb(32, 32, 32) : Rgb(243, 243, 243)),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(dark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(160, 255, 255, 255)),
        };
        window.RowDefinitions.Add(new RowDefinition { Height = new GridLength(11) });
        window.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var ink = dark ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(120, 0, 0, 0);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(Capsule(14, 2, ink));
        bar.Children.Add(Capsule(8, 2, ink));
        window.Children.Add(bar);
        var body = new Grid { Margin = new Thickness(4, 0, 4, 4) };
        Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var nav = new StackPanel { Spacing = 3, Margin = new Thickness(0, 2, 4, 0) };
        nav.Children.Add(new Border { Height = 7, CornerRadius = new CornerRadius(2), Background = Brush(Rgb(0, 120, 212)) });
        nav.Children.Add(Capsule(18, 2, ink));
        nav.Children.Add(Capsule(14, 2, ink));
        body.Children.Add(nav);
        var cards = new StackPanel { Spacing = 3, Margin = new Thickness(0, 2, 0, 0) };
        Grid.SetColumn(cards, 1);
        for (var i = 0; i < 3; i++)
        {
            cards.Children.Add(new Border
            {
                Height = 11, CornerRadius = new CornerRadius(3), Background = Brush(dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(180, 255, 255, 255)),
                BorderThickness = new Thickness(1), BorderBrush = Brush(dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0)),
            });
        }

        body.Children.Add(cards);
        window.Children.Add(body);
        grid.Children.Add(window);
        return grid;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    private static SolidColorBrush Brush(Color color) => new(color);

    private static Border Capsule(double width, double height, Color color) =>
        new() { Width = width, Height = height, CornerRadius = new CornerRadius(height / 2), Background = Brush(color), HorizontalAlignment = HorizontalAlignment.Left };
}
