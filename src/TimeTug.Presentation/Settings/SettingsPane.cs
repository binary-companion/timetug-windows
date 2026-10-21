namespace TimeTug.Presentation.Settings;

/// <summary>The Settings window's panes, in the Mac's order.</summary>
public enum SettingsPane
{
    General,
    Appearance,
    Accounts,
    Calendars,
    TugRules,
}

/// <summary>A drill-down destination reachable from the General hub (the Mac's <c>generalPath</c>).</summary>
public enum GeneralDestination
{
    About,
    SoftwareUpdate,
}
