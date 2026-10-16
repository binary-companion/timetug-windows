using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TimeTug.App.Tug;
using TimeTug.Platform.OsState;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Startup;

namespace TimeTug.App.Composition;

/// <summary>
/// Starts the running app inside the host: toast buttons, the tug windows, then the engine (the runtime listens before it starts
/// the engine), then the system-event bridge. Stopping closes an outstanding takeover; the host then disposes the services, which
/// disposes the engine last of all.
/// </summary>
internal sealed class AppRuntimeService(AppRuntime runtime, IToastService toasts, TugWindowHost windows, IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        (toasts as ToastService)?.Start();
        windows.Start();
        await runtime.StartAsync();

        // The bridge sends system events to a started engine, so it is created after it.
        _ = services.GetRequiredService<SystemEventsBridge>();
    }

    public async Task StopAsync(CancellationToken cancellationToken) => await runtime.QuitAsync();
}
