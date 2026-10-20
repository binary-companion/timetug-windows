using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.OsState;

/// <summary>
/// The connected displays, enumerated with <c>EnumDisplayMonitors</c> and refreshed on <c>WM_DISPLAYCHANGE</c>.
/// <see cref="Changed"/> is raised only when the layout actually differs from the last one.
/// </summary>
public sealed unsafe class MonitorService : IMonitorService, IDisposable
{
    private readonly IWindowMessageSource _source;
    private readonly Func<IReadOnlyList<MonitorInfo>> _enumerate;
    private IReadOnlyList<MonitorInfo> _monitors;

    public MonitorService(IWindowMessageSource source, Func<IReadOnlyList<MonitorInfo>>? enumerate = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _enumerate = enumerate ?? Enumerate;
        _monitors = _enumerate();
        source.MessageReceived += OnMessage;
    }

    public IReadOnlyList<MonitorInfo> Monitors => Volatile.Read(ref _monitors);

    /// <summary>The primary display; a zero-size placeholder when Windows reports no display at all.</summary>
    public MonitorInfo Primary
    {
        get
        {
            var monitors = Monitors;
            return monitors.Count == 0
                ? new MonitorInfo(string.Empty, 0, 0, 0, 0, true)
                : monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        }
    }

    public event Action? Changed;

    public void Dispose() => _source.MessageReceived -= OnMessage;

    /// <summary>Enumerates the monitors Windows reports now, in virtual-screen pixels.</summary>
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var found = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(found);
        try
        {
            PInvoke.EnumDisplayMonitors(default, null, &CollectMonitor, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return found;
    }

    private void OnMessage(WindowMessage message)
    {
        if (message.Id != WindowMessage.DisplayChange)
        {
            return;
        }

        var updated = _enumerate();
        var previous = Interlocked.Exchange(ref _monitors, updated);
        if (!previous.SequenceEqual(updated))
        {
            Changed?.Invoke();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static BOOL CollectMonitor(HMONITOR monitor, HDC dc, RECT* bounds, LPARAM data)
    {
        var list = (List<MonitorInfo>)GCHandle.FromIntPtr(data.Value).Target!;
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
        {
            var rect = info.monitorInfo.rcMonitor;
            var work = info.monitorInfo.rcWork;
            var id = new string(info.szDevice.AsSpan().TrimEnd('\0'));
            var primary = (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0;

            // The effective DPI is what the flyout scales by. A monitor that will not say keeps the default of 96.
            var dpi = 96;
            if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded && dpiX > 0)
            {
                dpi = (int)dpiX;
            }

            list.Add(new MonitorInfo(id, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, primary)
            {
                WorkArea = new PixelRect(work.left, work.top, work.right - work.left, work.bottom - work.top),
                Dpi = dpi,
            });
        }

        return true;
    }
}
