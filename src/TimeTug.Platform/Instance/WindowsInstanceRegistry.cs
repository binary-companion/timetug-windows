using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace TimeTug.Platform.Instance;

/// <summary>
/// The Windows App SDK's <c>AppInstance</c>: <c>FindOrRegisterForKey</c> and <c>RedirectActivationToAsync</c>. Needs package identity (an
/// unpackaged process throws and the caller falls back to <see cref="MutexInstanceRegistry"/>).
/// </summary>
public sealed class WindowsInstanceRegistry : IInstanceRegistry
{
    public IInstanceRegistration FindOrRegister(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new Registration(AppInstance.FindOrRegisterForKey(key));
    }

    private sealed class Registration : IInstanceRegistration
    {
        private readonly AppInstance _instance;

        public Registration(AppInstance instance)
        {
            _instance = instance;
            if (instance.IsCurrent)
            {
                instance.Activated += OnActivated;
            }
        }

        public bool IsCurrent => _instance.IsCurrent;

        public event Action<AppActivation>? ActivationReceived;

        public async Task RedirectToAsync(AppActivation activation)
        {
            // The real activation is handed over as it came (the Start menu, the alias, a jump list item), and the primary reads what it
            // asks for from its arguments (OnActivated), so the request passed in is not needed here. The arguments are read here, on the
            // launching thread; the call itself must not block the UI thread, so it runs on the pool.
            var arguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            await Task.Run(() => _instance.RedirectActivationToAsync(arguments).AsTask()).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (!_instance.IsCurrent)
            {
                return;
            }

            _instance.Activated -= OnActivated;
            try
            {
                _instance.UnregisterKey();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // The key goes with the process anyway.
            }
        }

        /// <summary>A launch that carried <c>--jump word</c> asks for that action; every other launch asks for the flyout.</summary>
        private void OnActivated(object? sender, AppActivationArguments arguments) => ActivationReceived?.Invoke(Read(arguments));

        private static AppActivation Read(AppActivationArguments arguments)
        {
            try
            {
                if (arguments.Kind == ExtendedActivationKind.Launch && arguments.Data is ILaunchActivatedEventArgs launch)
                {
                    return ActivationParser.FromCommandLine(launch.Arguments);
                }
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidCastException or InvalidOperationException)
            {
                // The arguments could not be read: it is still a second launch.
            }

            return AppActivation.Flyout;
        }
    }
}
