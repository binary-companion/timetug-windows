# Jump list: what was observed (2026-10-30)

Task 5 of the countdown chip and jump list plan. It builds on `docs/spikes/2026-10-chip-window-and-jumplist.md` (the API works with no window and no pin; Windows keeps the list on disk; arguments arrive on the command line). Windows 11 build 26200, ARM64, Parallels, the development package. Everything below was driven by the `jump-list` self-test scenario over invented data, plus one run of the real app.

## Observed

| Question | Answer | How |
| --- | --- | --- |
| Does the service save the base list before the engine's first state? | yes | the scenario cleared the list, started the shell, and read three tasks (`--jump flyout`, `--jump test-tug`, `--jump settings`) back from `JumpList.LoadCurrentAsync` before pushing a state |
| What does Windows hold for a plan with "Join next meeting"? | four items in order, all `Kind = Arguments`, empty `GroupName`, `RemovedByUser = false`, `SystemGroupKind = None` | read back from Windows, not from our own service |
| Does the file Windows keeps hold anything from the calendar? | no | `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\*.customDestinations-ms`: the one file holding our item names (2992 bytes with four items, 2233 with three) was searched as UTF-16 and UTF-8 for an invented title, link and calendar name: none |
| Is the list updated only when the plan changes? | yes | unit tests (`JumpListServiceTests`) and the log of the scenario: one save for the base list, one when the link appeared, one when it went, one when it came back, one on quit |
| Does an item chosen while TimeTug runs reach the first copy? | yes, for all four actions | `IApplicationActivationManager.ActivateApplication(aumid, "--jump <word>")` (the call the shell makes) started a second process that handed itself to the first copy through `AppInstance` redirection, and exited with code 0 within a second; `WindowsInstanceRegistry` read the action from the redirected launch arguments. Each action had its one effect: Settings window opened, the engine was sent the test tug once, the meeting's link was opened once, the flyout opened once |
| Does the command-line path work too? | yes | the same arguments through the execution alias (`timetug-dev.exe --jump test-tug`): exit code 0 after 0.5 s, one test tug. An unknown word (`--jump nonsense`) is an ordinary second launch and opens the flyout |
| A stale "Join next meeting" (the link is gone) | opens the flyout, opens no link | the scenario removed the link from the scripted day and chose the item |
| An item chosen while the tug shows | ignored, logged ("a second launch was ignored because the tug is showing"), no engine command, nothing opened | |
| Is the base list on disk after a quit? | yes | the scenario quit the shell with "Join next meeting" in the list and read three tasks back |
| An item chosen while TimeTug is not running | the app starts, holds the action until the engine's first state, then does it | the real app on the VM was started through the alias with `--jump settings` after being stopped: log lines "a launch arrived before startup finished; it is held until then", then the list saved, then "a second launch asked this copy for the jump list action settings"; the Settings window existed afterwards. Only generic words are logged |

## Not observed

- **Where Windows draws the list** (the taskbar button while a window is open, a pinned icon, the Start entry) was not seen. The VM's taskbar is invisible to UI Automation, `prlctl capture` is black while the Mac's screen is locked, and an in-session screen grab through the job runner did not run reliably after Explorer was restarted. Per Microsoft's documentation the taskbar button (running or pinned) shows it; whether Windows 11's Start does is unverified and the app does not promise it. Manual checklist 5.28 asks the owner to record what the taskbar button, the pinned icon and Start show.
- **Whether a person can remove a task** was not observed. Microsoft's documentation says tasks are fixed by the app and only items in a custom category can be removed. The service replaces the whole list on every save, so a removed task would come back at the next change if it is part of the plan.
- **Choosing an item with a real click** (the shell's own path from the menu to `ActivateApplication`) was not driven; the scenario makes the same call the shell makes.

## What it changed in the design

- The service lives in Presentation (`JumpListService`, `IJumpListPlatform`) with only `WindowsJumpListPlatform` in Platform, so the rules (save at startup, only on a change, base list on quit, failures logged and ignored) are unit-tested on any machine.
- "Rapid duplicates coalesce" applies to identical requests: a different action within 500 ms is delivered, so a person who chooses two items quickly gets both.
- `Join next meeting` follows the flyout's Join rule (the meeting in progress, else the one the engine names as next, with a link; all-day and past events never count). It prefers the meeting in progress.
