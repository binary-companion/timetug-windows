using Microsoft.UI.Dispatching;
using TimeTug.App.Theming;
using TimeTug.App.Views;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App;

/// <summary>Builds the welcome window for an engine client. The startup flow (task 9) calls this when <see cref="WelcomeViewModel.ShouldShowAtStartup"/> is true.</summary>
public static class WelcomeWindowFactory
{
    /// <summary>Must be called on the UI thread.</summary>
    public static WelcomeWindow Create(IEngineClient engine, AccountFormService forms, ILocalizer text, TimeProvider? clock = null, Action? openAccountsPane = null,
        LaunchAtLoginViewModel? launchAtLogin = null, ThemeService? theme = null,
        BrokerParentWindow? brokerParent = null, IBrokerSignInIntent? brokerIntents = null, IWindowsAccountSource? windowsAccounts = null,
        IWindowsAccountDismissalStore? dismissals = null, TaskbarSettingsViewModel? taskbar = null, IUiSettings? uiSettings = null,
        ISettingsClient? settings = null)
    {
        var dispatcher = new DispatcherQueueDispatcher(DispatcherQueue.GetForCurrentThread());
        // The "You're connected" step lists the calendars the way Settings > Calendars does and saves each choice at once through the same client; without a client the step has no list.
        // Windows 1.0 registers no on-device text generator, so the model is told so (nothing here shows or sends it). The window owns the model and disposes it; the client is the app's.
        var calendars = settings is null ? null : new CalendarsViewModel(engine, settings, dispatcher, text, inferenceSupported: false, showAccountProblems: true);
        var model = new WelcomeViewModel(engine, dispatcher, text, clock, openAccountsPane, launchAtLogin, taskbar, calendars);
        // The Windows account broker (when this build has one): what the person chose reaches the broker host, and Continue can add the Windows account unless the
        // person removed it before. The store is set first: it decides whether the listing is asked for at all.
        model.Accounts.BrokerIntents = brokerIntents;
        model.Accounts.Dismissals = dismissals;
        model.Accounts.WindowsAccounts = windowsAccounts;
        return new WelcomeWindow(model, forms, text, theme, brokerParent, uiSettings);
    }
}
