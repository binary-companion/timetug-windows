using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tray;
using Windows.UI;

namespace TimeTug.App.Settings;

/// <summary>
/// The tile that shows a tray icon style: a cropped desktop with the right end of the taskbar. The icon is the real one, drawn by the same
/// <see cref="TrayIconRenderer"/> as the tray (the colour puppy, with the sample minute digit for the digit style), so the preview cannot
/// differ from what the tray will show. The strip follows the taskbar theme (light or dark), not the app theme.
/// </summary>
internal sealed partial class TrayPreview : UserControl
{
    /// <summary>The digit drawn in the preview: invented, not a real meeting.</summary>
    private const int SampleDigit = 7;

    private readonly TrayIconStyle _style;
    private readonly TrayIconRenderer? _renderer;
    private readonly string _clock;

    public TrayPreview(TrayIconStyle style, TrayIconRenderer? renderer, string clock)
    {
        _style = style;
        _renderer = renderer;
        _clock = clock;
        InitializeComponent();
        Clock.Text = clock;
    }

    /// <summary>The icon picture now on the strip (for the self-test).</summary>
    public Microsoft.UI.Xaml.Media.ImageSource? Shown => Icon.Source;

    /// <summary>Draws the strip for the taskbar theme and the icon on it.</summary>
    public void Update(bool lightTaskbar)
    {
        var ink = lightTaskbar ? Color.FromArgb(255, 26, 26, 26) : Color.FromArgb(255, 255, 255, 255);
        Bar.Background = new SolidColorBrush(lightTaskbar ? Color.FromArgb(235, 243, 243, 243) : Color.FromArgb(235, 32, 32, 32));
        var brush = new SolidColorBrush(ink);
        Chevron.Foreground = brush;
        Wifi.Foreground = brush;
        Volume.Foreground = brush;
        Clock.Foreground = brush;
        Icon.Source = Picture(lightTaskbar);
    }

    private WriteableBitmap? Picture(bool lightTaskbar)
    {
        if (_renderer is null)
        {
            return null;
        }

        try
        {
            var image = _renderer.Render(TrayIconState.Soon, _style == TrayIconStyle.IconWithMinuteDigit ? SampleDigit : null, lightTaskbar, 32);
            var bitmap = new WriteableBitmap(image.Width, image.Height);
            var rgba = image.Rgba;
            using var stream = bitmap.PixelBuffer.AsStream();
            var bgra = new byte[rgba.Length];
            for (var i = 0; i < rgba.Length; i += 4)
            {
                // The bitmap wants blue, green, red and alpha with the colour already multiplied by the alpha.
                var alpha = rgba[i + 3];
                bgra[i] = (byte)((rgba[i + 2] * alpha + 127) / 255);
                bgra[i + 1] = (byte)((rgba[i + 1] * alpha + 127) / 255);
                bgra[i + 2] = (byte)((rgba[i] * alpha + 127) / 255);
                bgra[i + 3] = alpha;
            }

            stream.Write(bgra, 0, bgra.Length);
            bitmap.Invalidate();
            return bitmap;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
        {
            // The picture files are missing: the strip is shown without the icon (the real tray reports this itself).
            return null;
        }
    }
}
