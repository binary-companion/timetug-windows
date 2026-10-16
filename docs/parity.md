# Mac parity ledger

One row per capability of the Mac app (`Apps/macOS/Sources` in `binary-companion/timetug`). Status is one of: **done**, **differs on purpose**, **not applicable**, **not yet**. This table is the release gate for phase 9: a real release needs every row done, differs on purpose or not applicable (with the reason in Notes).

| Area | Feature | Mac capability | Status | Notes |
|---|---|---|---|---|
| Tug screen | Takeover | Full-screen takeover with hero picture, live countdown, title, details line (OverlayView, OverlayController, TakeoverText) | done | Slice 1. `TugWindowHost` draws the controller's plans; the running app composes it (`AppComposition`), and the `startup` self-test shows it from the real engine on both architectures |
| Tug screen | Takeover | Join (primary), Snooze 1/5/10 minutes, Dismiss; Return, Esc and 1, 5, 0 keys | done | Slice 1. Built and self-tested (task 8), composed in task 9 |
| Tug screen | Takeover | Mirror on other monitors ("Dismiss on your main display") | done | Slice 1. Built and self-tested (task 8), composed in task 9 |
| Tug screen | Takeover | Safe Join link opening (SafeJoin, JoinLabel); link shown to copy if opening fails | done | Slice 1. Built and self-tested (task 8), composed in task 9 |
| Tug screen | Takeover | Late return of a tug held while locked or covered | done | Windows-specific rule, slice 1. Composed in task 9 (`AppRuntime` carries unlock, full-screen-ended and monitor changes to the controller); `LateReturnTests` cover the rule end to end. A real lock and unlock is a manual check |
| Tug screen | Takeover | Toast fallback when the screen cannot be shown | done | Windows-specific, slice 1. `ToastService` is composed in task 9 and Windows accepts the toast in the packaged self-test; the button presses are a manual check |
| Tug screen | Takeover | Test tug | not yet | Slice 1. The welcome window's Test tug button works through the real engine (`startup` self-test); the tray item arrives with the tray (task 10) |
| Tray | Menu bar item | Status item with icon states (MenuBarIconState: idle, soon) | not yet | Tray icon; minimal in slice 1, states in slice 2 |
| Tray | Menu bar item | Menu bar text modes (MenuBarDisplayMode) and countdown | differs on purpose, later | Icon only, or icon with digits for the final ten minutes, plus live tooltip; pinned countdown chip deferred |
| Tray | Popup | Dropdown with the day's agenda (DropdownView, PopupRowModel, CalendarGrouping, CalendarSections) | not yet | Flyout, slice 2 |
| Tray | Popup | Popup card style (PopupCardStyle, PopupPalette, PopupStyle) | not yet | Slice 3, Appearance |
| Tray | Popup | Popup keyboard shortcut (PopupShortcut) | not yet | Global hotkey, slice 2 |
| Tray | Popup | Gear button opens Settings; Enable Tug and Quit | not yet | Slice 1 (minimal) and 2 |
| Settings | General | Launch at login | not yet | Settings toggle plus the ask in the welcome window; slice 3 |
| Settings | General | Popup shortcut recorder | not yet | Slice 3 |
| Settings | General | Other copy installed notice (CollisionNotice, InstanceArbiter) | not yet | Single instance in slice 2; notice to decide |
| Settings | Accounts | Connected accounts list, add and remove an account, sign in again, status text | not yet | Welcome window and accounts, slice 1; pane in slice 3 |
| Settings | Accounts | Google and Microsoft browser sign-in with loopback redirect (BrowserPreferringPresenter, OAuth settings) | not yet | Slice 1 |
| Settings | Accounts | iCloud and Other (CalDAV) credential form (CredentialSheet, CredentialPrompter) | not yet | Slice 1 |
| Settings | Accounts | Apple Calendar (this Mac) / EventKit source | not applicable | Windows has no system calendar store; Windows account sign-in is phase 6 |
| Settings | Accounts | Windows account sign-in (WAM) and On this PC accounts | not yet | Phase 6, no Mac equivalent |
| Settings | Calendars | Calendar list grouped by account; enable and hide calendars; subscribed and system calendars | not yet | Slice 3 |
| Settings | Tug Rules | Enable Tug, lead time, skip all-day events, require conference link, require other attendees | not yet | Slice 3, engine settings |
| Settings | Tug Rules | Duplicate detection: merge, unmerge, split off, forget learned corrections, pending-pair notices (CollisionNoticeRow, MergeBadge) | not yet | Slice 3, engine dedup commands |
| Settings | Tug Rules | On-device inference for duplicates (InferenceAvailability, AppleIntelligence) | not yet | Phase 8 (on-device AI); differs by engine |
| Settings | Appearance | Light, Dark or Auto | not yet | Slice 3; follows the Windows theme by default |
| Settings | Appearance | Menu bar text and popup card styles | not yet | Slice 3 |
| Settings | About | About window with version, license note, hero | not yet | Slice 3 |
| Settings | Software update | Sparkle updater (UpdateController, UpdatesSection, SparkleUpdater), beta channel | differs on purpose | The Store and winget update the app; website builds use an .appinstaller (phase 9) |
| Settings | Search | Settings search (SettingsSearch, SettingsNavigation) | not yet | Slice 3 |
| Settings | Diagnostics | Copy Diagnostics and Clear (AppDiagnostics, OSLogDiagnosticLog) | not yet | Slice 3; rolling log file plus ETW instead of OSLog |
| App | Lifecycle | Single instance and second-launch handoff (InstanceArbiter, InstanceFiles, InstanceSignals) | not yet | Slice 2, Windows App SDK AppInstance |
| App | Lifecycle | First-run experience | not yet | Welcome window, slice 1 (differs from the Mac on purpose: a normal window, not a tray popup) |
| App | Distribution | Beta badge and distribution kinds (BetaBadge, Distribution) | not yet | Slice 4 and phase 9 |
| App | Accessibility | Narrator announcement of the tug, reduce motion, transparency effects, contrast themes, automation names | not yet | Slice 1 for the tug; every window thereafter |
| Widgets | Widgets | Mac widgets and Control Center controls | not yet | Phase 7, Windows Widgets provider |
