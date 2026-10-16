# Windows manual test checklist

The owner's checks for what automation cannot do (real accounts, how it feels). It grows with each slice.

## First run

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
