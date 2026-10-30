using System.Runtime.InteropServices;
using System.Text;
using TimeTug.Presentation.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace TimeTug.Platform.Tray;

/// <summary>
/// Reads the real taskbar. The icon's rectangle comes from <c>Shell_NotifyIconGetRect</c>; the overflow button from UI Automation on
/// the taskbar window (<c>Shell_TrayWnd</c>), which on Windows 11 is a XAML surface with no child windows for its buttons; the notification
/// area from the taskbar's <c>TrayNotifyWnd</c> child or, if that has no size, the end of the taskbar nearest the clock. UI Automation runs
/// on its own multithreaded-apartment thread with a time limit, because a client must not call into another process from a UI thread
/// and a stuck explorer must not freeze the flyout.
/// </summary>
public sealed unsafe partial class WindowsTaskbarProbe : ITaskbarProbe
{
    private static readonly TimeSpan UiAutomationLimit = TimeSpan.FromSeconds(2);

    // What the Windows 11 taskbar calls its notification-area buttons, from a real probe (docs/spikes/2026-10-flyout-anchor.md): every
    // one has the automation id "SystemTrayIcon", and the overflow button ("Show Hidden Icons") is the first of the class
    // "SystemTray.NormalButton" inside the notification area. Names are localised, so they are never matched.
    internal const string TrayButtonId = "SystemTrayIcon";
    internal const string TrayNormalButtonClass = "SystemTray.NormalButton";

    public PixelRect? IconRect(Guid iconId)
    {
        var identifier = new NotifyIconIdentifier { Size = (uint)sizeof(NotifyIconIdentifier), Guid = iconId };
        return Shell_NotifyIconGetRect(ref identifier, out var rect) >= 0 ? ToPixels(rect) : null;
    }

    public PixelRect? OverflowButtonRect() => OnUiAutomationThread(FindChevron, null);

    public PixelRect? NotificationAreaRect()
    {
        var taskbar = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (taskbar.IsNull)
        {
            return null;
        }

        var notify = PInvoke.FindWindowEx(taskbar, default, "TrayNotifyWnd", null);
        if (!notify.IsNull && PInvoke.GetWindowRect(notify, out var area) && ToPixels(area) is { IsEmpty: false } found)
        {
            return found;
        }

        // No sized notification area: the end of the taskbar nearest the clock (the right end, the bottom end of a vertical bar).
        if (!PInvoke.GetWindowRect(taskbar, out var bar))
        {
            return null;
        }

        var whole = ToPixels(bar);
        const int Corner = 160;
        return whole.Width >= whole.Height
            ? new PixelRect(Math.Max(whole.X, whole.Right - Corner), whole.Y, Math.Min(Corner, whole.Width), whole.Height)
            : new PixelRect(whole.X, Math.Max(whole.Y, whole.Bottom - Corner), whole.Width, Math.Min(Corner, whole.Height));
    }

    public string Describe() => OnUiAutomationThread(DescribeTaskbar, "(UI Automation did not answer)");

#if SELFTEST
    /// <summary>Self-test only: how many taskbar elements there are of each UI Automation class (class names only, no names or ids). Null when UI Automation did not answer in time.</summary>
    public static IReadOnlyDictionary<string, int>? ElementClassCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var answered = OnUiAutomationThread(
            () =>
            {
                _ = WalkTaskbar(element =>
                {
                    counts[element.ClassName] = counts.GetValueOrDefault(element.ClassName) + 1;
                    return null;
                });
                return true;
            },
            false);
        return answered ? counts : null;
    }

    /// <summary>
    /// Self-test only: the taskbar's elements whose name or automation id (an app's button is identified by its application id, here the package family name) contains <paramref name="text"/> (case-insensitive). Nothing else on the
    /// taskbar is returned or logged. Null when UI Automation did not answer in time.
    /// </summary>
    public static IReadOnlyList<TaskbarMatch>? ButtonNamesContaining(string text)
    {
        var names = new List<TaskbarMatch>();
        var answered = OnUiAutomationThread(
            () =>
            {
                _ = WalkTaskbar(element =>
                {
                    if ((element.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || element.AutomationId.Contains(text, StringComparison.OrdinalIgnoreCase)) && !element.Rect.IsEmpty)
                    {
                        names.Add(new TaskbarMatch(element.Name, element.ClassName, element.AutomationId));
                    }

                    return null;
                });
                return true;
            },
            false);
        return answered ? names : null;
    }
#endif

    // ---- UI Automation ------------------------------------------------------------------------------------------

    private PixelRect? FindChevron()
    {
        // The chevron sits inside the notification area; the same class on the taskbar's app buttons must not match.
        var area = NotificationAreaRect();
        return WalkTaskbar(element =>
            element.AutomationId == TrayButtonId && element.ClassName == TrayNormalButtonClass && !element.IsOffscreen && !element.Rect.IsEmpty
            && (area is not { } box || box.Contains(element.Rect.CenterX, element.Rect.CenterY))
                ? element.Rect
                : null);
    }

    private static string DescribeTaskbar()
    {
        var text = new StringBuilder();
        _ = WalkTaskbar(element =>
        {
            text.AppendLine(FormattableString.Invariant($"{element.ControlType,5} | id='{element.AutomationId}' | class='{element.ClassName}' | name='{element.Name}' | rect=({element.Rect.X},{element.Rect.Y},{element.Rect.Width}x{element.Rect.Height}) | offscreen={element.IsOffscreen}"));
            return null;
        });
        return text.ToString();
    }

    /// <summary>Visits every element under the taskbar window; returns the first non-null answer.</summary>
    private static PixelRect? WalkTaskbar(Func<TaskbarElement, PixelRect?> visit)
    {
        var taskbar = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (taskbar.IsNull)
        {
            return null;
        }

        IUIAutomation* automation = null;
        IUIAutomationElement* root = null;
        IUIAutomationCondition* all = null;
        IUIAutomationElementArray* found = null;
        try
        {
            if (CUIAutomation.CreateInstance(out automation).Failed || automation is null)
            {
                return null;
            }

            root = automation->ElementFromHandle(taskbar);
            all = automation->CreateTrueCondition();
            found = root->FindAll(TreeScope.TreeScope_Descendants, all);
            var count = found->Length;
            for (var i = 0; i < count; i++)
            {
                var element = found->GetElement(i);
                try
                {
                    var rect = element->CurrentBoundingRectangle;
                    var item = new TaskbarElement(
                        Text(element->CurrentAutomationId),
                        Text(element->CurrentClassName),
                        Text(element->CurrentName),
                        new PixelRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top),
                        element->CurrentIsOffscreen,
                        (int)element->CurrentControlType);
                    if (visit(item) is { } answer)
                    {
                        return answer;
                    }
                }
                finally
                {
                    _ = element->Release();
                }
            }

            return null;
        }
        finally
        {
            if (found is not null)
            {
                _ = found->Release();
            }

            if (all is not null)
            {
                _ = all->Release();
            }

            if (root is not null)
            {
                _ = root->Release();
            }

            if (automation is not null)
            {
                _ = automation->Release();
            }
        }
    }

    private static string Text(BSTR value)
    {
        try
        {
            return value.Value is null ? string.Empty : new string(value.Value);
        }
        finally
        {
            Marshal.FreeBSTR((nint)value.Value);
        }
    }

    private static T OnUiAutomationThread<T>(Func<T> work, T fallback)
    {
        var result = fallback;
        var thread = new Thread(() =>
        {
            var initialised = PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED).Succeeded;
            try
            {
                result = work();
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
            {
                result = fallback;
            }
            finally
            {
                if (initialised)
                {
                    PInvoke.CoUninitialize();
                }
            }
        })
        { IsBackground = true, Name = "TimeTug taskbar probe" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return thread.Join(UiAutomationLimit) ? result : fallback;
    }

    private static PixelRect ToPixels(RECT rect) => new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);

    private static PixelRect ToPixels(NativeRect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private readonly record struct TaskbarElement(string AutomationId, string ClassName, string Name, PixelRect Rect, bool IsOffscreen, int ControlType);

    // NOTIFYICONIDENTIFIER and RECT as the shell declares them; CsWin32 cannot generate the former for AnyCPU.
    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid Guid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect rect);
}

#if SELFTEST
/// <summary>A taskbar element the self-test asked for by name: the name, the class and the automation id (a notification-area icon has the id <c>SystemTrayIcon</c>).</summary>
public sealed record TaskbarMatch(string Name, string ClassName, string AutomationId)
{
    public bool IsTrayIcon => AutomationId == WindowsTaskbarProbe.TrayButtonId;
}
#endif
