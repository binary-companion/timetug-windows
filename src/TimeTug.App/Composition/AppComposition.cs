using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using TimeTug.App.Tug;
using TimeTug.Engine;
using TimeTug.Platform;
using TimeTug.Platform.Auth;
using TimeTug.Platform.Hotkey;
using TimeTug.Platform.OsState;
using TimeTug.Platform.Tray;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tug;
using Pkg = TimeTug.Engine;

namespace TimeTug.App.Composition;

/// <summary>
/// The composition root: the generic host that owns every service, registered over the interfaces the rest of the app depends
/// on. Nothing else in the app creates a service; a self-test replaces registrations (the last one wins) to script the engine
/// host and the OS state while the real graph is still resolved.
/// </summary>
internal static class AppComposition
{
    public static IHost CreateHost(DispatcherQueue queue, IDiagnostics diagnostics, Action<IServiceCollection>? replace = null)
    {
        // No configuration files, console logging or environment variables: the app has none of them.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, ContentRootPath = AppContext.BaseDirectory });
        Register(builder.Services, queue, diagnostics);
        replace?.Invoke(builder.Services);
        return builder.Build();
    }

    private static void Register(IServiceCollection services, DispatcherQueue queue, IDiagnostics diagnostics)
    {
        // ---- basics ---------------------------------------------------------------------------------------------
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(diagnostics);
        services.AddSingleton<IUiDispatcher>(new DispatcherQueueDispatcher(queue));
        services.AddSingleton<ILocalizer, ResourceLoaderLocalizer>();
        services.AddSingleton<ISecondTicker>(sp => new SecondTicker(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IMinuteTicker>(sp => new MinuteTicker(sp.GetRequiredService<TimeProvider>()));

        // ---- the engine -----------------------------------------------------------------------------------------
        services.AddSingleton(sp => new AccountFormService(queue, sp.GetRequiredService<ILocalizer>()));
        services.AddSingleton<IAccountFormService>(sp => sp.GetRequiredService<AccountFormService>());
        services.AddSingleton(sp => DefaultEngineStartup(sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton<IEngineHost>(sp => WindowsEngineHost.Create(
            new SystemBrowserLauncher(), sp.GetRequiredService<IAccountFormService>(), sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton<Pkg.IEngine>(sp =>
        {
            var startup = sp.GetRequiredService<EngineStartup>();
            return EngineFactory.Create(new EngineOptions
            {
                DataDirectory = startup.DataDirectory,
                Platform = "windows",
                OAuthClients = startup.OAuthClients,
                TimeZone = SystemEventsBridge.ReadWindowsTimeZoneAsIana(),
                Testing = startup.Testing,
                TestClockStart = startup.TestClockStart,
                Host = sp.GetRequiredService<IEngineHost>(),
            });
        });
        services.AddSingleton(sp => new EngineClient(sp.GetRequiredService<Pkg.IEngine>(), sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton<IEngineClient>(sp => sp.GetRequiredService<EngineClient>());

        // ---- OS state -------------------------------------------------------------------------------------------
        services.AddSingleton<MessageWindow>();
        services.AddSingleton<IWindowMessageSource>(sp => sp.GetRequiredService<MessageWindow>());
        services.AddSingleton<ISessionState>(sp => new SessionState(sp.GetRequiredService<IWindowMessageSource>(), diagnostics: sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton<IMonitorService>(sp => new MonitorService(sp.GetRequiredService<IWindowMessageSource>()));
        services.AddSingleton<IUiSettings>(sp => new UiSettings(sp.GetRequiredService<IWindowMessageSource>(), diagnostics: sp.GetRequiredService<IDiagnostics>()));
        // The notification state asks the toast service whether Windows lets the app show toasts; the toast service needs
        // the controller (for its buttons) and the controller needs both, so the controller is looked up only when a button is pressed.
        services.AddSingleton(sp => new ToastService(
            () => sp.GetRequiredService<TugController>(), sp.GetRequiredService<ILocalizer>(), sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton<IToastService>(sp => sp.GetRequiredService<ToastService>());
        services.AddSingleton<INotificationState>(sp => new NotificationState(
            sp.GetRequiredService<TimeProvider>(), toastsEnabled: () => sp.GetRequiredService<ToastService>().IsEnabled));
        services.AddSingleton<IUrlLauncher>(_ => new UrlLauncher());

        // ---- the popup shortcut (nothing starts it yet: the shell calls HotkeyService.StartAsync once the engine's first state is in) ----
        services.AddSingleton<IHotkeyStore>(sp => LocalSettingsHotkeyStore.ForCurrentPackage(sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton(sp => new HotkeyRegistrar(sp.GetRequiredService<MessageWindow>(), sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton<IHotkeyRegistrar>(sp => sp.GetRequiredService<HotkeyRegistrar>());
        services.AddSingleton(sp => new HotkeyService(
            sp.GetRequiredService<IHotkeyRegistrar>(), sp.GetRequiredService<IHotkeyStore>(), sp.GetRequiredService<ITugPresence>(),
            sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton<INetworkAvailability, NetworkAvailability>();
        services.AddSingleton(sp => new SystemEventsBridge(
            sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<IWindowMessageSource>(), sp.GetRequiredService<INetworkAvailability>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IDiagnostics>()));

        // ---- the tug and the app flow ---------------------------------------------------------------------------
        services.AddSingleton(sp => new TugController(
            sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<ISessionState>(), sp.GetRequiredService<INotificationState>(),
            sp.GetRequiredService<IMonitorService>(), sp.GetRequiredService<IToastService>(), sp.GetRequiredService<IUrlLauncher>(),
            sp.GetRequiredService<IDiagnostics>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ITugActions>(sp => sp.GetRequiredService<TugController>());
        services.AddSingleton(sp => new TugPresence(sp.GetRequiredService<TugController>()));
        services.AddSingleton<ITugPresence>(sp => sp.GetRequiredService<TugPresence>());
        services.AddSingleton(sp => new TugWindowHost(
            sp.GetRequiredService<TugController>(), sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<IUiSettings>(),
            sp.GetRequiredService<ILocalizer>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ISecondTicker>()));
        services.AddSingleton(sp => new AppRuntime(
            sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<TugController>(), sp.GetRequiredService<ISessionState>(),
            sp.GetRequiredService<INotificationState>(), sp.GetRequiredService<IMonitorService>(), sp.GetRequiredService<ILocalizer>(),
            sp.GetRequiredService<IDiagnostics>(), sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService<AppRuntimeService>();
    }

    private static EngineStartup DefaultEngineStartup(IDiagnostics diagnostics)
    {
        IReadOnlyDictionary<string, OAuthClient> clients = new Dictionary<string, OAuthClient>();
#if SELFTEST
        // Development and CI builds only: the ids come from a git-ignored file on this machine. A Release build offers
        // Google and Microsoft only once phase 9 injects the ids; until then those kinds show as not configured.
        clients = Presentation.Startup.DevOAuthFile.Load(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTug", Presentation.Startup.DevOAuthFile.FileName), diagnostics);
#endif
        _ = diagnostics;
        return new EngineStartup(EngineStartup.DefaultDataDirectory(), Testing: false, clients);
    }
}
