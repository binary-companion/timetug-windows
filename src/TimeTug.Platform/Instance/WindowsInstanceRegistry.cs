using Microsoft.Windows.AppLifecycle;

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

        public event Action<ActivationKind>? ActivationReceived;

        public async Task RedirectToAsync(ActivationKind kind)
        {
            // Every launch asks for the flyout, whatever the launch was (the Start menu, the alias, a file association later). The
            // arguments are read here, on the launching thread; the call itself must not block the UI thread, so it runs on the pool.
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

        private void OnActivated(object? sender, AppActivationArguments arguments) => ActivationReceived?.Invoke(ActivationKind.Flyout);
    }
}
