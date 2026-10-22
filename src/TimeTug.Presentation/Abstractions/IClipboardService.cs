namespace TimeTug.Presentation.Abstractions;

/// <summary>Puts text on the system clipboard. Must be called on the UI thread.</summary>
public interface IClipboardService
{
    /// <summary>True when the text is on the clipboard; false when Windows refused (the clipboard can be held by another program for a moment).</summary>
    bool TrySetText(string text);
}
