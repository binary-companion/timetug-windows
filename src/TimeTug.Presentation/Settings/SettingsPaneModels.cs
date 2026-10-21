namespace TimeTug.Presentation.Settings;

/// <summary>
/// Makes the view model of a pane when the Settings window first shows it. The window owns what it gets and disposes it when it closes,
/// so a closed window leaves no subscription to the engine behind.
/// </summary>
public sealed class SettingsPaneModels(Func<TugRulesViewModel> tugRules)
{
    public TugRulesViewModel CreateTugRules() => tugRules();
}
