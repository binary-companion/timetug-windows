using Microsoft.UI.Dispatching;
using TimeTug.App.Views;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App;

/// <summary>Builds the welcome window for an engine client. The startup flow (task 9) calls this when <see cref="WelcomeViewModel.ShouldShowAtStartup"/> is true.</summary>
public static class WelcomeWindowFactory
{
    /// <summary>Must be called on the UI thread.</summary>
    public static WelcomeWindow Create(IEngineClient engine, AccountFormService forms, ILocalizer text, TimeProvider? clock = null, Action? openAccountsPane = null,
        LaunchAtLoginViewModel? launchAtLogin = null)
    {
        var dispatcher = new DispatcherQueueDispatcher(DispatcherQueue.GetForCurrentThread());
        return new WelcomeWindow(new WelcomeViewModel(engine, dispatcher, text, clock, openAccountsPane, launchAtLogin), forms, text);
    }
}
