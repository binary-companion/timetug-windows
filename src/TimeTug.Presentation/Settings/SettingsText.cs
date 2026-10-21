using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// The visible label of each settings control, shared by the pages and the search catalog so they cannot drift (the Mac's
/// <c>SettingsText</c>). Everything comes from <c>Resources.resw</c>; the keys are <c>SettingsPane_*</c> for panes and
/// <c>SettingsText_&lt;id&gt;</c> for controls, where the id is the search catalog's.
/// </summary>
public sealed class SettingsText(ILocalizer text)
{
    public string PaneTitle(SettingsPane pane) => text.GetString("SettingsPane_" + pane);

    /// <summary>The title of the control with this search id.</summary>
    public string ControlTitle(string id) => text.GetString("SettingsText_" + id);

    /// <summary>
    /// The words, besides the title, that should find the control with this search id: a semicolon-separated list in the resource file, so a
    /// translation can use the words people in that language use. Empty when the file lists none.
    /// </summary>
    public IReadOnlyList<string> Keywords(string id) =>
        text.TryGetString("SettingsKeywords_" + id)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    public string NoResults => text.GetString("Settings_NoResults");

    public string Get(string key) => text.GetString(key);
}
