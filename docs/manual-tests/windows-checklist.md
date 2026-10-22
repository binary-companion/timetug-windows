# Windows manual test checklist (slices 1 and 2)

Every check a person has to do for slices 1 and 2: real accounts, real monitors, how it feels. The self-test scenarios (`docs/development.md`) already cover everything that can be scripted; each check below says what automation covers and what is left for you. The list grows with each slice.

## Before you start

**Get a build.** CI builds the development package for both architectures on every pull request and every merge to `main` and uploads it as a workflow artifact for 14 days:

| Artifact | For |
|---|---|
| `msix-x64` | an Intel or AMD PC |
| `msix-arm64` | an ARM PC, and the Parallels Windows 11 VM |

Each holds the package, its dependencies, `dev.cer` (the public part of a throwaway certificate; the key was deleted by the build), `vm-install.ps1`, `commit.txt` (the commit it was built from) and a short `README.txt`.

```
gh run list --repo binary-companion/timetug-windows --branch main --limit 3          # pick a green run
gh run download <run-id> --repo binary-companion/timetug-windows --name msix-x64 --dir msix-x64
```

**Install it** from an elevated PowerShell (it trusts the certificate, then installs for you):

```
powershell -ExecutionPolicy Bypass -File .\msix-x64\vm-install.ps1
```

The PC needs the .NET 10 desktop runtime for its architecture. Start TimeTug from the Start menu (TimeTug Dev). `vm-install.ps1 -Stage Uninstall` removes the package and the trusted certificate; a clean uninstall deletes the app's data, so the next install starts with no accounts. The certificate is valid 15 days from the build, so an artifact stays installable for its whole 14-day retention.

**On the Parallels VM** (no real hardware needed for most of the VM-only checks): `scripts/vm/vm-selftest.sh --artifact msix-arm64` installs the artifact there and runs the self-test; `scripts/vm/vm-run.sh --user ...` and `scripts/vm/vm-screenshot.sh <file.png>` help to look around. The VM has one display and cannot be locked or unplugged by a script, so the checks that need real displays, a real lock screen, Narrator or a real tray click are marked for a real PC.

**Accounts you need:** a Google account and a Microsoft account (any, ideally with a calendar that has events), and an iCloud account with an app-specific password (create one at <https://account.apple.com> under Sign-In and Security, App-Specific Passwords). Google and Microsoft sign-in also need the OAuth ids in `%LOCALAPPDATA%\TimeTug\dev-oauth.json` (`docs/development.md`, "OAuth client ids for local builds"; copy the file onto the PC, never paste or print it). Without it those two rows show "isn’t set up in this build".

**A meeting that is due soon.** The tug appears 60 seconds before a meeting starts. Create an event on a connected calendar that starts about 3 minutes from now (not all-day), and put a conferencing link in its location or description: a Zoom (`https://zoom.us/j/...`), Google Meet (`https://meet.google.com/...`) or Teams (`https://teams.microsoft.com/l/meetup-join/...`) link. For quick checks of the screen itself use Test tug (tray menu or the welcome window) instead.

**Recording.** Tick one box per check, write the date and the commit from `commit.txt` of the artifact you used, and note anything odd. Commit the filled-in file to a branch so the results are kept; a failed check becomes an issue.

Legend for **Automated**: the scenario name is the `--selftest` scenario in `src/TimeTug.App/SelfTest/Scenarios` (listed in `scripts/ci/selftest-scenarios.txt`); it runs in CI on x64 and ARM64 and on the VM with `scripts/vm/vm-selftest.sh <scenario>`. "Partly" says what a person still has to look at. "Not automated" means no script can do it.

---

## 1. First run

### 1.1 The welcome window appears with no accounts

- **Build:** `msix-x64` (or `msix-arm64`), fresh install (uninstall first if you had it).
- **Do:** start TimeTug from the Start menu.
- **Expect:** a normal window titled TimeTug opens in the middle of the screen, with a taskbar button: "Welcome to TimeTug", the line about connecting a calendar, and the list Google, Microsoft, iCloud, Other (CalDAV) (plus iCal link). Google and Microsoft are greyed "isn’t set up in this build" when `dev-oauth.json` is absent, and enabled when it exists.
- **Automated:** partly. `welcome` (both steps, an account added through a scripted provider, screenshots) and `startup` (the real composition root and the real engine, welcome window with no account) pass in CI. Left for you: the real Start-menu launch on a real desktop, and the look.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 1.2 Closing the window keeps TimeTug running; a second start does nothing

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** from 1.1 select "Skip for now" (or close the window). Look for the tray icon. Start TimeTug again from the Start menu.
- **Expect:** the process keeps running and the icon stays near the clock. The second start shows no window and its process ends by itself (Task Manager shows one TimeTug).
- **Automated:** partly. `AppInstanceService` has unit tests over a fake registry, and the `second-launch` self-test starts the packaged app again through its alias (exit code 0 in about a second, one process left, held until startup finished, ignored while a tug shows). Left for you: the real Start-menu launch and the tray.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 1.3 An account is already configured: the window does not appear

- **Build:** `msix-x64` (or `msix-arm64`), after one of the sign-ins in section 2 succeeded.
- **Do:** quit TimeTug from the tray menu (check 5.4), then start it again from the Start menu.
- **Expect:** no welcome window. The tray icon appears and its tooltip names the next meeting. Left-click on the icon opens the flyout (see 5.6); with no account it opens the welcome window instead.
- **Automated:** partly. `AppRuntimeTests` cover "welcome only when the first state has no accounts"; the `startup` scenario covers the no-account case. Not automated: a restart with a real stored account.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

---

## 2. Real accounts

### 2.1 Google sign-in

- **Build:** `msix-x64` (or `msix-arm64`) with `dev-oauth.json` containing a Google client.
- **Do:** welcome window, Google. Your default browser opens; sign in and allow calendar access. Come back to TimeTug.
- **Expect:** while waiting the window says "Waiting for your browser…" and offers Cancel. After the browser says it is done, the window moves to "You’re connected" with a line like "Google · you@example.com · 3 calendars" and "Next: <meeting> at <time>". A Google event you create for later today shows up as the next meeting within about a minute.
- **Automated:** partly. `welcome` drives the whole window with a scripted provider; the recorded Google responses run through the real engine in `tug-late-return`. Not automated: the real browser sign-in and the loopback redirect.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 2.2 Cancelling or failing a browser sign-in

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** start Google sign-in, then either press Cancel in TimeTug, or close the browser tab, or deny access.
- **Expect:** TimeTug returns to the provider list with a clear message ("Couldn’t connect to Google. Check your details and try again." when it failed); nothing hangs; you can start again; the diagnostics log (`%LOCALAPPDATA%\Packages\BinaryCompanion.TimeTug.Dev_68z4tsj3tx7ew\LocalState\logs\timetug.log`) names the step that failed and holds no tokens or event titles.
- **Automated:** partly. `welcome` and the view-model tests cover cancel and a failing provider. Not automated: a real denial in a browser.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 2.3 Microsoft sign-in

- **Build:** `msix-x64` (or `msix-arm64`) with `dev-oauth.json` containing a Microsoft client.
- **Do:** welcome window (or "Add another" on the second step), Microsoft; sign in with a personal or work account and allow calendar access.
- **Expect:** same as 2.1 with "Microsoft" in the summary line; events from that calendar appear as the next meeting.
- **Automated:** partly, as 2.1 (no Microsoft HTTP scenario). Not automated: the real sign-in.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 2.4 iCloud with an app-specific password

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** welcome window, iCloud. A "Sign in" dialog asks for the details the engine needs (your Apple ID and a password). First enter your normal Apple ID password and press Sign in. Then repeat with an app-specific password.
- **Expect:** the normal password is refused with "Couldn’t connect to iCloud. Check your details and try again." and the dialog or list stays usable; the app-specific password connects and the summary shows "iCloud · <calendars> · ..." with the next meeting. The password is not written to the log.
- **Automated:** partly. The credential form logic and the failure path have tests; no scenario talks to iCloud. Not automated: a real iCloud login.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 2.5 Credentials survive a restart

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** with one account connected, quit TimeTug (5.4), start it, wait a minute.
- **Expect:** the account is still connected and the next meeting shows in the tray tooltip without signing in again.
- **Automated:** partly. `startup` uses the real engine with a recorded store; `CredentialLockerStore` tests cover the revision rule that once broke this. Not automated: a real Credential Locker across a real restart.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

---

## 3. The tug screen

### 3.1 A real meeting takes over the screen

- **Build:** `msix-x64` (or `msix-arm64`), one account connected.
- **Do:** create an event starting in about 3 minutes with a conferencing link (see "A meeting that is due soon"). Keep working in another app.
- **Expect:** 60 seconds before the start the tug covers the whole screen including the taskbar: hero picture, a large countdown that ticks every second ("Starts in 0:59"), the title, a details line ("12:21 – 12:51 PM · Work · 6 people"), Join, Snooze and Dismiss, and the hints "Return joins · Esc dismisses · 1, 5 or 0 snoozes".
- **Automated:** partly. `tug-basic` (two faked monitors, primary and mirror) and `startup` (real engine test takeover) with screenshots; `tug-late-return` and `tug-meeting-gone` run the real engine on a manual clock. Not automated: the real wall clock, a real calendar.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 3.2 Join opens a real conferencing link

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** repeat 3.1 once per link kind you use (Zoom, Meet, Teams). Press Join with the mouse; then repeat with the Return key.
- **Expect:** the meeting opens in the browser or the app you have registered for that link; the tug closes; Return does the same as Join when no button has keyboard focus. No second tug follows for that meeting.
- **Automated:** partly. `tug-join` (exactly one join command, link handed to the launcher), `tug-join-fails`. Not automated: Windows actually opening a real link with the real default handler.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 3.3 A link that cannot be opened is shown to copy

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** make an event whose location holds a link with a scheme Windows cannot open, or temporarily remove your default browser association; press Join.
- **Expect:** "TimeTug couldn’t open the meeting link. Copy it and open it yourself." with the link in a text box and a Copy link button; after Copy, "Link copied".
- **Automated:** `tug-join-fails` (injected launcher failure, screenshot). Left for you: a real failing link, if you can make one.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 3.4 Snooze and Dismiss

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** with a real tug up: open Snooze and pick 1 minute; wait for the tug to come back; press key 5; later press Dismiss, and on another tug press Esc.
- **Expect:** each action closes the tug at once. A snoozed tug comes back after the chosen time. Dismiss and Esc do not bring it back for that meeting. Nothing else changes in your calendar.
- **Automated:** partly. `tug-snooze` and `tug-dismiss` assert exactly one terminal command each through the real controller; `tug-basic` covers the keys. Not automated: the real return of a snoozed tug on the real clock.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 3.5 Test tug from the tray

- **Build:** `msix-x64` (or `msix-arm64`), one account connected.
- **Do:** right-click the tray icon, choose Test tug. Also try the Test tug button on the welcome window’s second step.
- **Expect:** a "Test meeting" tug appears on every monitor; Dismiss closes it; the engine does not keep a stuck request (choose Test tug again and it works again).
- **Automated:** `startup` (the tray’s Test tug through the real engine) and `tray`. Left for you: the real right-click and the real second monitor.

### 3.6 Minute digit in the tray icon

- **Do:** with an account connected, create an event that starts in about 12 minutes. Watch the notification area (pin the TimeTug icon to the taskbar corner first: Settings > Personalization > Taskbar > Other system tray icons). Try display scaling 100, 125, 150 and 200 percent, and a light and a dark taskbar theme.
- **Expect:** at 10 minutes the icon turns to the colour puppy; from 9 minutes it shows a white digit with a dark outline (9, then 8 ... 1) that changes on each whole minute; the tooltip counts the same minutes. In the last minute the digit goes and the icon stays colour. The digit is legible at every scaling.
- **Automated:** `tray-states` (real tray service, tooltip text and icon pixels at each step, pictures in `docs/screenshots/tray-states-enlarged.png`) and the golden pictures in the Platform tests. Left for you: how it looks on the real taskbar.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 3.6 The tug on a real second monitor

- **Build:** `msix-x64` (or `msix-arm64`) on a PC with two displays.
- **Do:** Test tug with both displays on, with different resolutions or scaling if you can.
- **Expect:** the main display has hero, countdown, title, details, Join, Snooze and Dismiss; the other display shows the countdown, the title and "Dismiss on your main display" with no controls. Both cover their whole monitor with no frame or edge line, including the taskbar. Dismissing on the main display closes both.
- **Automated:** partly. `tug-basic` and `tug-monitors` use faked monitors and check window sizes against monitor bounds. The VM has one display. Left for you: real hardware.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 3.7 The tug over a borderless full-screen app

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** run a game or video player in borderless full-screen (windowed full-screen), and separately a maximized browser, and let a real tug become due on top of each (create an event 3 minutes out as in "A meeting that is due soon"). For a quick look, show Test tug first, then start the full-screen app and press Snooze 1 minute.
- **Expect:** the tug appears above both, including above the borderless full-screen app. For an exclusive full-screen game or a presentation, a toast appears instead (see 4.1).
- **Automated:** partly. `tug-toast` covers the refusal path with fake OS state and the real controller; the topmost placement is checked against a maximized Notepad on the VM (not in CI). Not automated: a real borderless full-screen app.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 3.8 Unplugging a display while the tug shows

- **Build:** `msix-x64` (or `msix-arm64`) with two displays.
- **Do:** show the tug (Test tug), then unplug the second display (or turn it off at its cable, or disconnect it in Settings > System > Display), and plug it back in.
- **Expect:** the tug stays on the remaining display with its controls, no crash; the mirror is gone; when the display returns the tug does not duplicate or leave a stale window; Dismiss still works.
- **Automated:** partly. `tug-monitors` replaces the monitor list while the tug shows and checks the windows. Not automated: a real unplug.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 3.9 No keyboard focus: "Click anywhere to respond"

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** type in another app and let a real tug appear (or Test tug while typing).
- **Expect:** if Windows does not give the tug the keyboard, the pill "Click anywhere to respond" shows at the top; Join works by mouse at once; a click on any empty part takes the keyboard and the pill goes away; then Return and Esc work.
- **Automated:** `tug-no-focus` (real mouse click). It is skipped on the ARM64 runner and passes on x64 and the VM. Left for you: whether it really happens on your PC while typing.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

---

## 4. Toasts, Do Not Disturb, lock and unlock

### 4.1 Toast with Do Not Disturb off and on

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** with notifications allowed for TimeTug (Settings > System > Notifications > TimeTug Dev), run an exclusive full-screen game or PowerPoint presenting (the cases Windows will not let a window cover), and let a real tug become due (3.1). Do it once with Do Not Disturb off and once with it on (Settings > System > Notifications > Do not disturb). Then once more with no full-screen app and Do Not Disturb on.
- **Expect:** Do Not Disturb off: a toast with the title, the times and Join, Snooze and Dismiss appears. Do Not Disturb on: Windows suppresses or queues the toast in the notification centre, and the tray icon shows the red attention dot until the tug is dealt with. In both cases, when the full-screen app or presentation ends, the full tug appears. With no full-screen app the full tug appears even under Do Not Disturb, and no toast.
- **Automated:** partly. `toast` (the packaged app registers and Windows accepts a toast) and `tug-toast` (refusal then promotion to the full tug when the screen is free). Not automated: Do Not Disturb, a real full-screen app, how the toast looks.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 4.2 Buttons on a real toast

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** get a toast as in 4.1; press Join on one, choose a snooze time and Snooze on another, and Dismiss on a third.
- **Expect:** Join opens the link and closes the request; Snooze brings the tug back after the chosen time; Dismiss ends it; none opens a second TimeTug (the click is handled by the running app).
- **Automated:** partly. `ITugActions` tests cover that a stale button cannot answer a newer request. Not automated: pressing a real toast button.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 4.3 Lock and unlock during a meeting (late return)

- **Build:** `msix-x64` (or `msix-arm64`) on a real PC.
- **Do:** create an event starting in 4 minutes. Lock the PC (Win+L) before the tug is due. Wait until the meeting has started (say 3 minutes after the start). Unlock.
- **Expect:** nothing appears on the lock screen; right after you unlock, the tug appears with a late countdown in red-orange ("Started 3 min ago") and Join as the primary button. Dismiss it. Repeat, but delete the event from the calendar app on another device while locked: after unlocking no tug appears, and the log says the meeting is no longer on the calendar.
- **Automated:** partly. `tug-late-return` (real engine, clock advanced 10 minutes, unlock: late by exactly 10 minutes, one tug) and `tug-meeting-gone` (deleted while locked). Not automated: a real lock screen and unlock (an unattended session cannot be unlocked).
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 4.4 Sleep and wake

- **Build:** `msix-x64` (or `msix-arm64`) on a real PC.
- **Do:** create an event 10 minutes out, put the PC to sleep for 5 minutes, wake it.
- **Expect:** the app is still running, the tooltip is correct, and the tug appears at the right time (60 seconds before the start) as if nothing happened.
- **Automated:** no. `SystemEventsBridge` tests cover the wake message; the VM cannot sleep.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

---

## 5. The tray

### 5.1 The icon in the notification area and in the overflow

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** start TimeTug. Look near the clock, including the overflow flyout (the up arrow). Drag the icon out to the taskbar. Then switch Settings > Personalization > Colors > "Choose your mode" and the Windows taskbar theme (light and dark) independently.
- **Expect:** a puppy icon appears (in the overflow until you pin it). It stays readable on a light and on a dark taskbar: dark puppy on a light taskbar, light puppy on a dark one. At 100, 125, 150 and 200 percent display scaling it is sharp.
- **Automated:** partly. `tray` (icons exist at 16, 20, 24 and 32 px in every variant, the tray view model, the clicks), `startup` (the icon is created or the failure is recorded). The VM hides its taskbar, so no picture of the icon exists. Left for you: seeing it.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.2 Tooltip, icon states and attention dot

- **Build:** `msix-x64` (or `msix-arm64`), one account connected.
- **Do:** hover the icon with a meeting later today, again with a meeting less than 10 minutes away, again just after turning Enable Tug off, and again while a toast was refused (4.1).
- **Expect:** the tooltip reads like "Design review in 4 min" and updates each minute ("starts now", "has started", "· Tug is off"); within 10 minutes of a meeting the icon is the colour puppy; with a refused toast or a held tug it shows the red attention dot until dealt with.
- **Automated:** partly. `tray` asserts the tooltip texts, icon choice and attention state from the view model. Left for you: that Windows shows them.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.3 Clicking each tray menu row

- **Build:** `msix-x64` (or `msix-arm64`), one account connected.
- **Do:** right-click the icon. Click each row once, reopening the menu between clicks: the first row (Next or "No upcoming meetings"), Enable Tug (twice), Test tug, Settings…, and click elsewhere to close the menu.
- **Expect:** rows in this order: Next (greyed, information), Enable Tug (checked), Test tug, Settings… (greyed, slice 3), Quit TimeTug, with separators between groups. Clicking Next or Settings… does nothing. Enable Tug toggles its check, and with it off no tug appears for a meeting (turn it back on). Test tug shows the tug. A left click with no account opens the welcome window, with an account it opens the flyout (and closes it when it is open).
- **Automated:** partly. `tray` and the tray view-model tests invoke every row’s command and check the engine commands and the check mark. The menu rows could not be clicked by a script: the VM session’s injected input does not reach a native popup menu. Not automated: the real clicks.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.4 Quit from the tray menu

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** right-click the icon, choose Quit TimeTug. With a test tug showing, quit again from the menu if the menu is reachable.
- **Expect:** the icon disappears at once (no ghost icon left when you hover the notification area), any open tug or welcome window closes, and no TimeTug process remains in Task Manager.
- **Automated:** partly. `startup` quits through the same path (`AppShell.QuitAsync` with an outstanding test takeover: closed first, tray removed, windows closed, no error). Not automated: the real menu click.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.5 Explorer restart and an update

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** Task Manager, Windows Explorer, Restart. Later install a newer artifact over the old one.
- **Expect:** after the Explorer restart the icon comes back by itself. After an update the notification-area "Always show" choice may need to be switched on again (Windows ties it to the package path); note whether it did.
- **Automated:** no (H.NotifyIcon recreates the icon on TaskbarCreated; not exercised on CI, which has no taskbar).
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.6 The flyout next to the icon

- **Build:** `msix-x64` (or `msix-arm64`), one account connected and a calendar with a few meetings today.
- **Do:** left-click the tray icon with the icon in the overflow, then pinned to the taskbar; again with the taskbar at 100, 150 and 200 percent scaling, with the taskbar set to hide itself, and (if you have two monitors) with the icon's monitor not the main one. Press Esc, click elsewhere, and click the icon twice in a row. Press Tab and Enter on a Join button. Open a maximized window first.
- **Expect:** the flyout opens above the icon (or the overflow button) on that monitor with an even gap, 360 DIP wide and never taller than 520 DIP (a long day scrolls under a fixed header), over the maximized window and not behind it. Esc, clicking elsewhere and a second click on the icon close it (the second click does not close it and open it again). It has no taskbar button and is not in Alt+Tab. With transparency effects off or a contrast theme it is a solid fill; with animations off it appears without sliding. When a tug appears it closes and cannot be opened over the tug.
- **Automated:** partly. `flyout-empty`, `flyout-day`, `flyout-problems`, `flyout-tug-wins` and `flyout-anchor` open the real window and check text, placement, keyboard focus, Join and the screenshots; `FlyoutPlacementTests` cover every taskbar edge, several monitors and scales; the axe-windows scan reads the flyout. Not automated: clicking the real icon, pinned versus overflow on other machines, a second monitor.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### Slice 2 at a glance

| Human check | Where |
|---|---|
| Flyout from a tray icon in the overflow and when pinned | 5.6 |
| Flyout beside a side taskbar and on a second monitor | 5.7 |
| Join from a card | 5.8 |
| Auth-expired row and Reconnect | 5.9 |
| The minute digit in the real tray icon in the last ten minutes | 3.6 |
| Hotkey set through the dev store, and a conflict with another program's key | 5.10 |
| Second launch while a tug shows | 5.11 |
| Flyout with Narrator | 5.12 |
| Clock changed backwards while the app runs, then second launch and tray click | 5.13 |
| Launch at login across a real sign-out and sign-in, and a task turned off in Task Manager | 5.15 |
| The shortcut recorder with a real keyboard: record, the same chord again, Escape, a conflict with another program, a restart | 5.16 |
| About and Software Update: the picture, the version, the links, the Store button | 5.17 |
| The Appearance pane against real Windows settings: app mode switched while Settings is open, a real contrast theme, transparency effects off, the minute digit in the real tray | 5.18 |

### 5.7 The flyout beside a side or top taskbar, and on a second monitor

- **Build:** `msix-x64` (or `msix-arm64`), one account connected and a calendar with a few meetings today.
- **Do:** (a) Windows 10, or Windows 11 with a tool that moves the taskbar (stock Windows 11 keeps it at the bottom, so skip this part there): put the taskbar on the left, then the right, then the top (right-click the taskbar > Taskbar settings > Taskbar location on screen). Left-click the TimeTug icon each time, with the icon in the overflow and pinned. (b) With two monitors, pin the icon on the secondary monitor's taskbar (Settings > Personalization > Taskbar > Taskbar behaviors > Show my taskbar on all displays), set that monitor to a different scaling than the main one, and left-click the icon there. Put the taskbar back at the bottom afterwards.
- **Expect:** (a) the flyout sits beside the icon on the side the taskbar is on (to the right of a left taskbar, to the left of a right taskbar, below a top taskbar), 8 DIP from it, fully inside the work area, never under the taskbar. (b) it opens on the monitor holding the icon, at that monitor's scaling (360 DIP wide in that monitor's pixels), not on the main monitor.
- **Automated:** partly. `FlyoutPlacementTests` cover every edge, several monitors, mixed scales and negative coordinates; `flyout-day` checks the real window's position against the real work area on the VM. Not automated: a real moved taskbar and a real second monitor.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.8 Join from a card

- **Build:** `msix-x64` (or `msix-arm64`), a Google, Microsoft or iCloud calendar.
- **Do:** create two events starting within the next hour, one with a Google Meet or Zoom link in its description or location, one without. Left-click the tray icon. Click Join on the first card. Open the flyout again; for a failure case, set a default app for `https` links that cannot open (or block the browser) and click Join again.
- **Expect:** only the current meeting and the next meeting show a Join button, and only when they have a link; the card with no link has none. Join opens the link in the default browser or conferencing app and closes the flyout. When Windows cannot open the link the flyout stays open and the card shows "TimeTug couldn't open the meeting link. Copy it and open it yourself." with the link in a box and a Copy link button that puts the link on the clipboard ("Link copied"). Pressing Tab then Enter on the Join button does the same as the click.
- **Automated:** partly. `FlyoutViewModelTests` (Join, failure, no engine command sent), `flyout-day` and `flyout-problems` (the real window's Join and the failure card). Not automated: a real link opening in a real browser or app.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.9 An expired sign-in: the problem row and Reconnect

- **Build:** `msix-x64` (or `msix-arm64`) with Google or Microsoft configured (`dev-oauth.json` on the PC, see `docs/development.md`).
- **Do:** connect the account. In the provider's account page remove TimeTug's access (Google: myaccount.google.com/permissions > TimeTug > Delete all connections; Microsoft: account.live.com/consent/Manage). Restart TimeTug and wait for its next calendar refresh (up to a few minutes), then left-click the tray icon. Press "Sign in again" and finish the browser sign-in.
- **Expect:** an amber row above the header reads "<account> needs you to sign in again" with a "Sign in again" button (a failing source without that cause shows "<account> failed" and no button). The button opens the browser sign-in for that account; after it succeeds the row disappears and the day's meetings come back without restarting. The row is a group named "Calendar problems" for Narrator.
- **Automated:** partly. `FlyoutViewModelTests` (a row for each problem kind, Reconnect calls the engine), `flyout-problems` (two rows, screenshot, `docs/screenshots/flyout-problems.png`). Not automated: a real revoked token and the real browser round trip.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.10 The popup hotkey: set through the dev store, and a conflict with another program

- **Build:** `msix-x64` (or `msix-arm64`); the CI development package reads the dev store, and the Settings recorder (5.16) is the way a person sets it.
- **Do:** (a) quit TimeTug from its tray menu; create `%LOCALAPPDATA%\TimeTug\dev-hotkey.txt` containing one line, `Ctrl+Alt+T`; start TimeTug; press Ctrl+Alt+T twice, with another window focused. (b) Quit TimeTug; start another program that already uses Ctrl+Alt+T (AutoHotkey with `^!t::MsgBox`, or a text expander); start TimeTug; press Ctrl+Alt+T. (c) Quit both, delete `dev-hotkey.txt`, start TimeTug and press Ctrl+Alt+T.
- **Expect:** (a) the first press opens the flyout (near the icon, even with the icon in the overflow), the second closes it; while a tug shows the press does nothing. (b) TimeTug starts normally and the other program keeps the key; TimeTug's log (`%LOCALAPPDATA%\Packages\BinaryCompanion.TimeTug.Dev_68z4tsj3tx7ew\LocalState\logs\timetug.log`) has a warning "the shortcut Ctrl+Alt+T is used by another program, so it is inactive" and no other message names that program; the state is kept for the Settings page of slice 3. (c) nothing happens, as on the Mac (no default shortcut).
- **Automated:** partly. `HotkeyServiceTests` and `DevHotkeyStoreTests`; the `hotkey` scenario registers a real chord, takes the conflict path when another process holds it (forced once on the VM), and `compose` presses it in the real shell; on the VM the dev file registers the chord (a second process then cannot). Not automated: real key presses with a real keyboard layout, and a real other program's key.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.11 A second launch while a tug shows

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** right-click the tray icon > Test tug. While the tug is on screen start TimeTug again from the Start menu (or run `timetug-dev.exe`). Dismiss the tug. Start TimeTug again.
- **Expect:** the second start shows nothing and its process ends by itself within a couple of seconds (Task Manager: still one TimeTug); the tug stays exactly as it was (same window, still counting, keyboard still working) and no flyout opens. After the tug is gone the next start opens the flyout. The log has "a second launch was ignored because the tug is showing".
- **Automated:** yes for the logic and the real launches: `second-launch` (starts the packaged app through its alias during a tug: exit code 0, one process, no flyout, the tug unaffected, nothing sent to the engine) and `AppInstanceServiceTests`. Left for you: the real Start-menu launch.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.12 The flyout with Narrator

- **Build:** `msix-x64` (or `msix-arm64`), an account with a current meeting, a next meeting with a link, and one problem row (see 5.9).
- **Do:** turn Narrator on (Win+Ctrl+Enter). Open the flyout from the tray icon. Move with Tab and Shift+Tab, with Narrator's item navigation (Caps Lock+Right arrow), then press Enter on Join and Esc.
- **Expect:** Narrator says the window opened and reads the header (weekday and "N meetings left"); each card is read as one item: title, time range, how soon ("starts in 12 minutes" or "now"), state, calendar and attendees; Join reads "Join <title>" and works with Enter; the problem group is read as "Calendar problems" with its Sign in again button; the Settings button is read as unavailable; Esc closes the flyout and focus returns to the previous window. Nothing is read twice or as "unlabelled".
- **Automated:** partly. `a11y-hold` round 3 runs axe-windows over the flyout (a problem row and a long day) with no error-level violation; `FlyoutViewModelTests` assert each row's spoken description. Not automated: what Narrator actually says and where focus goes.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.13 The clock changed backwards while the app runs

- **Build:** `msix-x64` (or `msix-arm64`), an account connected, a hotkey set as in 5.10 (a).
- **Do:** with TimeTug running, turn off Settings > Time & language > Date & time > Set time automatically, and set the clock back one hour (Change date and time). Within a minute: click the tray icon, close it by clicking elsewhere, click the icon again; press the hotkey; start TimeTug again from the Start menu. Then set the clock back to the right time and turn automatic time on again.
- **Expect:** every click, hotkey press and second start still works at once: the flyout opens each time (a click right after the flyout closed by clicking elsewhere is still treated as that same click and does not reopen it, within about half a second), and no launch is lost. The tooltip and the flyout rows follow the engine's new state, not a stale one.
- **Automated:** partly. `FlyoutHostTests` and `AppInstanceServiceTests` step the wall clock backwards and forwards while real time passes (the click grace and the launch coalescing now use a monotonic timer). Not automated: a real clock change reaching the engine.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.14 The Accounts pane: remove an account, Credential Locker, Copy Diagnostics

- **Build:** `msix-x64` (or `msix-arm64`), signed in to a real account you can add again.
- **Do:** (a) Open Settings > Accounts from the tray. Read each account's status. Let a token expire if you can (revoke TimeTug's access in the provider's account page) and look for "Sign in again" on that account only. (b) From the menu (the three dots) choose Remove account, then Cancel; again, then Remove. Open Control Panel > Credential Manager > Windows Credentials and look for entries named `TimeTug` before and after. Remove the last account: the pane shows "No internet accounts yet. Add one below." (c) Add the account again from the Add an Account cards. Press Cancel while the browser sign-in waits. (d) Press Copy Diagnostics and paste into Notepad; press Clear and copy again. (e) With one account that cannot be reached (turn the network off), open the flyout and press "Open Accounts" on its row.
- **Expect:** (a) "Sign in again" only on an expired account (or one that cannot start). (b) the question names the account and says its calendars no longer appear and their Tug and visibility choices are forgotten; Cancel changes nothing; after Remove the account's `TimeTug` credential entries are gone from Credential Manager and the list follows the engine. (c) no second sign-in can start while one waits; Cancel stops the wait without an error. (d) the pasted text starts with "TimeTug diagnostics", the app version and the Windows version, followed by recent log lines with no link that has a `?`, no token and no event or calendar name; after Clear the next copy holds only the header. (e) the flyout closes and the Accounts pane opens with that account tinted for a moment.
- **Automated:** partly. `settings-accounts` drives every part of this on scripted data, and against the real engine it removes the account and asserts that the engine asked the host's credential store to remove it. Left for you: the real Credential Locker entries, the real clipboard and the real provider sign-in.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.15 Launch at login: a real sign-in, and turning it off in Windows

- **Build:** `msix-x64` (or `msix-arm64`), a PC where you can sign out and in. Note the state of Settings > Apps > Startup > TimeTug (development) before you begin: it is off for a new install.
- **Do:** (a) Open Settings (tray > Settings) > General and turn on Launch at login. Look at Windows Settings > Apps > Startup and Task Manager > Startup apps: TimeTug is listed as Enabled. Sign out and sign in again. (b) After signing in, look at the tray. (c) In Task Manager > Startup apps right-click TimeTug > Disable, then bring TimeTug's Settings window to the front again (click another window, then it). (d) Click "Open Startup apps". Turn it on in Windows Settings, come back to TimeTug's Settings window. (e) Turn Launch at login off again in TimeTug's Settings (or in Windows).
- **Expect:** (a) the switch stays on and Windows lists TimeTug as Enabled. (b) TimeTug is running in the tray after sign-in with no window opened (no welcome window if an account exists). (c) the switch goes to off and is disabled, with "Windows is blocking TimeTug from starting when you sign in. Turn it on in Windows Settings under Startup apps." and the link. (d) the link opens Windows Settings on Startup apps; after you turn TimeTug on there and return, the switch shows on and can be used again. (e) off, and Windows lists it as Disabled.
- **Automated:** partly. `settings-general` draws the page against a scripted startup task in every state Windows reports and writes the machine's real task state to `result.json`; when that is Disabled it enables it through the app's own `StartupTask` call, reads it back and the package's registry key (`HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\<package family>\TimeTugStartup`, value `State`: 0 off, 2 on), and turns it off again. `welcome` ticks the checkbox. Not automated: an actual sign-out and sign-in (no VM reboot was done), Task Manager's own Startup tab and a real policy.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.16 The shortcut recorder with a real keyboard

- **Build:** `msix-x64` (or `msix-arm64`), no `dev-hotkey.txt` (delete `%LOCALAPPDATA%\TimeTug\dev-hotkey.txt` if it exists). Another program that can hold a key (AutoHotkey with `^!m::MsgBox`) for step (d).
- **Do:** (a) Settings > General > Shortcut: click the button that says "Record shortcut", press only Ctrl, then press a bare letter, then press Ctrl+Alt+T. (b) Press Ctrl+Alt+T in another window: the flyout opens; again: it closes. Back in Settings click the button and press Ctrl+Alt+T again (the chord that is registered); click it and press Esc; click it and click elsewhere in the window. Check the flyout still opens on Ctrl+Alt+T. (c) Click the button and press Backspace; then record a shortcut again and click the ✕ button. (d) Start the other program holding Ctrl+Alt+M, record Ctrl+Alt+M in TimeTug. Quit TimeTug and start it again with the other program still running, and record Ctrl+Alt+T, quit, let the other program hold Ctrl+Alt+T, start TimeTug and open Settings > General.
- **Expect:** (a) the button reads "Ctrl+…" while only Ctrl is down, a bare letter says a shortcut needs Ctrl, Alt, Shift or the Windows key, and Ctrl+Alt+T is accepted ("Shortcut is active."). (b) the flyout toggles on Ctrl+Alt+T from any window; recording the same chord works and the shortcut keeps working afterwards; Esc and clicking away leave it as it was and working. (c) Backspace and ✕ remove it ("Shortcut cleared."), the key no longer opens the flyout. (d) "Ctrl+Alt+M is already used by another program. Ctrl+Alt+T still works." and the old shortcut keeps working; after the restart with the other program holding the stored chord the row says "Ctrl+Alt+T is already used by another program, so it is inactive. Record a different shortcut." and the key does nothing.
- **Automated:** partly. `HotkeyRecorderViewModelTests` (30), `HotkeyServiceTests` (suspend and resume), the `hotkey` and `settings-general` scenarios (a second registrar proves the registration is released while recording and held again after every way out; a real `RegisterHotKey` conflict; one real `SendInput` chord reached the recorder on the VM). Not automated: a real keyboard layout, the Windows key (the shell keeps some Win chords), and another program's real key.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.17 About and Software Update

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** open Settings > General. Open About (read it, press Website and Licences), go back, open Software Update and press "Open Microsoft Store updates". Turn off the network and press Website again.
- **Expect:** About shows the hero picture, the icon and "TimeTug", the tagline, the version of the installed package (Settings > Apps > Installed apps shows the same number), the credit, the copyright and the licence line; Website opens https://binarycompanion.com/ and Licences the LICENSE file on GitHub in the default browser; Software Update says "TimeTug <version>" and, for this development build, "Development build <commit>. Updates are installed by hand, not from the Store." with no Store button (a Store build says updates come from the Microsoft Store and the button opens the Store's Downloads page); with no network the browser, not TimeTug, shows the error. Every control reads sensibly with Narrator.
- **Automated:** partly. `AboutViewModelTests`, `settings-general` (the texts, the links through a recording launcher, a link that fails, the development and Store variants) and axe round 9 on the About page. Not automated: the real browser and Store opening, and what Narrator says.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.18 The Appearance pane against real Windows settings

- **Build:** `msix-x64` (or `msix-arm64`). Windows 11.
- **Do:** (a) Open Settings > Appearance with Theme on "Use system". In Windows Settings > Personalization > Colors change "Choose your mode" between Light and Dark while TimeTug's Settings window and the flyout (open it from the tray) stay open. (b) Choose Dark in TimeTug while Windows is Light, close and open the Settings window and the welcome window (tray icon with no account), and restart TimeTug. (c) Turn on a contrast theme (Settings > Accessibility > Contrast themes), look at the Settings window and the flyout, turn it off. (d) Turn Transparency effects off (Settings > Accessibility > Visual effects) with Acrylic chosen and open the flyout. (e) Choose "Icon only", then add a meeting that starts in 6 minutes and watch the tray icon; choose "Icon with minute digit".
- **Expect:** (a) the windows follow at once, caption included (a dark caption bar with light caption buttons in dark, a light one in light); (b) Dark is kept, the caption of every TimeTug window is dark from the moment it opens, nothing flashes light; (c) every window uses the contrast theme's own colours, caption included, and the flyout is solid; the pane says a high-contrast theme overrides the backdrop; (d) the flyout is solid and the pane says so; (e) with Icon only the colour puppy shows without a digit and the tooltip still counts the minutes; with the digit style the whole minutes (6, then 5...) are on the icon.
- **Automated:** partly. `settings-appearance` (a fake system theme, scripted settings: each choice applied to the open windows, caption colours read from the window and from a screenshot, overrides), `tray-states`, `AppearanceViewModelTests`, `AppearanceResolverTests`, axe rounds 10 and 11. Not automated: a real change of the Windows theme, a real contrast theme, and the look of Mica and acrylic (PrintWindow cannot capture them).
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 5.19 Merge actions on a real duplicate meeting

- **Build:** `msix-x64` (or `msix-arm64`), with a Google, Microsoft or CalDAV account that has the same meeting on two calendars (for example a shared invitation that also sits on your own calendar), and a second pair of meetings that overlap but are not copies.
- **Do:** (a) Open the flyout from the tray. The doubled meeting shows once with "N events merged" (or "Merged with ..." when on-device intelligence merged it) and the calendars it is on. (b) Press the "..." button on it (or right-click it, or Tab to the button and press Enter) and open "Not the same meeting": pick one calendar's copy. (c) On the overlapping pair press "..." > "Merge with..." and pick the other meeting. (d) On the merged card press "..." > "Unmerge". (e) With the menu open, click another window, then reopen the flyout and open a menu again. (f) Open the menu on a card, then let the minute change (or wait for the clock to tick) before choosing.
- **Expect:** (b) the picked copy becomes its own card and the rest stay merged (or all separate when only two); (c) one card, "Merged manually · 2 events"; (d) the copies are separate again and stay separate; (e) opening a menu does not close the flyout; clicking another window closes the flyout and the menu together; (f) the menu stays open until it is closed, then the list refreshes. Every item reads sensibly with Narrator (the button says "Merge actions for <title>"; items name the calendars and times). When a meeting changed on another device while the menu was open, choosing it shows "That meeting has changed. The list is up to date." and nothing else happens.
- **Automated:** partly. `MergeManagementTests` (members, candidates, badge text, commands with ids only, a stale card, the engine's refusal), `flyout-merge` (the real engine merging, splitting and re-merging; the stale message; the engine's refusals) and axe round 12 with the menu open. Not automated: a real mouse (hover opening the submenu, click choosing an item, Esc), a real click-away with a menu open, and Narrator.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

---

## 6. Accessibility and appearance

### 6.1 Narrator reads the tug

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** turn Narrator on (Win+Ctrl+Enter), show a tug (Test tug or a real meeting). Use Tab, Return, Esc and the keys 1, 5, 0.
- **Expect:** Narrator announces the tug as it appears ("Test meeting starts in 4 minutes" or the real title and time), reads every control by its name (Join meeting, Snooze this reminder, Dismiss this reminder) and the hints; the keys work as on screen.
- **Automated:** partly. `a11y-hold` runs axe-windows over the welcome window and the tug (canary round, then two rounds, no error-level violation); the announcement and automation names are asserted in `tug-basic`. Not automated: what Narrator actually says.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 6.2 High contrast

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** Settings > Accessibility > Contrast themes: turn on Aquatic, then Night sky, then a light one (Desert). Show the tug and open the welcome window each time.
- **Expect:** the tug uses the system colours (Window and WindowText, outlined buttons, a filled Join), every word and control readable, no navy or acrylic left; the welcome window follows the theme; turning the theme off and on while a tug is up restyles it.
- **Automated:** partly. `tug-contrast-and-motion` (contrast theme screenshots and checks). Left for you: the real themes on a real display.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 6.3 Transparency effects off

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** Settings > Personalization > Colors > Transparency effects off; show the tug. Then on again.
- **Expect:** off: a solid navy backdrop with no blur; on: navy tinted acrylic with the desktop blurred behind it.
- **Automated:** partly. `tug-contrast-and-motion` (solid backdrop chosen and screenshot). Acrylic cannot be told apart in CI captures. Left for you: the look.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 6.4 Reduce motion

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** Settings > Accessibility > Visual effects > Animation effects off; show a tug in its last minute.
- **Expect:** the countdown does not pulse and nothing animates; with animation effects on, the last-minute countdown pulses gently.
- **Automated:** partly. `tug-contrast-and-motion` asserts the pulse flag follows the setting. Left for you: seeing it.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

### 6.5 Scaling and keyboard-only use

- **Build:** `msix-x64` (or `msix-arm64`).
- **Do:** set display scaling to 150 and 200 percent and text size to 150 percent (Settings > Accessibility > Text size); open the welcome window and show a tug. Then use only the keyboard: Tab through the welcome window, Return on a provider, Esc and Return on the tug.
- **Expect:** nothing is cut off or overlapping; focus is always visible; every action is reachable without the mouse (Join by Return, Dismiss by Esc, snooze by 1, 5, 0).
- **Automated:** partly. axe-windows (`a11y-hold`) checks names, roles and keyboard focus rules. Left for you: the look at large scaling.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________

---

## 7. Failure cases on a managed PC

### 7.1 Credential Locker or the loopback address blocked

- **Build:** `msix-x64` (or `msix-arm64`) on a work PC with policies, if you have one.
- **Do:** try Google or Microsoft sign-in and an iCloud login.
- **Expect:** if the browser cannot reach the local redirect or Credential Locker is blocked, the accounts UI says so clearly and the log names the step; other accounts keep working; the app does not crash.
- **Automated:** partly. Platform tests simulate a blocked store and a failing loopback. Not automated: a real locked-down PC.
- **Record:** ☐ pass ☐ fail · Date: ________ · Commit: ________ · Notes: ________
