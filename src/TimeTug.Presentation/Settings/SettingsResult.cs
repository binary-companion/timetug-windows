namespace TimeTug.Presentation.Settings;

/// <summary>How one settings patch came out. After <see cref="Rejected"/> the control shows the engine's last published value.</summary>
public sealed record SettingsResult
{
    private SettingsResult(bool isApplied, string? reason)
    {
        IsApplied = isApplied;
        Reason = reason;
    }

    /// <summary>The engine took the patch (or the patch changed nothing) and published a state.</summary>
    public static SettingsResult Applied { get; } = new(true, null);

    public bool IsApplied { get; }

    /// <summary>Why the patch was not taken: a short English detail for diagnostics (the engine's own message, or that it did not answer). The pane shows its own localized text. Null when applied.</summary>
    public string? Reason { get; }

    /// <summary>The engine refused the patch, or did not answer in time.</summary>
    public static SettingsResult Rejected(string reason) => new(false, reason);
}
