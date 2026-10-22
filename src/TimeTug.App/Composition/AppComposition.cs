using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using TimeTug.App.Flyout;
using TimeTug.App.Settings;
using TimeTug.App.Tray;
using TimeTug.App.Tug;
using TimeTug.Engine;
using TimeTug.Platform;
using TimeTug.Platform.Auth;
using TimeTug.Platform.Clipboard;
using TimeTug.Platform.Diagnostics;
using TimeTug.Platform.Hotkey;
using TimeTug.Platform.OsState;
using TimeTug.Platform.Settings;
using TimeTug.Platform.Startup;
using TimeTug.Platform.Tray;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Diagnostics;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tug;
using TimeTug.Presentation.Welcome;
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
        services.AddSingleton(sp => new EngineClient(sp.GetRequiredService<Pkg.IEngine>(), sp.GetRequiredService<IDiagnostics>(), sp.GetRequiredService<ILocalizer>()));
        services.AddSingleton<IEngineClient>(sp => sp.GetRequiredService<EngineClient>());

        // ---- settings: the engine's (patches the engine persists) and the app's own presentation preferences -----------------
        services.AddSingleton<ISettingsClient>(sp => new SettingsClient(sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IPresentationSettingsStore>(sp => LocalSettingsPresentationStore.ForCurrentPackage(sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton(sp => new SettingsText(sp.GetRequiredService<ILocalizer>()));

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
        // The links the app chooses itself (Windows Settings, the Store, the website): not the meeting-link launcher above.
        services.AddSingleton<ISystemLauncher>(_ => new AppLinkLauncher());
        services.AddSingleton<IStartupTaskService>(_ => new StartupTaskService());
        services.AddTransient(sp => new LaunchAtLoginViewModel(
            sp.GetRequiredService<IStartupTaskService>(), sp.GetRequiredService<ISystemLauncher>(), sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILocalizer>()));

        // ---- the popup shortcut (nothing starts it yet: the shell calls HotkeyService.StartAsync once the engine's first state is in) ----
        services.AddSingleton<IHotkeyStore>(sp =>
        {
            IHotkeyStore store = LocalSettingsHotkeyStore.ForCurrentPackage(sp.GetRequiredService<IDiagnostics>());
#if SELFTEST
            // Development and CI builds only: dev-hotkey.txt sets a shortcut without the Settings recorder (the VM scripts and the checklist use it).
            store = new DevHotkeyStore(
                store, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTug", DevHotkeyStore.FileName), sp.GetRequiredService<TimeProvider>());
#endif
            return store;
        });
        services.AddSingleton(sp => new HotkeyRegistrar(sp.GetRequiredService<MessageWindow>(), sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton<IHotkeyRegistrar>(sp => sp.GetRequiredService<HotkeyRegistrar>());
        services.AddSingleton<IHotkeyControl>(sp => sp.GetRequiredService<HotkeyService>());
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
        // ---- the flyout (the shell opens it from the tray, a second launch and the shortcut; see FlyoutComposition) ------------
        services.AddSingleton<TrayIconIdSource>();
        services.AddSingleton<ITaskbarProbe>(_ => new WindowsTaskbarProbe());
        services.AddSingleton<ITrayAnchor>(sp => new TrayAnchor(
            () => sp.GetRequiredService<TrayIconIdSource>().Id, sp.GetRequiredService<ITaskbarProbe>(),
            sp.GetRequiredService<IMonitorService>(), sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton(sp => new FlyoutViewModel(
            sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<IUrlLauncher>(), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IMinuteTicker>(), sp.GetRequiredService<ILocalizer>()));
        services.AddSingleton<IFlyoutSurfaceFactory>(sp => new FlyoutSurfaceFactory(
            sp.GetRequiredService<FlyoutViewModel>(), sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILocalizer>(),
            sp.GetRequiredService<IUiSettings>()));
        services.AddSingleton(sp => new FlyoutHost(
            sp.GetRequiredService<IFlyoutSurfaceFactory>(), sp.GetRequiredService<ITugPresence>(), sp.GetRequiredService<IUiSettings>(),
            sp.GetRequiredService<ITrayAnchor>(), sp.GetRequiredService<IMonitorService>(), sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IDiagnostics>()));
        // ---- the Settings window (the tray's Settings row and the flyout's gear call SettingsHost.Show) ------------------------
        services.AddSingleton(sp => new SettingsPaneModels(
            tugRules: () => new TugRulesViewModel(
                sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<ISettingsClient>(), sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILocalizer>()),
            calendars: () => new CalendarsViewModel(
                sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<ISettingsClient>(), sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILocalizer>(),
                // Windows 1.0 registers no on-device text generator (WindowsEngineHost.TextGenerator is null); phase 8 changes this.
                inferenceSupported: false),
            accounts: () => new AccountsViewModel(
                sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILocalizer>(), sp.GetRequiredService<TimeProvider>()),
            diagnostics: () => new DiagnosticsViewModel(
                sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<IDiagnosticsReader>(), sp.GetRequiredService<IClipboardService>(),
                sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILocalizer>(), sp.GetRequiredService<TimeProvider>(), AppVersion.Current(), AppVersion.WindowsVersion()),
            general: () => new GeneralViewModel(
                sp.GetRequiredService<LaunchAtLoginViewModel>(),
                new HotkeyRecorderViewModel(sp.GetRequiredService<IHotkeyControl>(), sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILocalizer>()),
                new AboutViewModel(
                    AppVersion.Current(), AppVersion.BuildCommit(), IsDevelopmentBuild, sp.GetRequiredService<ISystemLauncher>(),
                    sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILocalizer>()))));
        services.AddSingleton<IClipboardService>(sp => new ClipboardService(sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton<IDiagnosticsReader>(_ => new DiagnosticsReader(RollingFileLog.DefaultDirectory()));
        services.AddSingleton(sp => new SettingsWindowFactory(
            sp.GetRequiredService<SettingsText>(), sp.GetRequiredService<ILocalizer>(), sp.GetRequiredService<IPresentationSettingsStore>(),
            sp.GetRequiredService<IMonitorService>(), sp.GetRequiredService<IUiSettings>(), sp.GetRequiredService<SettingsPaneModels>()));
        services.AddSingleton<ISettingsWindowFactory>(sp => sp.GetRequiredService<SettingsWindowFactory>());
        services.AddSingleton(sp => new SettingsHost(
            sp.GetRequiredService<ISettingsWindowFactory>(),
            () => new SettingsShellViewModel(
                sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<SettingsText>(), sp.GetRequiredService<IUiSettings>(),
                sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<TimeProvider>()),
            sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<IDiagnostics>()));
        services.AddSingleton(sp => new TugWindowHost(
            sp.GetRequiredService<TugController>(), sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<IUiSettings>(),
            sp.GetRequiredService<ILocalizer>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ISecondTicker>()));
        services.AddSingleton(sp => new AppRuntime(
            sp.GetRequiredService<IEngineClient>(), sp.GetRequiredService<TugController>(), sp.GetRequiredService<ISessionState>(),
            sp.GetRequiredService<INotificationState>(), sp.GetRequiredService<IMonitorService>(), sp.GetRequiredService<ILocalizer>(),
            sp.GetRequiredService<IDiagnostics>(), sp.GetRequiredService<TimeProvider>()));
        services.AddHostedService<AppRuntimeService>();
    }

    /// <summary>A development or CI build (the <c>SELFTEST</c> configurations): not in the Store, so the Software Update page shows its commit.</summary>
    private const bool IsDevelopmentBuild =
#if SELFTEST
        true;
#else
        false;
#endif

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
