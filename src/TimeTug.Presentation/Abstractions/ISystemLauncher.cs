namespace TimeTug.Presentation.Abstractions;

/// <summary>
/// Opens the few links the app itself chooses (the Startup apps page of Windows Settings, the Microsoft Store's updates page, the
/// website and the licence). It is a different launcher from the one for meeting links, which never opens a <c>ms-settings:</c> link
/// that arrived in an event.
/// </summary>
public interface ISystemLauncher : IUrlLauncher
{
}
