# The Windows calendar store as a fallback source (2026-11-01)

Task 1A of the phase 6 plan (`docs/superpowers/plans/2026-11-01-phase-6-wam-sign-in.md` in `binary-companion/timetug`). It adds findings and a recommendation only; no product code was kept. Measured on the Windows 11 ARM64 VM (build 26200) with a throwaway probe in the packaged development app (scratch branch, not merged), calling `AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly)` with the `appointments` capability in the spike manifest. The probe records counts only: no title, attendee, location text or account name was written down, and this document has none.

## What was and was not observed

The VM has one Windows sign-in account (a personal Microsoft account) and no other account added under Settings > Accounts > Email & accounts, so the questions that depend on several real accounts (C2 for Google and for a local calendar, C3) were not answerable by the agent. Nothing was changed in Windows privacy settings.

## Answers

**C1 Consent.** Access was neither refused nor prompted: `RequestStoreAsync` returned a store on the first call with no dialog. The global switch (Settings > Privacy & security > Calendar, `ConsentStore\appointments`) was `Allow`; the per-app list in the registry held entries only for the People app and the retired Mail and Calendar app (both `Prompt`), none for the TimeTug development package, so a new app is not given a visible per-app entry by the call alone (whether Settings lists it as a row was not looked at). Behaviour when the global or the per-app switch is off was not observed (owner step below). Declaring `appointments` in the manifest is what the documentation recommends so that the app is managed under the privacy page from installation.

**C2 Contents.** The store held one calendar, with the provider shown as "Microsoft account", bound to a user-data account, readable by other apps (`OtherAppReadAccess` Full), not hidden, with **0 appointments in the next 14 days**. That is the VM's Windows sign-in account; whether the zero means an empty mailbox or a sync that has not run on this VM is not known. No Google, other Microsoft or local calendar was present to count. The machine's state for "Mail & Calendar retired, new Outlook installed" was not established: the consent store shows the old Mail and Calendar app was used here at some point.

**C3 Freshness.** Not measured (needs an account whose web calendar can be edited and a store that is populated).

**C4 Fit.** Tested with data written by the probe into its own calendar and read back through the all-calendars read-only store (which is how a real fallback source would read; this is not a provider's data):

| Needed by the engine | Result |
| --- | --- |
| Start, end (duration) | intact; times come back as local-offset values (`DateTimeOffset`) and the duration as a `TimeSpan` |
| All-day | flag intact; an all-day event starts at local midnight with a 24 h duration |
| Recurrence expansion | a 5-occurrence daily series came back as 5 occurrences (one query over 14 days), each with its series info and its original start; an all-day and a single event came back too, 7 in total as written |
| Organizer | present, **but only when the property is requested explicitly** |
| Location | intact |
| Online-meeting link | present, **but only when requested explicitly** (`OnlineMeetingLink`) |

The default property set (`AppointmentProperties.DefaultProperties`: subject, location, start, duration, busy status, all-day, parent folder, recurrence, remote id, original start, change number) does not include the organizer, the online-meeting link, the invitees or the canceled flag; a source must add them to `FetchProperties`. Whether Microsoft or Google events written by their sync carry an online-meeting link in that property (and not only in the body or the location) is unknown and is the main thing a real account must show.

**C5 Packaging.** `appointments` is a general-use capability in Microsoft's list: it needs no Store approval and no justification text of the restricted kind, though `docs/store/capabilities.md` gains one row saying why. `appointmentsSystem` is the restricted capability and the documentation advises against declaring it for Store apps; the probe did not need it. In this repository, adding the capability to the development manifest alone fails four architecture tests in CI (`FlavourContractTests`: a release manifest may differ from the development one only in identity, alias and COM class, and may carry no extra capability), so a real change would edit `Package.appxmanifest`, `Package.Store.appxmanifest`, `Package.Direct.appxmanifest`, that test's expected set and `docs/store/capabilities.md` together. Windows also gives the user a per-app switch for it, which the app must treat as a state ("Calendars on this PC are switched off for TimeTug") rather than an error.

## Recommendation

**Needs more data**, with a leaning toward a later phase. The mechanism is sound: it opened without a prompt, the fields the engine needs come through once the properties are requested, recurrence is expanded by the store, and the capability is cheap to ship. What is unknown is whether the store is populated on an ordinary 2026 PC: the one real calendar here is empty, and nothing shows yet that a Microsoft, Google or other account added under Email & accounts keeps its calendar in the store without an app open. If the owner runs below and sees (a) events from a Microsoft account and from a Google account appear, and (b) a change made on the account's web calendar arrive within a few minutes, the recommendation becomes: **worth a later phase as a "calendars on this PC" fallback source** for people without a Microsoft account (and a way to show Google calendars without a TimeTug sign-in). It would be a host-side source like EventKit on the Mac, behind the existing `systemCalendar` capability, with the Windows app mapping `Appointment` to the engine's event shape and the engine's existing duplicate rules handling the overlap with a TimeTug-signed-in account. If the store stays empty for real accounts, the recommendation is **not worth building**.

Open risks if it is built: the store content depends on Windows' own sync, which the app cannot repair or schedule; the per-app and global privacy switches can silently empty it; online-meeting links may be missing for some providers; events from the same account the person also signs in to inside TimeTug would duplicate unless the engine's identity-family rule is extended to the store's account; Microsoft has been moving its own apps off this store, so its long-term stability needs a check.

## Still to run (owner, on the ARM64 VM, at the console)

The installed development package contains the probe (see "Still to run" in `2026-11-wam-broker.md` for how it is run and what it writes). Each run writes `C:\Users\Public\timetug-vm\probe\<Name>\result.json`: counts and flags only. Add only accounts you are happy to leave on the VM.

1. **A populated store (C2).** Add, under Settings > Accounts > Email & accounts, a Google account and a second Microsoft account (an account whose calendar has events in the next 14 days). Wait ten minutes, then run
   `& 'C:\Users\Public\timetug-vm\probe.ps1' -Name c3 -Spec 'store'`
   and note, per calendar line, the provider text, the number of appointments and how many have an organizer, a location and an online link.
2. **Freshness (C3).** In the account's web calendar create an event starting within the next hour. Run `... -Name c4a -Spec 'store'` straight after, again after 3 minutes (`c4b`) and after 10 minutes (`c4c`), and say when the count first rises.
3. **Consent behaviour (C1).** In Settings > Privacy & security > Calendar: say whether "TimeTug (development)" is listed; switch "Let apps access your calendar" off, run `... -Name c5a -Spec 'store'` and send the first lines; switch it on and, if an app list appears, switch only TimeTug off and run `c5b`; then put both back on.
4. **An everyday PC (C2).** If you have one with new Outlook as the mail app, tell the agent: it will prepare a package and the command for it (the CI package of this spike does not declare the capability).
5. **Cleanup.** Remove the extra accounts if they were only for this.

The probe's own calendar ("TimeTug (development)") is created and deleted by the fidelity test and is not left behind.
