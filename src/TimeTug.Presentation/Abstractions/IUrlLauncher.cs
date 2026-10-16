namespace TimeTug.Presentation.Abstractions;

/// <summary>Opens a URL in the default handler.</summary>
public interface IUrlLauncher
{
    /// <summary>True when the URL was opened; false when it was refused or failed.</summary>
    ValueTask<bool> LaunchAsync(string url);
}
