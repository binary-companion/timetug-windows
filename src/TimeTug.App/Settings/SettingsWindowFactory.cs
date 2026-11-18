using TimeTug.App.Theming;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Settings;

/// <summary>Adapts <see cref="SettingsWindow"/> to what <see cref="SettingsHost"/> needs.</summary>
internal sealed class SettingsWindowSurface : ISettingsWindow
{
    public SettingsWindowSurface(SettingsWindow window)
    {
        Window = window;
        window.WindowClosed += () => Closed?.Invoke();
    }

    public SettingsWindow Window { get; }

    public event Action? Closed;

    public void Show() => Window.ShowAndFocus();

    public void Close() => Window.Close();
}

/// <summary>Makes a Settings window around a shell, each time the window opens.</summary>
internal sealed class SettingsWindowFactory(
    SettingsText text, ILocalizer localizer, IPresentationSettingsStore store, IMonitorService monitors, IUiSettings ui,
    SettingsPaneModels models, ThemeService theme, BrokerParentWindow? brokerParent = null, AccountFormService? forms = null) : ISettingsWindowFactory
{
    /// <summary>How many windows have been made (for the self-test).</summary>
    public int Created { get; private set; }

    /// <summary>The window made last (for the self-test).</summary>
    public SettingsWindowSurface? Last { get; private set; }

    public ISettingsWindow Create(SettingsShellViewModel shell)
    {
        var window = new SettingsWindow(shell, text, localizer, store, monitors, ui, models, theme);
        if (brokerParent is not null)
        {
            // "Sign in again" and "Add" in Accounts open the Windows dialog in front of this window while it is open.
            var handle = window.Handle;
            brokerParent.Register(handle);
            window.WindowClosed += () => brokerParent.Unregister(handle);
            // The window the person last used is the one a returning browser sign-in brings forward.
            window.Activated += (_, args) =>
            {
                if (args.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated)
                {
                    brokerParent.Touch(handle);
                }
            };
        }

        if (forms is not null)
        {
            // The sign-in form (iCloud, Other (CalDAV)) opens over the window the person is using. Settings > Accounts asks for it too, so
            // this window takes the form root when it is activated and gives it back when it closes.
            Microsoft.UI.Xaml.XamlRoot? formsRoot = null;
            void TakeFormRoot()
            {
                if (window.RootElement.XamlRoot is { } root)
                {
                    formsRoot = root;
                    forms.XamlRoot = root;
                }
            }

            // The first activation can come before the content has a root, so the load takes it too.
            window.RootElement.Loaded += (_, _) => TakeFormRoot();
            window.Activated += (_, args) =>
            {
                if (args.WindowActivationState != Microsoft.UI.Xaml.WindowActivationState.Deactivated)
                {
                    TakeFormRoot();
                }
            };
            window.WindowClosed += () =>
            {
                if (formsRoot is not null)
                {
                    forms.Release(formsRoot);
                }
            };
        }

        var surface = new SettingsWindowSurface(window);
        Created++;
        Last = surface;
        return surface;
    }
}
