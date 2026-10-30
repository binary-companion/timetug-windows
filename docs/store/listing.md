# Microsoft Store listing: text and links (draft for the owner)

Everything here is a draft that the owner pastes into Partner Center when the first submission is prepared (phase 9). Nothing is submitted from this repository. Values that only Partner Center can give (the reserved name, the identity) are not here: they go into the release environment (`docs/release.md`).

The wording follows the Mac listing text (the README and the website's home page) and says only what the Windows app does today. The "not in 1.0" list at the end is what must not be promised. Re-check this file against `docs/parity.md` before submitting.

## Product

| Field | Value |
| --- | --- |
| Name | TimeTug (reserve this first; if it is taken, the reserved name goes into `PARTNER_CENTER_APP_NAME`) |
| Category | Productivity |
| Publisher | Binary Companion LLC (as the Partner Center account shows it) |
| Price | Free |
| Website | <https://timetug.binarycompanion.com> |
| Support contact | <sobryan@binarycompanion.com> (the address the website uses), and the website |
| Privacy policy URL | <https://timetug.binarycompanion.com/privacy> |
| Copyright | Copyright (c) 2026 Binary Companion LLC |
| Minimum Windows | Windows 11 22H2 (build 22621) |
| Architectures | x64 and ARM64 (one bundle) |

The privacy policy page at that address currently describes the Mac app ("TimeTug is a macOS app", Keychain). The Store requires a policy that covers this app: it must name Windows, say that tokens and passwords are kept in Windows Credential Locker, and keep the rest (what is read, what is stored on the PC, what is removed on uninstall). That is a change to the website (`binary-companion/website` or `timetug`'s `site/`) and the owner's to approve; it is a phase 9 item and a blocker for submission.

## Short description (100 characters at most)

A tug when time needs your attention: your meetings, and a screen takeover just before one starts.

(98 characters.)

## Description

TimeTug lives in your notification area, lists today's meetings, and takes over every screen shortly before one starts, so you don't hyperfocus straight through it.

Built to interrupt you on purpose
- Full-screen takeover on every display, at a lead time you choose (0 minutes means "starting now"). Join, Snooze or Dismiss, without digging through tabs.
- One-click Join. Zoom, Google Meet, Microsoft Teams, Webex and similar links are found automatically and the Join button is right in front of you.
- No duplicate tugs. The same meeting on two calendars, such as a personal invite and a team calendar copy, is merged so you are interrupted once. Merged meetings show how they were merged, and you can split them, unmerge them or merge two yourself.
- Per-calendar control. Choose which calendars can tug you at all. All-day events never take over and declined events are skipped. Optional rules: require a video link, or other attendees.
- One switch to go quiet. Turn off Enable Tug to stop every takeover at once.

Always in reach
- The tray icon shows the minutes to your next meeting; click it for today's agenda.
- A small countdown chip above the notification area, if you want one (Off by default).
- Right-click the TimeTug taskbar button for Open today's meetings, Join next meeting, Test tug and Settings.
- An optional global keyboard shortcut for today's meetings, and an option to start TimeTug when you sign in (off until you turn it on).
- Light, Dark or your Windows setting; follows contrast themes, transparency and animation settings.

Connect the calendars you already use
- Google Calendar and Microsoft (Outlook) calendars: sign in with your browser; TimeTug reads events with read-only permission.
- iCloud, with an app-specific password, and other CalDAV calendars such as Fastmail or Nextcloud.

Your calendar data stays on your PC. TimeTug talks to your calendar provider directly, keeps its working data in its own folder on your PC and keeps sign-in tokens and passwords in Windows Credential Locker. There are no TimeTug accounts and no TimeTug servers that see your calendar. Removing the app removes its data.

## What's new in the first version

First release for Windows: the day's agenda, the screen takeover with Join, Snooze and Dismiss, Google, Microsoft, iCloud and CalDAV calendars, merged duplicate meetings, a tray icon with a live countdown, a taskbar jump list, an optional countdown chip, and a full Settings window.

## Search keywords (Partner Center takes up to 7, each at most 30 characters)

meetings, calendar, reminder, countdown, focus, join meeting, agenda

## Not in this version (do not promise)

- Windows account sign-in and the accounts already on the PC (phase 6), Windows widgets (phase 7), on-device intelligence for duplicate meetings (phase 8). Duplicate meetings are merged by rules and by hand only.
- Apple Calendar, Reminders-style local calendars, and Outlook classic PST calendars: there is no equivalent on Windows (`docs/parity.md`).
- Automatic update choices inside the app: the Store updates the app.

## Notes for certification (type these into Partner Center; no credentials belong in the repository)

TimeTug reads calendars the person connects. To exercise it a tester needs a calendar account: the owner should prepare a dedicated test account (for example an iCloud account with an app-specific password, or a CalDAV account) with a meeting that starts in a few minutes, and enter its details in the "Notes for certification" box in Partner Center, never in this repository. Settings > Tug Rules > Test tug shows the takeover without any calendar.
