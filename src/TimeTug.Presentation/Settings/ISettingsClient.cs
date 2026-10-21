namespace TimeTug.Presentation.Settings;

/// <summary>
/// Edits the engine's settings. The engine persists them and publishes the result in its next state; the app keeps no copy of its own,
/// so every control binds to the engine's last published settings and this interface only asks for changes.
/// </summary>
public interface ISettingsClient
{
    /// <summary>
    /// Sends one atomic <c>settings.apply</c> patch carrying only the fields <paramref name="edit"/> sets. Calls are taken one at a time:
    /// an edit runs only after the previous patch has been answered, so it always starts from what the engine last published (two quick
    /// edits to the tug list cannot lose the first). A patch that changes nothing is not sent.
    /// </summary>
    ValueTask<SettingsResult> ApplyAsync(Func<EngineSettingsPatch, EngineSettingsPatch> edit);

    /// <summary>Asks the engine to forget what on-device duplicate finding has learned (<c>dedup.forget</c>).</summary>
    ValueTask ForgetLearnedCorrectionsAsync();
}
