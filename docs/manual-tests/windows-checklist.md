# Windows manual test checklist (slice 1)

Every check a person has to do for slice 1: real accounts, real monitors, how it feels. The self-test scenarios (`docs/development.md`) already cover everything that can be scripted; each check below says what automation covers and what is left for you. The list grows with each slice.

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
- **Expect:** no welcome window. The tray icon appears and its tooltip names the next meeting. Left-click on the icon does nothing yet (the flyout window exists; the click is wired in slice 2).
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
- **Expect:** rows in this order: Next (greyed, information), Enable Tug (checked), Test tug, Settings… (greyed, slice 3), Quit TimeTug, with separators between groups. Clicking Next or Settings… does nothing. Enable Tug toggles its check, and with it off no tug appears for a meeting (turn it back on). Test tug shows the tug. A left click with no account opens the welcome window, with an account does nothing.
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

### 5.6 The flyout next to the icon (once the left click opens it, slice 2 task 8)

- **Build:** `msix-x64` (or `msix-arm64`), one account connected and a calendar with a few meetings today.
- **Do:** left-click the tray icon with the icon in the overflow, then pinned to the taskbar; again with the taskbar at 100, 150 and 200 percent scaling, with the taskbar set to hide itself, and (if you have two monitors) with the icon's monitor not the main one. Press Esc, click elsewhere, and click the icon twice in a row. Press Tab and Enter on a Join button. Open a maximized window first.
- **Expect:** the flyout opens above the icon (or the overflow button) on that monitor with an even gap, 360 DIP wide and never taller than 520 DIP (a long day scrolls under a fixed header), over the maximized window and not behind it. Esc, clicking elsewhere and a second click on the icon close it (the second click does not close it and open it again). It has no taskbar button and is not in Alt+Tab. With transparency effects off or a contrast theme it is a solid fill; with animations off it appears without sliding. When a tug appears it closes and cannot be opened over the tug.
- **Automated:** partly. `flyout-empty`, `flyout-day`, `flyout-problems`, `flyout-tug-wins` and `flyout-anchor` open the real window and check text, placement, keyboard focus, Join and the screenshots; `FlyoutPlacementTests` cover every taskbar edge, several monitors and scales; the axe-windows scan reads the flyout. Not automated: clicking the real icon, pinned versus overflow on other machines, a second monitor.
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
