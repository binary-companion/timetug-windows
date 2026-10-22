# Spike: theming windows that are already open (Appearance pane)

Phase 5, slice 3, task 7. Question: when the person changes Theme (Use system, Light, Dark) in Settings > Appearance, or Windows changes
its app mode under "Use system", what does the app have to do for the windows that are already on screen? Measured on the Windows 11 ARM64
VM (Windows App SDK runtime 2.5.1, packaged development build) with the `settings-appearance` scenario and a temporary spike scenario
(not kept): the Settings window open on Appearance while the theme was changed in code, one step at a time, with a PrintWindow screenshot and
pixel samples after each step, plus live screen captures of the VM for the parts PrintWindow cannot draw.

## What WinUI does by itself

| Part of the window | `root.RequestedTheme = Dark` alone | Needed |
|---|---|---|
| Controls, text, brushes (`ThemeResource`) | follow at once, including the navigation view and the pane's cards (nav pane pixel 243,243,243 to 32,32,32) | set `RequestedTheme` on the window's root element (`Window.Content`) |
| Caption bar and its buttons | **do not change**: the caption pixel stayed 243,243,243 under a dark root, and stayed 32,32,32 after the root went light again | set the `AppWindow.TitleBar` colours explicitly (`TitleBarColors`: background, foreground, inactive, button, hover and pressed) |
| Mica (stock `MicaBackdrop` from XAML) | followed the root's `ActualTheme` in both directions in the live screen captures (a dark root over a light-system window gave dark Mica, a light root gave light Mica) | nothing; it was not recreated. PrintWindow cannot draw Mica (it shows the flat theme colour), so this was judged on live captures only |
| `ContentDialog` (remove account, forget corrections, credential form) | not checked on screen; a dialog does not inherit its owner's `RequestedTheme` by WinUI's design | `ThemeService.Match` copies the owner's `ActualTheme` onto each dialog before it opens |
| The flyout's cards | drawn from code-made brushes. A brush looked up by key in `Application.Resources` is the one for the theme the app *started* in, so a flyout forced to the other theme kept the wrong text and card colours | `FlyoutPalette` takes the colour of the flyout's theme from the theme dictionaries (`ThemeColors`, with WinUI's own values as the fallback) and is rebuilt when the theme changes |

Recreated: nothing. The flyout window was already made again every time it opens; a flyout that is open when the theme changes gets its palette
rebuilt and its acrylic backdrop made again in place (`FlyoutHost` passes a new `FlyoutLook` with the theme to `Update`).

## What was tried and dropped

* **Recreating the stock `MicaBackdrop` on every change** (the plan's first idea) works (no crash, no flicker seen) but, per the table, is not needed.
* **A custom `SystemBackdrop` holding a `MicaController` and a `SystemBackdropConfiguration`, theme set live** (to control `Theme`,
  `IsInputActive` and `IsHighContrast` explicitly) **crashed the app** on the first theme change: a stowed exception, `E_INVALIDARG`
  (`ArgumentException`, "Value does not fall within the expected range") out of `Microsoft.UI.Xaml.dll` a moment after the configuration
  was changed, with no managed stack. Changing the same properties before the first frame did not fail. Not investigated further because the stock
  backdrop does the job; do not set `SystemBackdropConfiguration.Theme` on a live Mica controller in this build without testing it.
* **`ElementTheme.Default` for "Use system"** would follow Windows too, but `Application.RequestedTheme` is fixed when the app starts and
  is not testable, so the service resolves Use system to Light or Dark itself from `IUiSettings.AppsUseDarkTheme` (the registry value
  `AppsUseLightTheme` under `HKCU\...\Themes\Personalize`, re-read on `WM_SETTINGCHANGE`) and sets that explicitly.

## What the app does now

* `AppearanceResolver` (Presentation, unit-tested) decides: dark or light (the choice; the Windows apps theme under Use system), and the
  flyout backdrop (the choice, but always solid with transparency effects off or a contrast theme).
* `ThemeService` (App) attaches to the Settings window and the welcome window; on attach and on every change of the store or of Windows'
  settings it sets the root's `RequestedTheme` and the caption colours; under a contrast theme the root is `Default` and the caption colours
  are cleared (Windows draws them). A saved window place raises the store's `Changed` too and changes nothing (the service compares).
* `FlyoutHost` and `TrayViewModel` read `IPresentationSettingsStore.Changed` (the flyout re-styles when its look changed, the tray re-plans
  its icon and raises `Changed` only when the picture or tooltip differs).
* The light caption noted after task 2 (the app was dark, the caption light) is fixed: `settings-general`'s windows and every other Settings
  screenshot now have a caption in the theme's colour (`SolidBackgroundFillColorBase`, the colour Mica is drawn over).

## Not verified here

A real change of the Windows app mode, a real contrast theme and the visible look of Mica and acrylic are manual (checklist 5.18); the scenario
drives a fake `IUiSettings` and PrintWindow for everything else. In the contrast-theme screenshot the fake does not switch the real system theme, so the caption
shown there (black, inactive) is only the default Windows draws once the app stops colouring it.
