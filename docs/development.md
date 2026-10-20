# Development

## Requirements

- .NET 10 SDK (`global.json` pins 10.0.401 and rolls forward to later 10.0.4xx patches). Windows 11 22H2 or later to build the app.
- The WinUI project (`TimeTug.App`) builds only on Windows. On macOS or Linux only `TimeTug.Presentation`, `TimeTug.Architecture.Tests` and `TimeTug.Presentation.Tests` build and run. Restoring everything off Windows needs `-p:EnableWindowsTargeting=true`.

## Build and test on a Windows machine

```
scripts\verify.ps1 [-Platform x64|ARM64] [-Configuration Debug|CI|Release]
```

It runs, from the repository root:

```
dotnet build TimeTug.sln -c Debug -p:Platform=ARM64 --nologo
dotnet test  TimeTug.sln -c Debug -p:Platform=ARM64 --no-build --nologo
```

- Always pass `-p:Platform=x64` or `-p:Platform=ARM64`; the solution maps every other project to Any CPU. Libraries and tests are AnyCPU, so the same test binaries run natively on both architectures. Only the app differs (`src\TimeTug.App\bin\<platform>\<configuration>\...`).
- `SELFTEST` is defined in `Debug` and `CI`, not `Release`.

## The Windows 11 ARM VM (Parallels)

The VM is named `Windows 11` (Windows 11 build 26200, ARM64) and is normally suspended. It has .NET SDK 10.0.401, Git and Windows PowerShell 5.1, no winget and no `pwsh`, and the repository cloned at `C:\src\timetug-windows`. Everything below is driven from the Mac with the scripts in `scripts/vm` (they need Parallels Desktop's `prlctl`; set `VM_NAME` for a VM with another name). Each prints what it does, fails with a message that says why, and returns the command's exit code.

| Script | What it does |
|---|---|
| `scripts/vm/vm-up.sh [--user]` | Resumes (or starts) the VM and waits until Parallels Tools answers; `--user` also waits for the signed-in user's desktop session. Safe to run again. |
| `scripts/vm/vm-run.sh [--user] [--ps] <command...>` | Runs a command in the VM, starting it first if needed. Default: as `NT AUTHORITY\SYSTEM` (session 0, no desktop): builds, git, installing certificates. `--user`: as the logged-in user on the real desktop (`prlctl exec --current-user`): installing the package, launching the app. `--ps`: the arguments are PowerShell code (sent with `-EncodedCommand`, so quoting is no problem). |
| `scripts/vm/vm-sync.sh [--patch] [<ref>]` | Puts a repository state into `C:\src\timetug-windows`: the current branch (pushed) by default, or a branch, tag or commit. `--patch` also sends uncommitted and untracked local work as a patch on top of the pushed `HEAD` (no secrets: ignored files are left out, a private key is refused). The VM checkout is reset to the target each time. |
| `scripts/vm/vm-selftest.sh [scenario\|all]` | Builds the development package in the VM (or `--artifact <dir>` installs a CI artifact), runs the self-test, copies `build\selftest` (result.json and screenshots) to `build/vm-selftest/<time>` on the Mac and prints the pass/fail summary. `--hold <s>` keeps the first screen up for `vm-screenshot.sh`. |
| `scripts/vm/vm-screenshot.sh <file.png>` | Captures the VM's whole screen (`prlctl capture`). |
| `scripts/vm/vm-suspend.sh` | Suspends the VM again (nothing happens if it is already suspended). Do this when you are done. |
| `scripts/vm/vm-install.ps1` | Runs in Windows, not on the Mac: installs a development MSIX and trusts its throwaway certificate. It is also what the CI artifact ships; see "Installing a CI build". |

Typical session:

```
scripts/vm/vm-sync.sh                    # the VM checks out your pushed branch (or: vm-sync.sh --patch for uncommitted work)
scripts/vm/vm-selftest.sh                # build, install, run all 17 scenarios, results in build/vm-selftest/<time>/
scripts/vm/vm-selftest.sh tug-basic --no-build --hold 40 &   # reuse the installed package, keep the tug up 40 s ...
sleep 15; scripts/vm/vm-screenshot.sh build/tug.png           # ... and look at it
scripts/vm/vm-run.sh --user --ps 'Get-Content "$env:LOCALAPPDATA\Packages\BinaryCompanion.TimeTug.Dev_68z4tsj3tx7ew\LocalState\logs\timetug.log" -Tail 30'
scripts/vm/vm-suspend.sh
```

What the scripts do under the hood (the verified recipe; useful when something needs doing by hand):

- `prlctl exec "Windows 11" <program> ...` runs as SYSTEM, `prlctl exec "Windows 11" --current-user ...` as the logged-in user in the interactive session (the user must be signed in at the VM console). `Add-AppxPackage` run as SYSTEM would register the package for SYSTEM only, so the build and the certificate trust run as SYSTEM and the install and the self-test as the user.
- Quote Windows paths in single quotes on the Mac side (the Mac shell eats backslashes); inside `cmd /c "..."` they are fine. PowerShell code goes through `-EncodedCommand` (`vm-run.sh --ps`).
- Long commands: `prlctl exec` sometimes drops its connection, and now and then fails to start a command ("PrlJob_GetResult: Invalid argument", a second try works). `vm-selftest.sh` therefore starts each long step detached in the VM (`C:\Users\Public\timetug-vm\<name>.ps1`), follows its log file and reads an exit-code file when it finishes. Do the same by hand: redirect to a file in the VM and read it.
- Files travel as a tar stream through `prlctl exec` (stdin and stdout are binary-safe): `COPYFILE_DISABLE=1 tar -cf - -C <dir> . | prlctl exec "Windows 11" tar -xf - -C 'C:\path'`, and the reverse. No shared folder is needed. Patches travel on stdin the same way.
- Uncommitted work reaches the VM only through `vm-sync.sh` (git, or a patch). No secrets or OAuth ids are copied this way: `dev-oauth.json` is copied file to file by hand and never printed.
- A cold restore plus build takes about 1.5 minutes the first time (the Windows App SDK packages are several hundred MB); after that `vm-selftest.sh all` takes about 2m40s end to end (build 50 s, self-test 95 s, the rest is polling and copying results).
- The x64 app builds on the ARM64 VM (cross-build), but the x64 package cannot be run there: the VM has only the ARM64 .NET 10 desktop runtime.
- First-time setup of a VM (once): `prlctl exec "Windows 11" cmd /c "mkdir C:\src && cd /d C:\src && git clone https://github.com/binary-companion/timetug-windows.git"`; the repository is public, no credentials. If the SDK is missing or too old: `powershell -c "iwr https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\di.ps1; & $env:TEMP\di.ps1 -Channel 10.0"`.

### Installing a CI build (the development MSIX artifact)

CI builds the development package for both architectures on every pull request and every merge, signs it with a throwaway certificate created in the job (the key is deleted there and never leaves the job) and uploads, for 14 days, one artifact per architecture: `msix-x64` and `msix-arm64`. Each holds the `.msix`, `Dependencies\<arch>\`, `dev.cer` (the public certificate), `vm-install.ps1`, `commit.txt` and a `README.txt`. A repository check (`scripts/ci/check-repository-contracts.py`) fails the build if a key file is committed, uploaded or exported.

```
gh run list --repo binary-companion/timetug-windows --branch main --limit 3
gh run download <run-id> --repo binary-companion/timetug-windows --name msix-arm64 --dir build/msix-arm64
scripts/vm/vm-selftest.sh --artifact build/msix-arm64      # install it in the VM and run every scenario (use the commit it was built from: commit.txt)
```

On a real PC, from an elevated PowerShell in the unpacked folder: `powershell -ExecutionPolicy Bypass -File .\vm-install.ps1` (trusts the certificate, installs for you; `-Stage Trust` and `-Stage Install` do the halves separately, `-Stage Uninstall` removes the package and the trusted certificate). The manual checklist (`docs/manual-tests/windows-checklist.md`) starts from this.

### Tray digit pictures

The tray icon with its minute digit is drawn in managed code (`TimeTug.Platform/Tray`) from the committed icon files and the bundled digit strokes (`src/TimeTug.App/Assets/Tray/Glyphs`). `tests/TimeTug.Platform.Tests/data/tray-digit-*.png` are golden pictures compared within 2 of 255 per channel; they are never rewritten by a normal test run. After a deliberate change run `scripts/ci/tray-goldens.ps1 -Update` (or on a Mac `TIMETUG_UPDATE_TRAY_GOLDENS=1 dotnet test tests/TimeTug.Platform.Tests -p:EnableWindowsTargeting=true -p:EnableMsixTooling=false -p:GenerateProjectPriFile=false -p:AppxGeneratePriEnabled=false --filter Update_the_goldens_when_asked`), look at the pictures and `docs/screenshots/tray-digits-contact-sheet.png`, and commit them.

### Tests for the helper scripts

`scripts/vm/tests/test-vm-scripts.sh` checks the shell scripts against a stub `prlctl` (argument parsing, VM state handling, what is sent to the VM, patch contents); `scripts/vm/tests/vm-install.tests.ps1` checks how `vm-install.ps1` finds the package, dependencies and certificate (plain PowerShell: Windows PowerShell 5.1 and `pwsh`). Both, and `shellcheck`, run in the `architecture` CI job through `scripts/ci/architecture.sh`.

## Pinned versions

Every version is exact in `Directory.Packages.props`. To change one, edit it there and record why in the pull request. To look versions up: `https://api.nuget.org/v3-flatcontainer/<lowercase id>/index.json`.

## Real-API smoke tests for the OS-state services

`tests/TimeTug.Platform.Tests/OsState/RealApiSmokeTests.cs` checks the real Windows calls behind `SessionState`, `NotificationState`, `MonitorService`, `UiSettings` and the hidden message window. The checks that need a signed-in desktop run only when `TIMETUG_OS_SMOKE=1`. On the VM, run them as the logged-in user:

```
prlctl exec "Windows 11" --current-user powershell -NoProfile -ExecutionPolicy Bypass -Command '$env:TIMETUG_OS_SMOKE="1"; cd C:\src\timetug-windows; dotnet test tests\TimeTug.Platform.Tests -c Debug --no-build --nologo --filter "FullyQualifiedName~RealApiSmoke" --logger "console%3Bverbosity=detailed"'
```

(`%3B` is an encoded `;`; the logger option cannot carry a literal one through `prlctl`.) `TIMETUG_OS_TIMECHANGE=1` additionally enables a test that waits up to 90 seconds for a real clock change and a real time-zone change (`Set-Date`, `Set-TimeZone` from an elevated `prlctl exec` while it runs) and checks that the bridge reports `clockChanged` and `timeZoneChanged`. Restore the VM's time zone afterwards (`Set-TimeZone -Id "Central America Standard Time"`).

Toasts need package identity, so they are checked by the packaged app: the `toast` self-test scenario registers the toast service, shows a toast and requires Windows to accept it (the buttons themselves are a manual check).

## The self-test and the accessibility scan

A development or CI build (the `SELFTEST` define) accepts `timetug-dev.exe --selftest <scenario> --out <folder> [--hold <seconds>]`. Each scenario is one class in `src/TimeTug.App/SelfTest/Scenarios` (a `Name` constant and a short script over the fakes in `SelfTest/FakeOsState.cs`, `TugRig` and `EngineRig`). It writes `result.json` into its folder: named checks (`{name, ok}`), notes, and numbered screenshots (`01-primary.png`, `02-mirror.png`, ...). `scripts/ci/selftest-scenarios.txt` lists every scenario with the fewest screenshots it must produce and its timeout; `check-repository-contracts.py` fails when that list and the scenario classes differ.

```
scripts\ci\build-msix.ps1 -Platform ARM64            # build, trust and install the development package
scripts\ci\run-selftest.ps1                          # every scenario; or -Scenario tug-basic [-HoldSeconds 30]
scripts\ci\run-a11y.ps1                              # the axe-windows scan (needs the solution built in the same configuration)
```

`run-selftest.ps1` runs every scenario even after one fails and then fails when a scenario left no `result.json`, when `ok` is not true, when any check is false, or when a screenshot is missing, too few, or blank. Output goes to `build\selftest\<scenario>\`; CI uploads it as the `selftest-x64` and `selftest-arm64` artifacts. Two kinds of scenario:

- Fake-based tug scenarios (`tug-basic`, `tug-join`, `tug-snooze`, `tug-dismiss`, `tug-no-focus`, `tug-toast`, `tug-join-fails`, `tug-monitors`, `tug-contrast-and-motion`) use `TugRig`: a scripted engine client and fake session, notification state, monitors, toasts, link launcher and appearance settings behind the real `TugController` and real tug windows. Each ends by checking the exact list of commands the engine received.
- Scenarios that need time to pass (`tug-late-return`, `tug-meeting-gone`) use `EngineRig`: the real composition root and the real native engine on its manual clock (`testing: true`, a fixed start), one Google account whose calendar answers come from the recorded responses in `tests/data` (embedded in development and CI builds; copied from `timetug-shared`'s conformance data, so they are the shapes the real API sends), and a `ManualClock` that is advanced together with the engine's (`test.advanceClock`). The recorded credential store behaves like the engine's own: a refresh-token update keeps the revision, and revisions are hyphenated UUIDs.

`tug-no-focus` clicks the real mouse (`MouseInput` in `TimeTug.Platform`, built only with `SELFTEST`) at a spot with no control on it after another window of the app took the keyboard from the tug.

The accessibility scan (`tests/TimeTug.App.A11y`, axe-windows `Axe.Windows.Automation`) runs while the `a11y-hold` scenario holds windows open: round 0 is a canary (a window with a button that has no name, which must be found, or the scan is not seeing the app), round 1 the welcome window's Connect step and the tug as it first appears, round 2 the Connected step and the tug with the link to copy, round 3 the flyout (a problem row and a long day). It fails on any error-level rule violation. The report is `build\a11y\a11y-report.txt` (and `.json`, plus the `.a11ytest` files Accessibility Insights for Windows opens). Accepted exceptions go in `tests/TimeTug.App.A11y/suppressions.json` with a reason; there are none.

On the VM use `scripts/vm/vm-selftest.sh` (above); it builds, installs and runs `run-selftest.ps1` for you. The accessibility scan needs the scanner built first (`dotnet build tests\TimeTug.App.A11y -c CI` as SYSTEM, for example `vm-run.sh cmd /c "cd /d C:\src\timetug-windows && dotnet build tests\TimeTug.App.A11y -c CI --nologo"`), then `vm-run.sh --user powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\src\timetug-windows\scripts\ci\run-a11y.ps1'`. Redirect long output to a file inside the VM and read it afterwards: `prlctl exec` sometimes drops its connection while a long command runs.

## The flyout scenarios and the real tray icon

`flyout-empty`, `flyout-day`, `flyout-problems` and `flyout-tug-wins` open the real flyout window through `FlyoutHost` over a `TugRig` (scripted engine, real `TugController`), on a standing clock (Friday 16 October 2026, 10:20 UTC) and the real monitors; the tray icon is stood in for by a rectangle near the bottom-right corner. `flyout-anchor` uses the real tray icon and `TrayAnchor`, and writes `anchor.txt`, a dump of the taskbar's UI Automation elements (`taskbar-uia.txt`; it contains the live tooltip, so it names the next meeting) and, with `--hold N`, `anchor-log.txt` (the anchor every two seconds); `docs/spikes/2026-10-flyout-anchor.md` records what it found. Real losses of the active state are ignored in these scenarios (`FlyoutWindow.IgnoreSystemDeactivation`): on the VM, Parallels' foreground helper takes the keyboard whenever a script runs a command in it, which would close the flyout under the scenario; the "lost the active state" path is pressed with `LoseActivation` instead.

To see the flyout over another window on the VM: `Start-Process notepad -WindowStyle Maximized` in the user session, `vm-selftest.sh flyout-anchor --no-build --hold 40` and, while it holds, a screen capture. `vm-screenshot.sh` (`prlctl capture`) comes back black while the Mac's own screen is locked; a capture taken inside the VM's user session works then: PowerShell with `SetProcessDPIAware`, `Graphics.CopyFromScreen` over `SystemInformation.VirtualScreen`, saved as a PNG and copied out with `prlctl exec ... cmd /c type file.png > file.png`. Close Notepad (or any app that modern packages share resources with) before `vm-selftest.sh` installs a new build, or the install fails with 0x80073D02. To see the pinned icon, set `IsPromoted` to 1 under `HKCU\Control Panel\NotifyIconSettings\<id>` and restart Explorer, and remove it afterwards; the VM hides its taskbar through byte 8 of `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3` (3 hides, 2 shows): put it back as it was.

## OAuth client ids for local builds

Google and Microsoft sign-in need the OAuth client registrations that the Mac app uses. They are never in the repository. A development or CI build (the `SELFTEST` configurations) reads them from `%LOCALAPPDATA%\TimeTug\dev-oauth.json` on the machine running it; when the file is absent those two kinds show as "isn’t set up in this build", exactly as in pull-request CI. A Release build reads no such file (phase 9 injects the ids). The file is git-ignored by name (`dev-oauth.json`), copied onto the machine by hand and never printed or logged: diagnostics name only the families found.

```json
{
  "google": { "clientId": "<desktop client id>", "clientSecret": "<desktop client secret>" },
  "microsoft": { "clientId": "<application (client) id>" }
}
```

Names are not case sensitive; a family can be left out. A Google desktop client has a secret (not confidential for an installed app); a Microsoft public client has none. A packaged app reads the real `%LOCALAPPDATA%` for this (only writes are redirected), so the file goes in `C:\Users\<you>\AppData\Local\TimeTug\`.

## Running the packaged app for real

`scripts\ci\build-msix.ps1` builds and installs the development package; start it like a person would, from the Start menu or with `Start-Process "shell:AppsFolder\BinaryCompanion.TimeTug.Dev_68z4tsj3tx7ew!App"` in the user session (`prlctl exec "Windows 11" --current-user ...`). The engine keeps its files in the package’s `LocalState` folder (`%LOCALAPPDATA%\Packages\BinaryCompanion.TimeTug.Dev_68z4tsj3tx7ew\LocalState`); `LocalState\logs\timetug.log` is the rolling diagnostics log. The `startup` self-test (`run-selftest.ps1 -Scenario startup`) runs the same composition root against the real native engine with a scripted host.

The package is framework-dependent: a machine needs the .NET 10 desktop runtime for the app’s architecture (the VM has ARM64 only, so the x64 package cannot be run there).

## The tray icon on the VM

The development package puts its icon in the notification area; Windows registers it under `HKCU\Control Panel\NotifyIconSettings` (the entry for `TimeTug.exe` appears when the icon is created; `IsPromoted` unset means it sits in the overflow flyout until the person drags it out). The VM hides its taskbar, so a screen capture does not show the corner. To see the real menu from a script, post the tray's callback message to its hidden window (class `H.NotifyIcon_*`, WM_USER with WM_RBUTTONUP) and capture the screen in the same session (`Graphics.CopyFromScreen`); a new `prlctl exec` session dismisses the menu. The `--current-user` session is DPI-virtualised (2240 x 1260 on the 3072 x 1728 screen) and its injected mouse and keyboard input does not reach a native popup menu, so the menu rows were not clicked there; `startup` and `tray` self-tests cover the commands. Regenerate the icons with `scripts/ci/make-icons.py` (needs `rsvg-convert` and `scripts/ci/requirements-icons.txt`).
