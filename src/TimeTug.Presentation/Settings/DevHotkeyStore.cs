#if DEVFLAVOUR
namespace TimeTug.Presentation.Settings;

/// <summary>
/// A development and test aid (wired only in the <c>SELFTEST</c> configurations, like <c>dev-oauth.json</c>): a file <c>dev-hotkey.txt</c>
/// next to the other developer files (<c>%LOCALAPPDATA%\TimeTug</c>) holding one shortcut in its text form (<c>Ctrl+Alt+T</c>) sets the
/// popup shortcut without the Settings recorder (scripts and the checklist use it; delete the file when you record one by hand, or the file
/// wins again at the next start). A valid shortcut in the
/// file is the one the app registers at start; when the real store already holds the same shortcut its state (including a conflict found
/// at the last start) is kept; a file that is missing, empty or not a shortcut changes nothing. Saving always goes to the real store.
/// </summary>
public sealed class DevHotkeyStore(IHotkeyStore inner, string path, TimeProvider time) : IHotkeyStore
{
    public const string FileName = "dev-hotkey.txt";

    public HotkeyState Load()
    {
        var stored = inner.Load();
        return ReadFile() is { } wanted && stored.Binding != wanted
            ? new HotkeyState(wanted, HotkeyStatus.Registered, time.GetUtcNow())
            : stored;
    }

    public void Save(HotkeyState state) => inner.Save(state);

    private HotkeyBinding? ReadFile()
    {
        try
        {
            return File.Exists(path) && HotkeyBinding.TryParse(File.ReadAllText(path).Trim(), out var binding) && binding.Validate() == HotkeyReason.None
                ? binding
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
#endif
