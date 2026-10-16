using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TimeTug.Presentation.Tug;
using Windows.UI;

namespace TimeTug.App.Tug;

/// <summary>
/// The backdrop of a tug window: desktop acrylic (a live blur of what is behind the window) with a navy tint. The controller
/// is told its window is always active, because a mirror never is and a tug shown without the keyboard must not fall back to a
/// flat colour. When Windows has transparency effects off the controller itself draws its fallback colour, solid navy.
/// </summary>
#pragma warning disable CA1001 // The controller is disposed when the backdrop is disconnected from its window (OnTargetDisconnected).
internal sealed class TugBackdrop : SystemBackdrop
#pragma warning restore CA1001
{
    private static readonly Color Navy = Color.FromArgb(255, TugBrand.Navy.R, TugBrand.Navy.G, TugBrand.Navy.B);

    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;

    /// <summary>False where desktop acrylic cannot be used at all; the window then paints solid navy itself.</summary>
    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _configuration = new SystemBackdropConfiguration { IsInputActive = true, Theme = SystemBackdropTheme.Dark };
        _controller = new DesktopAcrylicController
        {
            TintColor = Navy,
            TintOpacity = (float)TugBrand.AcrylicTintOpacity,
            LuminosityOpacity = 0.25f,
            FallbackColor = Navy,
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
