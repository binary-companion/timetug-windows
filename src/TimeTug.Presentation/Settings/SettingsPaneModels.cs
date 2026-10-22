using TimeTug.Presentation.Diagnostics;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// Makes the view model of a pane when the Settings window first shows it. The window owns what it gets and disposes it when it closes,
/// so a closed window leaves no subscription to the engine behind.
/// </summary>
public sealed class SettingsPaneModels(
    Func<TugRulesViewModel> tugRules, Func<CalendarsViewModel> calendars, Func<AccountsViewModel> accounts, Func<DiagnosticsViewModel> diagnostics,
    Func<GeneralViewModel> general, Func<AppearanceViewModel> appearance)
{
    public AppearanceViewModel CreateAppearance() => appearance();

    public GeneralViewModel CreateGeneral() => general();

    public TugRulesViewModel CreateTugRules() => tugRules();

    public CalendarsViewModel CreateCalendars() => calendars();

    public AccountsViewModel CreateAccounts() => accounts();

    public DiagnosticsViewModel CreateDiagnostics() => diagnostics();
}
