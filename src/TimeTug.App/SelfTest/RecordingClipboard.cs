#if SELFTEST
using TimeTug.Platform.Clipboard;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.App.SelfTest;

/// <summary>
/// The clipboard for a scenario: it keeps what was copied (the scenario reads it) and also hands it to the real Windows clipboard, noting whether
/// Windows took it. Whether the real clipboard works depends on the session the scenario runs in, so that is a note, not a check.
/// </summary>
internal sealed class RecordingClipboard : IClipboardService
{
    private readonly ClipboardService _real = new();

    public List<string> Texts { get; } = [];

    /// <summary>True, false, or null before the first copy: whether the real clipboard accepted the text.</summary>
    public bool? RealAccepted { get; private set; }

    public bool TrySetText(string text)
    {
        Texts.Add(text);
        RealAccepted = _real.TrySetText(text);
        return true;
    }
}
#endif
