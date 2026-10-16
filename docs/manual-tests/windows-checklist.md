# Windows manual test checklist

The owner's checks for what automation cannot do (real accounts, how it feels). It grows with each slice.

## First run

- Install the development package and start it from the Start menu with no accounts: the welcome window opens in the middle of the screen, with a taskbar button. Google and Microsoft show "isn’t set up in this build" unless `%LOCALAPPDATA%\TimeTug\dev-oauth.json` exists (see `docs/development.md`).
- Close the welcome window: TimeTug keeps running with its icon in the notification area; start it a second time: nothing appears and the second copy exits. Quit from the tray menu.
- Click Test tug on the welcome window’s second step (after connecting an account): the tug appears on every monitor, Dismiss closes it.

## Accounts

## Tug screen

- With a tug showing on a real second monitor: the main display has hero, countdown, title, details, Join, Snooze and Dismiss; the other display shows the countdown, the title and "Dismiss on your main display". Both cover the whole monitor including the taskbar and sit above a maximized window and a borderless full-screen app.
- Unplug the second monitor while the tug is up: the tug stays on the remaining display with its controls and nothing crashes.
- Start the tug while typing in another program: if Windows does not give it the keyboard, "Click anywhere to respond" shows; a click on any part of the screen takes the keyboard and the banner goes away; Join works with the mouse before that.
- Narrator on: the tug is announced ("<title> starts in 4 minutes"), every control is read with its name, and Tab, Return, Esc and 1, 5, 0 work.
- Settings > Accessibility > Contrast themes on: the tug uses the theme colours. Settings > Personalization > Colors > Transparency effects off: the tug is solid navy. Settings > Accessibility > Visual effects > Animation effects off: the last-minute countdown does not pulse.
- Make Join fail (a link with an unknown scheme): the link is shown with a Copy link button.

- Lock the PC (Win+L) with a tug due, wait for the meeting to start, sign in: the tug appears with the countdown showing how late it is . Lock and unlock are not exercised by automation because an unattended session cannot be unlocked.
- Turn on Do Not Disturb, run a game or presentation in exclusive full-screen with a tug due: a toast with Join, Snooze and Dismiss appears or is queued; leaving full-screen shows the full tug.
- Press Join, Snooze and Dismiss on a real toast (the self-test checks only that Windows accepts the toast).

## Tray

- Start TimeTug: a puppy icon appears in the notification area (it may be in the overflow flyout until pinned: drag it out to keep it visible). It follows the taskbar theme: switch Settings > Personalization > Colors > "Choose your mode" and the Windows taskbar theme separately and check the icon stays readable (dark puppy on a light taskbar, light puppy on a dark one).
- Hover the icon: the tooltip names the next meeting and the minutes left ("Design review in 4 min"); it updates each minute.
- Right-click: Next (greyed), Enable Tug (checked), Test tug, Settings… (greyed), Quit TimeTug. Turn Enable Tug off and on again: the check follows. Test tug shows the tug. Quit removes the icon at once and ends the process (no TimeTug in Task Manager).
- With a meeting less than ten minutes away the icon turns to the colour puppy and the first row reads "Join <title>" when the meeting has a link (the row is information only until the flyout arrives).
- Left-click with no account connected: the welcome window opens. With an account nothing happens yet.
- Make a toast get refused (Do Not Disturb with a tug due): the icon shows the red attention dot until the tug is dealt with.
- Display scaling 100, 125, 150 and 200 percent: the icon is sharp at each (it has 16, 20, 24 and 32 px pictures).
- Restart Explorer (Task Manager > Windows Explorer > Restart): the icon comes back.
- After an update the notification-area setting "Always show" may need to be switched on again (Windows ties it to the package path).
