namespace TimeTug.App.Tray;

/// <summary>
/// Tells the flyout's placement which icon to look for. The icon is created by the shell after the services exist, so the shell
/// sets <see cref="Provider"/> when it creates the icon; until then (and after the icon is gone) there is no identifier.
/// </summary>
internal sealed class TrayIconIdSource
{
    private volatile Func<Guid?>? _provider;

    public Func<Guid?>? Provider
    {
        get => _provider;
        set => _provider = value;
    }

    public Guid? Id => _provider?.Invoke();
}
