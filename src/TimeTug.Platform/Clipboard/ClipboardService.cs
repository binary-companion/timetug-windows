using System.Runtime.InteropServices;
using TimeTug.Presentation.Abstractions;
using Windows.ApplicationModel.DataTransfer;

namespace TimeTug.Platform.Clipboard;

/// <summary>The Windows clipboard. Called on the UI thread; another program can hold the clipboard for a moment, which shows as a refusal.</summary>
public sealed class ClipboardService(IDiagnostics? diagnostics = null) : IClipboardService
{
    public bool TrySetText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            diagnostics?.Record("warning", "clipboard", $"the clipboard refused the text ({ex.GetType().Name} 0x{ex.HResult:X8})");
            return false;
        }

        try
        {
            // Keeps the text on the clipboard if the app closes; it is already there while the app runs, so a refusal here is not a failure.
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            diagnostics?.Record("info", "clipboard", $"the clipboard could not be flushed ({ex.GetType().Name})");
        }

        return true;
    }
}
