using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.App.Settings;

/// <summary>The tile that shows a flyout backdrop: a miniature flyout with two invented sample cards, translucent over the wallpaper or flat.</summary>
internal sealed partial class BackdropPreview : UserControl
{
    public BackdropPreview(FlyoutBackdrop backdrop, ILocalizer text)
    {
        InitializeComponent();
        Title1.Text = text.GetString("Appearance_Preview_Card1Title");
        Details1.Text = text.GetString("Appearance_Preview_Card1Details");
        Chip.Text = text.GetString("Appearance_Preview_Card1Chip");
        Title2.Text = text.GetString("Appearance_Preview_Card2Title");
        Details2.Text = text.GetString("Appearance_Preview_Card2Details");
        var solid = backdrop == FlyoutBackdrop.Solid;
        AcrylicFill.Visibility = solid ? Visibility.Collapsed : Visibility.Visible;
        SolidFill.Visibility = solid ? Visibility.Visible : Visibility.Collapsed;
    }
}
