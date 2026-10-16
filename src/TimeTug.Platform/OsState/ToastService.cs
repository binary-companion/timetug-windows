using System.Security.Cryptography;
using System.Text;
using Microsoft.Windows.AppNotifications;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tug;

namespace TimeTug.Platform.OsState;

/// <summary>A toast activation: the button's arguments and the values of its inputs.</summary>
public sealed record ToastActivation(IReadOnlyDictionary<string, string> Arguments, IReadOnlyDictionary<string, string> UserInput);

/// <summary>The part of Windows' toast machinery the service needs (replaced by a fake in tests).</summary>
public interface IAppNotifier : IDisposable
{
    /// <summary>False when notifications are off for the app, the user or by policy.</summary>
    bool IsEnabled { get; }

    event Action<ToastActivation>? Activated;

    /// <summary>Registers for activations. Must be called once at startup, before the first <see cref="Show"/>.</summary>
    void Register();

    /// <summary>Shows the toast; true when Windows accepted it (it may still be held back by Do Not Disturb).</summary>
    bool Show(string xml, string tag, string group);
}

/// <summary>The real thing: <c>AppNotificationManager</c> from the Windows App SDK. Needs package identity.</summary>
public sealed class WindowsAppNotifier : IAppNotifier
{
    private bool _registered;

    public bool IsEnabled => AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled;

    public event Action<ToastActivation>? Activated;

    public void Register()
    {
        AppNotificationManager.Default.NotificationInvoked += OnInvoked;
        AppNotificationManager.Default.Register();
        _registered = true;
    }

    public bool Show(string xml, string tag, string group)
    {
        var notification = new AppNotification(xml) { Tag = tag, Group = group };
        AppNotificationManager.Default.Show(notification);
        return notification.Id != 0;
    }

    public void Dispose()
    {
        if (_registered)
        {
            _registered = false;
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            AppNotificationManager.Default.Unregister();
        }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        Activated?.Invoke(new ToastActivation(
            new Dictionary<string, string>(args.Arguments),
            new Dictionary<string, string>(args.UserInput)));
}

/// <summary>
/// Shows a takeover as a toast with Join, Snooze and Dismiss, and routes the buttons to the tug controller. Returns
/// true only when Windows accepted the toast: the controller reports "presented, not on screen" on that basis alone.
/// </summary>
public sealed class ToastService : IToastService, IDisposable
{
    private readonly Func<ITugActions> _actions;
    private readonly ILocalizer _localizer;
    private readonly IDiagnostics _diagnostics;
    private readonly IAppNotifier _notifier;

    /// <param name="actions">Resolved when a button is pressed, not at construction: the tug controller needs this service, so it cannot be passed in directly.</param>
    public ToastService(Func<ITugActions> actions, ILocalizer localizer, IDiagnostics diagnostics, IAppNotifier? notifier = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _actions = actions;
        _localizer = localizer;
        _diagnostics = diagnostics;
        _notifier = notifier ?? new WindowsAppNotifier();
        _notifier.Activated += OnActivated;
    }

    /// <summary>Whether Windows allows notifications for the app (for <see cref="NotificationState"/>).</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                return _notifier.IsEnabled;
            }
            catch (Exception e) when (IsPlatformFailure(e))
            {
                return true;
            }
        }
    }

    /// <summary>Registers for button presses. Call once at startup; a failure is recorded and toasts can still be shown.</summary>
    public void Start()
    {
        try
        {
            _notifier.Register();
        }
        catch (Exception e) when (IsPlatformFailure(e))
        {
            _diagnostics.Record("error", "toast", $"toast buttons could not be registered ({e.GetType().Name} 0x{e.HResult:X8})");
        }
    }

    public ValueTask<bool> ShowAsync(TakeoverRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            if (!_notifier.IsEnabled)
            {
                _diagnostics.Record("warning", "toast", "notifications are turned off for TimeTug in Windows");
                return ValueTask.FromResult(false);
            }

            var accepted = _notifier.Show(ToastXml.Build(request, _localizer), TagFor(request.EventId), ToastXml.Group);
            if (!accepted)
            {
                _diagnostics.Record("warning", "toast", "Windows did not accept the toast");
            }

            return ValueTask.FromResult(accepted);
        }
        catch (Exception e) when (IsPlatformFailure(e))
        {
            _diagnostics.Record("error", "toast", $"the toast could not be shown ({e.GetType().Name})");
            return ValueTask.FromResult(false);
        }
    }

    public void Dispose()
    {
        _notifier.Activated -= OnActivated;
        _notifier.Dispose();
    }

    /// <summary>Toast tags are limited to 64 characters; calendar ids can be longer, so use a digest.</summary>
    private static string TagFor(string eventId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventId)))[..32];

    private static bool IsPlatformFailure(Exception e) =>
        e is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException;

    private void OnActivated(ToastActivation activation)
    {
        var action = ToastXml.Parse(activation.Arguments, activation.UserInput);
        if (action is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                switch (action.Kind)
                {
                    case ToastXml.JoinKind:
                        await _actions().JoinAsync(action.EventId);
                        break;
                    case ToastXml.SnoozeKind:
                        await _actions().SnoozeAsync(action.EventId, action.Snooze!.Value);
                        break;
                    default:
                        await _actions().DismissAsync(action.EventId);
                        break;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _diagnostics.Record("error", "toast", $"a toast button could not be handled ({e.GetType().Name})");
            }
        });
    }
}
