# Spike: the Community Toolkit's settings controls under Windows App SDK 2.x (2026-10-21)

Question (flagged untested in slice 1): do `SettingsCard` and `SettingsExpander` from `CommunityToolkit.WinUI.Controls.SettingsControls` 8.2.251219 work in this app, which pins Microsoft.WindowsAppSDK 2.5.1 (WinUI 2.3.9) on .NET 10, in a Mica window with a `NavigationView`?

Answer: yes. They restore, compile (XAML and code), render and lay out on Windows 11 ARM64 (the VM) and on both CI architectures, with no extra resource dictionary to merge. The first real use is General's hub (two click-enabled cards); `settings-shell` also builds a card with a switch and an expander with an item in code, checks sizes and the collapse and expand, and keeps a screenshot (`docs/screenshots/settings-controls-probe.png`).

## What was checked

| Check | Result |
| --- | --- |
| Package restore with the pinned version, referenced by `TimeTug.App` only | works; nothing else to pin |
| `<toolkit:SettingsCard IsClickEnabled Click=...>` with `HeaderIcon` in XAML (hub rows) | renders as the Windows 11 settings row, with the chevron; clicking it (a real mouse click in the self-test) raises `Click` |
| `SettingsCard` with a `ToggleSwitch` as content | renders, 71 DIP high at the default settings |
| `SettingsExpander` with `Items` holding a `SettingsCard` and a `ComboBox` | renders expanded; `IsExpanded` false and true change its height (71 and 122 pixels in the probe) |
| Fonts, theme and backdrop | follow the app theme; the card fill is drawn over Mica as in the Settings app |

## Accessibility findings (act on these when building the panes)

- A `SettingsCard` with only `Header` set has an **empty** UI Automation name (`SettingsCardAutomationPeer`). Always set `AutomationProperties.Name` on every card (the hub rows do; the self-test asserts an explicit name is taken). A `SettingsExpander` does take its header as its name.
- A click-enabled `SettingsCard` exposes **no UI Automation Invoke pattern**, so a test cannot invoke it through the peer (the self-test uses a real mouse click) and a screen reader's "default action" falls back to its own click. It still activates by keyboard (Enter and Space) and mouse. If Narrator testing in the manual checklist shows trouble, wrap the row's action in a `Button` or derive a peer.
- `SettingsExpander`'s peer does not offer the ExpandCollapse pattern (`SettingsExpanderAutomationPeer`, pattern absent). The expander's header button still toggles it from the keyboard; check Narrator's wording on the Calendars pane.
- `axe-windows` (round 4 of `a11y-hold`, the Settings window with the search results open) reports no errors with the cards named.

## Other things seen while building the shell

- `OverlappedPresenter.PreferredMinimumWidth` and `PreferredMinimumHeight` exist in this SDK and keep the window from being sized below 720 x 460 DIP.
- `NavigationView` in `Left` pane mode with `IsPaneToggleButtonVisible="False"` keeps the pane open at any width the minimum allows, and `NavigationView.AutoSuggestBox` puts the search box at the top of the pane.
- The title bar is drawn in the light theme while the app is dark (the window does not follow `RequestedTheme` for the caption); the appearance step sets the caption colours explicitly.
- A pane page is unloaded while another is open, so a page must subscribe to the shell in `Loaded` and unsubscribe in `Unloaded` (subscribing once in the constructor lost the subscription after the first pane change; the self-test caught it).
