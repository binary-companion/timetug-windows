# Screenshots: the shot list

The Store wants 1 to 10 screenshots (at least one at 1366 x 768 or larger, PNG, no more than 50 MB each) and a short caption for each. Use **only scripted data**: every picture below comes from a self-test scenario over invented meetings, never from a real calendar, so no person's meeting title, attendee or link can appear.

The scenarios write numbered PNGs (`build/selftest/<scenario>/NN-label.png`; CI uploads them as the `selftest-x64` and `selftest-arm64` artifacts, and `scripts/vm/vm-selftest.sh <scenario>` copies them from the VM). Take them at the VM's or a PC's native scale and crop only to remove empty desktop.

| # | Caption (draft) | Source | Notes |
| --- | --- | --- | --- |
| 1 | A tug when time needs your attention: the takeover with Join, Snooze and Dismiss | `tug-basic` (`01-primary.png`) | the hero picture |
| 2 | Today's meetings from the tray icon, with merged duplicates | `flyout-day` (`01-day.png`), `flyout-merge` (`01-merged-and-pair.png`) | pick the one with the merged card |
| 3 | Connect Google, Microsoft, iCloud or any CalDAV calendar | `welcome` (`01-connect.png`) | |
| 4 | Choose which calendars can tug you | `settings-calendars` (`01-calendars-default.png`) | |
| 5 | Light, Dark or follow Windows | `settings-appearance` (`01-appearance-light.png`, `02-appearance-dark.png`) | one image per mode, or a pair |
| 6 | An optional countdown chip above the notification area | `chip-modes` (`05-running.png`) | |
| 7 | Tug rules: lead time and requirements | `settings-tugrules` | |

Re-take all of them after any visible change, and check each picture by eye before it is uploaded. A picture of the takeover on the VM shows the VM's desktop: take it on a PC with a neutral desktop, or crop to the window.

Also needed for the listing (not screenshots): the store logo set (the `Assets` folder already holds the 150, 44 and 50 pixel logos the manifest names; the Store also wants a 300 x 300 and a 1:1 / 2:3 poster set, to be made from `artwork/` by the owner, whose licence is in `artwork/LICENSE.md`).
