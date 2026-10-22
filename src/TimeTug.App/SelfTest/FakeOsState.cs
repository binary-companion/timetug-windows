#if SELFTEST
using Microsoft.UI.Windowing;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.App.SelfTest;

// Stand-ins for the OS state the tug reads, so a scenario can lock the session, remove a monitor or switch on a contrast theme
// on a hosted runner that can do none of those things.

internal sealed class FakeSession : ISessionState
{
    public bool IsLocked { get; set; }

    public event Action? Locked;

    public event Action? Unlocked;

    public void Lock()
    {
        IsLocked = true;
        Locked?.Invoke();
    }

    public void Unlock()
    {
        IsLocked = false;
        Unlocked?.Invoke();
    }
}

internal sealed class FakeNotifications : INotificationState
{
    public bool CanShowFullScreen { get; set; } = true;

    public bool ToastsAllowed { get; set; } = true;

    public event Action? FullScreenEnded;

    public void SetWatchingForFullScreenEnd(bool watching)
    {
    }

    public void EndFullScreen()
    {
        CanShowFullScreen = true;
        FullScreenEnded?.Invoke();
    }
}

internal sealed class FakeMonitors : IMonitorService
{
    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = [];

    public MonitorInfo Primary => Monitors.First(m => m.IsPrimary);

    public event Action? Changed;

    public void Set(params MonitorInfo[] monitors)
    {
        Monitors = monitors;
        Changed?.Invoke();
    }

    /// <summary>
    /// Two monitors for a scenario: the real ones when the machine has two, otherwise the real main display and a smaller
    /// invented one inside it (a hosted runner has one display). Windows on an inner "monitor" overlap the main one, but each is
    /// still captured on its own.
    /// </summary>
    public static (MonitorInfo Main, MonitorInfo Second) Layout()
    {
        DisplayArea? main = null;
        DisplayArea? other = null;
        var areas = DisplayArea.FindAll();
        for (var i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            if (area.IsPrimary)
            {
                main ??= area;
            }
            else
            {
                other ??= area;
            }
        }

        var mainBounds = (main ?? DisplayArea.Primary).OuterBounds;
        var first = new MonitorInfo("selftest-1", mainBounds.X, mainBounds.Y, mainBounds.Width, mainBounds.Height, true);
        if (other is not null)
        {
            var b = other.OuterBounds;
            return (first, new MonitorInfo("selftest-2", b.X, b.Y, b.Width, b.Height, false));
        }

        var width = mainBounds.Width * 3 / 4;
        var height = mainBounds.Height * 3 / 4;
        return (first, new MonitorInfo("selftest-2", mainBounds.X + (mainBounds.Width / 8), mainBounds.Y + (mainBounds.Height / 8), width, height, false));
    }
}

internal sealed class FakeToasts : IToastService
{
    public bool Accept { get; set; } = true;

    public List<string> Shown { get; } = [];

    public ValueTask<bool> ShowAsync(TakeoverRequest request)
    {
        Shown.Add(request.EventId);
        return ValueTask.FromResult(Accept);
    }
}

internal sealed class FakeLauncher : IUrlLauncher
{
    public bool Succeeds { get; set; } = true;

    public List<string> Opened { get; } = [];

    public ValueTask<bool> LaunchAsync(string url)
    {
        Opened.Add(url);
        return ValueTask.FromResult(Succeeds);
    }
}

internal sealed class FakeUiSettings : IUiSettings
{
    public bool AnimationsEnabled { get; set; } = true;

    public bool TransparencyEffectsEnabled { get; set; } = true;

    public bool HighContrast { get; set; }

    public bool TaskbarUsesLightTheme { get; set; }

    public bool AppsUseDarkTheme { get; set; }

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}

internal sealed class RecordingDiagnostics : IDiagnostics
{
    public List<(string Level, string Category, string Message)> Entries { get; } = [];

    public void Record(string level, string category, string message) => Entries.Add((level, category, message));
}
#endif
