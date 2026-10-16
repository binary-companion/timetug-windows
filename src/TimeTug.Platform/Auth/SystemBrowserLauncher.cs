using Windows.System;

namespace TimeTug.Platform.Auth;

/// <summary>Opens http(s) URLs in the default browser.</summary>
public sealed class SystemBrowserLauncher : Presentation.Abstractions.IUrlLauncher
{
    public async ValueTask<bool> LaunchAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        try
        {
            return await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
