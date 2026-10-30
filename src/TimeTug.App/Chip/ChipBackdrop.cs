using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TimeTug.App.Chip;

/// <summary>
/// The chip's acrylic: desktop acrylic (a live blur of what is behind the window) with a tint of the theme's own colour. The built-in
/// <c>DesktopAcrylicBackdrop</c> and <c>MicaBackdrop</c> show only a flat fallback colour on a window that is never active (measured in
/// docs/spikes/2026-10-chip-window-and-jumplist.md), so the controller is told its window is always active, as the tug's backdrop does. The tint is
/// explicit because the configuration's theme made no difference to the colour; when Windows has transparency effects off the controller draws its
/// fallback colour, the same flat colour as the tint.
/// </summary>
#pragma warning disable CA1001 // The controller is disposed when the backdrop is disconnected from its window (OnTargetDisconnected).
internal sealed class ChipBackdrop(bool dark) : SystemBackdrop
#pragma warning restore CA1001
{
    private static readonly Color DarkTint = Color.FromArgb(255, 32, 32, 32);
    private static readonly Color LightTint = Color.FromArgb(255, 243, 243, 243);

    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;

    /// <summary>False where desktop acrylic cannot be used at all; the window then paints a flat fill itself.</summary>
    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    /// <summary>True for the dark tint (the backdrop is made again when the theme changes).</summary>
    public bool Dark { get; } = dark;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        var tint = Dark ? DarkTint : LightTint;
        _configuration = new SystemBackdropConfiguration { IsInputActive = true, Theme = Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light };
        _controller = new DesktopAcrylicController
        {
            TintColor = tint,
            TintOpacity = 0.7f,
            LuminosityOpacity = 0.5f,
            FallbackColor = tint,
        };
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
    }
}
