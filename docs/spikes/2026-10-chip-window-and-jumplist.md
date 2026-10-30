# Spike: a no-focus tool window for the countdown chip, and the jump list API (2026-10-30)

Questions (Task 0 of the countdown chip and jump list plan):

1. Can a WinUI 3 window be a chip: always on top, never active, clickable and draggable, rounded, with a backdrop, and absent from the taskbar and Alt+Tab?
2. Does `Windows.UI.StartScreen.JumpList` work for the packaged app with no window open and the app unpinned, and what can a person do with the items?

Short answers on Windows 11 build 26200 (ARM64, Parallels, 150 percent scaling, the development package, Windows App SDK 2.5.1):

| Behaviour | Works? | How |
| --- | --- | --- |
| `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW` set after creation survive the first show | yes | read back 0x08000088 (with `WS_EX_TOPMOST`) before show, right after the first show and 1.5 s later |
| Pointer click, right click, double click, drag reach the XAML content | yes | `PointerPressed`, `Tapped`, `RightTapped`, `DoubleTapped`, `PointerMoved` all fire from real `SendInput` mouse input |
| ... without activating the window | **no, not with the style alone; yes with `NoActivateGuard`** | WinUI itself activates the window and its input site on a press; a `WH_CBT` hook refuses both (details below) |
| Stays topmost over a maximized window, also after that window is activated again | yes | `WindowFromPoint` at the chip's centre is the chip, before and after |
| Rounded corners | yes | `DWMWA_WINDOW_CORNER_PREFERENCE` reads 2 and the composed screen shows the window behind in all four corner pixels |
| Acrylic and Mica backdrop | yes, **but only with a custom always-active backdrop** | the built-in `DesktopAcrylicBackdrop` and `MicaBackdrop` fall back to a flat colour on a window that is never active |
| No taskbar button, no Alt+Tab entry | yes | Windows' rule gives the chip none; the real Alt+Tab switcher listed only the ordinary window |
| `JumpList.IsSupported()` | yes (true) | |
| `LoadCurrentAsync` / `SaveAsync` with no window open, app unpinned | yes | items read back; Windows wrote them to disk |
| Where Windows draws the list, and whether a person can remove task items | **not observed here** | this VM's taskbar cannot be seen by UI Automation; see "What could not be driven" |
| Jump list launch arguments reach the app | yes, on the command line | `IApplicationActivationManager.ActivateApplication(aumid, arguments)` |

## How it was probed

A throwaway SELFTEST-only scenario `spike-chip-window` (and a second, `spike-jump-list`) ran in the real packaged app on the VM. It was not committed or registered in `selftest-scenarios.txt`; the code is gone and everything it measured is below. All content was invented: a maximized window split into a pure red left half and a pure blue right half (so a blur or a rounded corner is measurable from pixels), a chip window reading "Planning review - in 12 min", and jump list items named "Spike ...". The screen was read from the composed desktop (`BitBlt` with `CAPTUREBLT`), input was real (`SendInput`: absolute mouse moves, button down and up, keys), and `GetForegroundWindow` was sampled every 4 ms around each click.

## 1. The chip window

### What was built

Created in this order (this is the recipe Task 2 should follow):

1. `new Window()`; `OverlappedPresenter`: `SetBorderAndTitleBar(false, false)`, not resizable, maximizable or minimizable, `IsAlwaysOnTop = true`; `AppWindow.IsShownInSwitchers = false`.
2. `FlyoutWindowChrome.StyleAsFlyout(hwnd)` (frame removed, `WS_EX_TOOLWINDOW`, no `WS_EX_APPWINDOW`, round corners) and `TugWindowChrome.MakeNonActivating(hwnd)` (`WS_EX_NOACTIVATE`): the helpers that already exist.
3. **`NoActivateGuard.Protect(hwnd)`** (new, committed with this document, see below).
4. `AppWindow.MoveAndResize(...)`, `AppWindow.Show(false)` (no activation), `TugWindowChrome.PlaceTopmost(hwnd, ..., activate: false)`.

The window was 450 x 66 pixels (300 x 44 DIP at 150 percent) at the centre of the primary monitor's work area.

### Styles survive the show

| Moment | Extended style |
| --- | --- |
| just after `new Window()` | 0x00000100 (`WS_EX_WINDOWEDGE`) |
| after `StyleAsFlyout` and `MakeNonActivating`, before show | 0x08000088 (`NOACTIVATE`, `TOOLWINDOW`, `TOPMOST`) |
| right after the first show | 0x08000088 |
| 1.5 s later, and again after every backdrop change | 0x08000088 |

WinUI does not reset them. `AppWindow.IsShownInSwitchers` reads false.

### The style alone is not enough: WinUI activates the window itself

With the styles and no guard, a real left click on the chip made it the foreground window about 34 ms after the button went down (`GetForegroundWindow`: blocker, then **chip**), and `Window.Activated` reported `CodeActivated`. This repeated for every click, right click, double click and drag, in every run. A control window without the styles took the foreground on the same click (so the test can tell), and the chip received its pointer events either way.

Approaches tried, in order:

| Approach | Result |
| --- | --- |
| A. Styles after creation (`SetWindowLongPtr`) and `Show(false)` | styles hold, **window still activated on a press** |
| B. A window subclass (comctl32 `SetWindowSubclass`) on the top-level window and its three child windows (`InputNonClientPointerSource`, `DesktopChildSiteBridge`, `InputSiteWindowClass`) answering `WM_MOUSEACTIVATE` and `WM_POINTERACTIVATE` with no-activate | **never consulted**: neither message arrives. The trace showed no mouse or pointer message at all; just `WM_NCACTIVATE(1)`, `WM_ACTIVATE(WA_ACTIVE)` on the top-level window, then `WM_IME_SETCONTEXT` and `WM_SETFOCUS` on the input site. The XAML input stack activates the window and takes the focus by calls, not through the click's own messages |
| C. A thread `WH_CBT` hook that returns 1 for `HCBT_ACTIVATE` and `HCBT_SETFOCUS` when the window is the protected window or one of its children | **works** |

With C (`NoActivateGuard`): foreground transitions during a left click were `[blocker]` only; no `Activated` event fired; `Tapped`, `RightTapped`, `DoubleTapped` and the drag all arrived; the chip followed a 120-pixel drag by exactly 120 pixels (x 2079 to 2199, y unchanged). The hook refused 15 requests over the four trials (activation and focus requests). Showing the chip with `Show(false)` did not change the foreground either.

Notes for Task 2:

- A foreground change not caused by the chip does happen on this VM (Windows Terminal's window and Parallels' helper take the foreground now and then, as earlier spikes found), so "the chip never became the foreground window" is what to assert (the chip is absent from the sampled transitions, and no `Activated` event came), not "the foreground did not change".
- The guard is per UI thread and per window; dispose it before the window closes. It refuses nothing for other windows of the app or other apps.
- The window never has the keyboard. That matches the plan (Esc is not meaningful); the live-region announcement is a UI Automation notification and needs no focus.

### Topmost, rounded corners, taskbar and Alt+Tab

- **Topmost**: `WindowFromPoint` at the chip's centre returned the chip over the maximized blocker, and still did after the blocker was activated again with `Window.Activate()` (it rises above other non-topmost windows). Not probed: an exclusive full-screen app (the plan hides the chip there through `INotificationState`).
- **Rounded corners**: the preference reads back as 2. In every backdrop variant the four corner pixels of the composed screen were exactly the colour of the window behind (255,0,0 left, 0,0,255 right), and the pixel one step inside the corner was too. (One reading of one variant in one run showed a dark corner pixel, 12,12,12; every other reading, dozens over the runs, was exact.)
- **Taskbar and Alt+Tab**: Windows' rule (visible, not cloaked, and either `WS_EX_APPWINDOW` or neither owned nor `WS_EX_TOOLWINDOW`) gives the chip none and the ordinary window one (`WindowAudit`). A real Alt+Tab (`SendInput`, Alt held, Tab pressed) opened the switcher ("Task Switching"); its UI Automation tree had one entry, "Spike blocker", and none for the chip.

### Backdrop

Measured on the composed screen over the red and blue halves (the chip sits on the split). "Left" and "right" are samples in the chip's 20 percent and 80 percent columns near its top edge, where there is no text.

| Backdrop | Left | Right | Reading |
| --- | --- | --- | --- |
| solid brush (32,32,32) | 32,32,32 | 32,32,32 | flat |
| `DesktopAcrylicBackdrop` (built in) | 44,44,44 | 44,44,44 | **flat: the inactive fallback** |
| `MicaBackdrop` (built in) | 32,32,32 | 32,32,32 | **flat: the inactive fallback** |
| custom acrylic, `IsInputActive = true` | 217,91,91 | 81,81,207 | live blur of what is behind |
| custom acrylic, `IsInputActive = false`, fallback yellow | 255,255,0 | 255,255,0 | fallback shows |
| custom Mica, `IsInputActive = true`, default tint | 241,243,249 | 241,243,249 | real Mica, light |
| custom Mica, `IsInputActive = false`, fallback yellow | 255,255,0 | 255,255,0 | fallback shows |
| custom Mica active, `SystemBackdropConfiguration.Theme` Dark or Light | 241,243,249 | 241,243,249 | **the configuration's theme made no difference** (same for acrylic: 217,91,91 both) |
| custom Mica active with an explicit dark tint (32,32,32; tint and luminosity opacity 0.85) | 32,36,54 | 29,33,48 | dark, as asked |
| custom Mica active with an explicit light tint (243,243,243; 0.85) | 215,216,221 | 212,213,217 | light, as asked |
| custom acrylic active with an explicit dark tint (32,32,32; 0.7 and 0.5) | 92,38,38 | 22,22,98 | dark, blur still visible |

So: the built-in backdrops track window activation and a window that is never active shows only the fallback. The existing `TugBackdrop` already does the right thing for acrylic (a `SystemBackdrop` subclass whose configuration says `IsInputActive = true`); the chip needs the same, for acrylic, with the palette's own tint and fallback colour so that the look does not depend on the system theme (Mica ignores the configuration's theme here, and Mica samples the wallpaper only, so it is the wrong material for a chip that floats over windows). Screenshots read: rounded corners, the red-to-blue blur through the chip, legible text on both dark-tinted variants.

### The helper committed with this document

`src/TimeTug.Platform/Chip/NoActivateGuard.cs`: `NoActivateGuard.Protect(nint topLevelWindow)` returns an `IDisposable`; it installs one `WH_CBT` hook on the calling UI thread (removed with the last protected window) and refuses `HCBT_ACTIVATE` and `HCBT_SETFOCUS` for the protected window and its children. `NoActivateGuard.Vetoes` counts refusals (for diagnostics and the `chip-interaction` scenario). It is the exact code that ran in the spike (runs 5 to 8), nothing else. Task 2's `chip-interaction` scenario is its test on the real HWND (it needs a real window, so there is no fake-based unit test). Everything else the spike used (the throwaway scenarios, input injection, screen reads) is not committed.

## 2. The jump list

`Windows.UI.StartScreen.JumpList` in the packaged development app, **with no window open** (the scenario's first step; 0 visible windows of the process) and the package not pinned (installed by script, never pinned):

- `JumpList.IsSupported()` is true. `AppInfo.Current.AppUserModelId` is `BinaryCompanion.TimeTug.Dev_68z4tsj3tx7ew!App`.
- `LoadCurrentAsync()` returned a list with 0 items and `SystemGroupKind = Recent` (the default; `None` after we set it, and it stayed `None` in later runs, so Windows stores it).
- `SaveAsync()` with four task items (`JumpListItem.CreateWithArguments("--jump flyout", "name")` and three more, no `GroupName`) and one item with a `GroupName` succeeded with no window open. `Description` and a `ms-appx:///Assets/Square44x44Logo.png` logo were accepted.
- Reading back: five items, in order; the four without a group have `Kind = Arguments`, empty `GroupName`, the exact arguments and `RemovedByUser = false`; the fifth has its group name. Setting `SystemGroupKind = None` keeps a "Recent" category out (the app has no recent documents).
- Windows keeps the list on disk: `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\890bfe4f88a8af70.customDestinations-ms` (5316 bytes) was written during `SaveAsync` and contains the item names (UTF-16) and the group name. It is the file the shell reads. **It outlives the process**: after the app exits the list is still there.
- Clearing (`Items.Clear()`, `SaveAsync()`) reads back as 0 items.
- Launch arguments: `IApplicationActivationManager.ActivateApplication("BinaryCompanion.TimeTug.Dev_68z4tsj3tx7ew!App", "--selftest hello --out <folder>", 0)` (the call the shell makes for an item of a packaged app's list; the VM user session) started the app, and the app's own command-line parser (`Environment.GetCommandLineArgs`) saw the arguments: the `hello` scenario ran and wrote its result. So `--jump <action>` arrives as ordinary command-line arguments in a fresh process, like the second launch of slice 2; it is not an `AppInstance` activation by itself.

### What could not be driven

- **Where Windows draws the list** (taskbar button while a window is open, pinned icon, Start entry) was not observed. On this VM UI Automation sees no element under `Shell_TrayWnd` (zero, with the taskbar auto-hide bit off and Explorer restarted, from both the app process and a script in the user session), so a script could not find the taskbar button to right-click; the VM's taskbar bit and Explorer were put back afterwards. The earlier flyout spike could read the taskbar on a differently configured session; this one could not. The Alt+Tab switcher was readable, so the limit is the taskbar surface, not input.
- **Whether a person can remove task items** was not observed. Microsoft's documentation for jump lists says items in the Tasks category are fixed by the app and cannot be removed by the person, while items in custom categories can be, and the WinRT item has a `RemovedByUser` flag for that; this was not confirmed on screen here.
- **Whether Windows 11's Start menu shows the list** was not observed. It is widely reported that the Windows 11 Start menu does not show jump lists (only the taskbar button does); treat that as unverified and do not promise Start.

## Design changes this forces

Neither feature is blocked. Four statements in the plan need amending (proposed wording; the plan lives in `timetug`, so it is amended there, not here):

1. **Design, the chip's "never takes focus (no activation on click through `WS_EX_NOACTIVATE`; click handling through pointer events)", and Task 2's `ChipWindow` list.** Replace with: "never takes focus: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` plus `NoActivateGuard.Protect` (a thread `WH_CBT` hook that refuses `HCBT_ACTIVATE` and `HCBT_SETFOCUS` for the chip; `WS_EX_NOACTIVATE` alone does not stop WinUI activating the window on a press, see `docs/spikes/2026-10-chip-window-and-jumplist.md`); click handling through pointer events". Task 2's `chip-interaction` step already asserts the real HWND; make it assert "the chip is never in the sampled foreground transitions and raises no `Activated` event" and that the guard's `Vetoes` increased (so the test cannot pass by accident).
2. **Design, Appearance: "follows the flyout backdrop choice (Mica or Acrylic, solid under reduced transparency and contrast)", and Task 2's `ChipWindow` "backdrop per the Appearance choice".** Replace with: "acrylic through a custom always-active `SystemBackdrop` (like `TugBackdrop`: `IsInputActive = true`, the palette's tint and fallback colour; the built-in `DesktopAcrylicBackdrop` and `MicaBackdrop` show only a flat colour on a window that is never active); solid under reduced transparency and contrast. Mica is not used: it samples the wallpaper, ignores the configuration's theme, and needs an explicit tint anyway."
3. **Design, jump list, "Limit to state honestly": "so the list is reachable from Start and the pin".** Replace with: "reachable from the taskbar button of a running or pinned TimeTug; whether Windows 11's Start shows it is not verified and is not promised." Task 5's spike record (`docs/spikes/2026-10-jump-list.md`) keeps the observed-or-not wording per surface.
4. **Design, jump list, privacy and rebuild: add persistence.** Windows keeps the list on disk after the app exits, so an item such as "Join next meeting" can outlive the meeting that justified it. Add: "the service saves the base list (without 'Join next meeting') at startup before the first state and when the app quits; a stale 'Join next meeting' (launched with no join link now) opens the flyout (Task 5 already says so)." Task 4 gets one more test (`SaveAsync` is called with the base list on quit).

Two confirmations the plan can rely on: jump list arguments arrive on the command line (`ActivationParser` over argv, then `AppInstanceService` redirect, as Task 4 and 5 describe; the `AppInstance` redirection path is separate and is still tested by the Task 5 scenario), and `JumpList` needs no window and no pin.

## Reproducing

The scenario code is not kept. To repeat a measurement: a SELFTEST-only scenario that creates a maximized two-colour window and the chip as in section 1 (with and without `NoActivateGuard.Protect`), samples `GetForegroundWindow` every few milliseconds around a `SendInput` click, and reads the composed screen with `BitBlt`; run it with `scripts/vm/vm-selftest.sh <scenario> --no-build`. On the VM, a taskbar hidden by the auto-hide bit (`StuckRects3`, byte 8) and Windows Terminal's habit of taking the foreground are the two things to expect.
