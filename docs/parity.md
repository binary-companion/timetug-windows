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
| Tug screen | Takeover | Test tug | done | Slice 1. The welcome window's Test tug button works through the real engine (`startup` self-test); the tray's Test tug item does the same |
| Tray | Menu bar item | Status item with icon states (MenuBarIconState: idle, soon) | done | Slice 1. Idle follows the taskbar theme (`SystemUsesLightTheme`), Soon is the colour puppy when the engine's next takeover is for a meeting starting within ten minutes (the Mac's window), plus a Windows-only attention state (a held tug or a refused toast). Live tooltip ("Design review in 4 min"). Icons: `Assets/Tray`, made by `scripts/ci/make-icons.py` |
| Tray | Menu bar item | Menu bar text modes (MenuBarDisplayMode) and countdown | differs on purpose, done for the icon | Slice 2. The colour icon carries the remaining whole minutes (9 to 1) in the last ten minutes, drawn from bundled original digit strokes (`Assets/Tray/Glyphs`, no system font) in white with a dark outline at 16, 20, 24 and 32 px, plus the live tooltip; the pinned countdown chip is deferred. Contact sheet: `docs/screenshots/tray-digits-contact-sheet.png` |
| Tray | Popup | Dropdown with the day's agenda (DropdownView, PopupRowModel, CalendarGrouping, CalendarSections) | partial | Flyout window: header, problem rows (Sign in again), day cards with Join, empty state, "No more meetings", footer; opens next to the tray icon (`docs/spikes/2026-10-flyout-anchor.md`). A left click on the icon, a second launch and the hotkey open it (the tug wins, a quit closes it first); merge controls and the collision notice are slice 3 |
| Tray | Popup | Popup card style (PopupCardStyle, PopupPalette, PopupStyle) | not yet | Slice 3, Appearance |
| Tray | Popup | Popup keyboard shortcut (PopupShortcut) | partial | Slice 2. `HotkeyService` over `RegisterHotKey` on the hidden window, stored in the local settings (`HotkeyState`), none by default as on the Mac; a refused shortcut keeps the earlier one, is stored as a conflict and is still reported after a restart; a press toggles the flyout and does nothing while a tug shows. The `hotkey` self-test covers it with a real foreign holder. The recorder is slice 3; starting the service and toggling the flyout from it is wired in task 8 |
| Tray | Popup | Gear button opens Settings; Enable Tug and Quit | not yet | Slice 1 has the right-click menu (Next, Enable Tug, Test tug, Settings… disabled, Quit); the Join row is information only until the flyout (slice 2); Settings opens in slice 3 |
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
| App | Lifecycle | Single instance and second-launch handoff (InstanceArbiter, InstanceFiles, InstanceSignals) | partial | Slice 2. One package identity: `AppInstanceService` over the Windows App SDK `AppInstance` (key `TimeTug.Main`); a second launch redirects to the first copy and exits with code 0, the first copy holds one activation until startup has finished, ignores it while a tug shows and folds a burst into one. The mutex guard remains the unpackaged fallback. The `second-launch` self-test covers it. The flyout opening from it is wired in task 8; the other distribution's copy (Store versus website) is a separate row, phase 9 |
| App | Lifecycle | First-run experience | not yet | Welcome window, slice 1 (differs from the Mac on purpose: a normal window, not a tray popup) |
| App | Distribution | Beta badge and distribution kinds (BetaBadge, Distribution) | not yet | Slice 4 and phase 9 |
| App | Accessibility | Narrator announcement of the tug, reduce motion, transparency effects, contrast themes, automation names | not yet | Slice 1 for the tug; every window thereafter |
| Widgets | Widgets | Mac widgets and Control Center controls | not yet | Phase 7, Windows Widgets provider |
