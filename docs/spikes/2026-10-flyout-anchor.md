# Spike: where the tray icon is, so the flyout can open next to it (2026-10-20)

Question: how does the app find its tray icon on screen, in physical pixels, when the icon is pinned to the taskbar and when it sits in the overflow flyout ("Show hidden icons")? Plan order, from most to least exact: `Shell_NotifyIconGetRect` for the icon, then the overflow button found through UI Automation on the taskbar, then the notification area's own rectangle.

Answer: on Windows 11 build 26200 (ARM64, Parallels, 150 percent scaling, taskbar at the bottom) `Shell_NotifyIconGetRect` answers in both cases. For a pinned icon it returns the icon's own button; for an icon in the overflow it returns the rectangle of the overflow button (the chevron), the same rectangle UI Automation reports for it. The two fallbacks are therefore the safety net, not the main path. All three were probed with the real tray icon of the development package (`flyout-anchor` self-test scenario, which writes `anchor.txt`, `taskbar-uia.txt` and, with `--hold`, `anchor-log.txt`).

## Probe results

Screen 3360 x 1890 pixels (a 2240 x 1260 DPI-virtualised view of it for non-DPI-aware processes), scale 150 percent, taskbar 72 pixels high at the bottom. The app is per-monitor DPI aware, so every rectangle is in physical pixels.

| State | `Shell_NotifyIconGetRect` (icon id) | Overflow button (UI Automation) | Notification area (`TrayNotifyWnd`) | Source used |
| --- | --- | --- | --- | --- |
| Icon in the overflow (the default: `IsPromoted` unset) | (3039, 1818) 48 x 72, the overflow button | (3039, 1818) 48 x 72 | (3039, 1818) 321 x 72 | `Icon` |
| Icon pinned (`IsPromoted` = 1, Explorer restarted) | (3039, 1818) 48 x 72, the icon's own button; the overflow button moved left to (2991, 1818) | (2991, 1818) 48 x 72 | (2991, 1818) 369 x 72 | `Icon` |
| Taskbar set to hide itself (the VM's normal setting) | the same rectangles; the work area equals the whole display, so the placement takes the screen edge nearest the icon | same | same | `Icon` |
| No taskbar (hosted ARM64 runner) | icon not created | none | none | no anchor: the flyout opens in the bottom-right corner of the work area |

What the placement did with them: with the taskbar visible (work area 3360 x 1812) the 540 x 780 pixel flyout (360 x 520 DIP) sat 12 pixels (8 DIP) above the work area's bottom edge, centred on the icon and pushed against the right edge's margin; with the taskbar hidden it sat 12 pixels above the icon's top edge. A real screen capture with the flyout over a maximized Notepad is `docs/screenshots/flyout-over-maximized-window.png`.

## How the taskbar looks to UI Automation (Windows 11, the XAML taskbar)

The window `Shell_TrayWnd` has no child window for its buttons; UI Automation sees them. Relevant elements, in tree order:

| Automation id | Class | Name (localised, never matched) | Notes |
| --- | --- | --- | --- |
| `SystemTrayIcon` | `SystemTray.NormalButton` | "Show Hidden Icons" | The overflow button. First in the notification area. |
| `NotifyItemIcon` | `SystemTray.NormalButton` | the icon's tooltip text | A pinned app icon (ours: the live tooltip, "Design review in 2 hr 30 min"). Only present while pinned. |
| `SystemTrayIcon` | `SystemTray.AccentButton`, `OmniButtonCenter`, `OmniButton`, `ShowDesktopButton` | Network, Volume, Power, Clock, Show Desktop | System buttons. |
| (none) | `TrayNotifyWnd` | | A real child window whose rectangle is the notification area (clock and show-desktop included). |

The overflow button is therefore the element with automation id `SystemTrayIcon` and class `SystemTray.NormalButton` whose centre lies inside the `TrayNotifyWnd` rectangle. Names are localised and the automation id alone is shared by every system button, so neither is matched on its own. If a future Windows renames these, the first step still answers (as it does today for both cases) and the second falls through to the notification area.

## Notes

- UI Automation is called from its own multithreaded-apartment thread with a two-second limit (a UI Automation client must not call another process from a UI thread, and a stuck Explorer must not freeze the flyout). The whole dump of the taskbar takes about 50 ms.
- The anchor is read when the flyout opens and kept while it is open; the icon does not move meanwhile.
- A rectangle that is empty or on no monitor is rejected as stale, so a display change between two clicks cannot place the flyout off screen.
- The pinned state was produced by setting `HKCU\Control Panel\NotifyIconSettings\<id>\IsPromoted` to 1 and restarting Explorer; the registry value alone does not move a running icon. Windows 11 also needs the setting for a person who drags the icon out of the overflow, which is the normal way.
- The VM's taskbar auto-hide bit (`StuckRects3`, byte 8, 3 = hide) was set to 2 for the visible-taskbar probe and the real capture, and both the bit and `IsPromoted` were put back afterwards (Explorer restarted each time).
- Not probed: a taskbar on the top or a side (Windows 11 22H2 and later keep it at the bottom; the placement tests cover all four edges), and a second monitor (the second monitor's taskbar holds no tray icons in Windows 11; the placement tests cover negative coordinates and mixed scaling).
