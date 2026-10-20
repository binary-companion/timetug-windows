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

The VM is named `Windows 11` (Windows 11 build 26200, ARM64). `prlctl exec` runs commands as `NT AUTHORITY\SYSTEM`, with no interactive desktop. The VM already has .NET SDK 10.0.401 (and 7.0.400), Git and PowerShell 5.1; it has no winget and no `pwsh`. If the SDK is missing or too old: `powershell -c "iwr https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\di.ps1; & $env:TEMP\di.ps1 -Channel 10.0"` (or the dotnet-install script with `-Version`).

Verified recipe (2026-10-16), run from the Mac:

```
prlctl resume "Windows 11"                       # the VM is normally suspended
prlctl exec "Windows 11" cmd /c "cd /d C:\ && mkdir C:\src && cd C:\src && git clone https://github.com/binary-companion/timetug-windows.git"   # first time only; public repository, no credentials
prlctl exec "Windows 11" cmd /c "cd /d C:\src\timetug-windows && git pull -q"
prlctl exec "Windows 11" powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\src\timetug-windows\scripts\verify.ps1' -Platform ARM64
prlctl exec "Windows 11" powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\src\timetug-windows\scripts\verify.ps1' -Platform x64
prlctl suspend "Windows 11"                      # when finished, if it was suspended before
```

Notes learned:

- Quote Windows paths in single quotes when passing them as arguments (the Mac shell eats unquoted backslashes). Inside a `cmd /c "..."` string they are fine.
- A cold restore plus build takes about 1.5 minutes; the first run downloads the Windows App SDK packages (several hundred MB).
- The x64 app builds on the ARM64 VM (cross-build). x64 binaries are not launched on the VM by these commands; the tests are AnyCPU and run natively as ARM64. Running the packaged x64 app needs x64 emulation, which Windows 11 ARM provides.
- Uncommitted work reaches the VM only through git: push a branch and `git fetch`/`git checkout` it in `C:\src\timetug-windows`. No secrets or OAuth ids are copied this way (`local.settings.json` is git-ignored and is copied file to file separately when needed).
- `prlctl exec` returns the command's output; there is no desktop. To install, launch and screenshot the packaged app use `prlctl exec "Windows 11" --current-user ...` (runs in the logged-in user's session). The full recipe is in `docs/spikes/2026-10-spike-winui-ci.md`; the scripts are `scripts/ci/build-msix.ps1` (`-Stage Build` as SYSTEM, `-Stage Install` as the user) and `scripts/ci/run-selftest.ps1`.

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

The accessibility scan (`tests/TimeTug.App.A11y`, axe-windows `Axe.Windows.Automation`) runs while the `a11y-hold` scenario holds windows open: round 0 is a canary (a window with a button that has no name, which must be found, or the scan is not seeing the app), round 1 the welcome window's Connect step and the tug as it first appears, round 2 the Connected step and the tug with the link to copy. It fails on any error-level rule violation. The report is `build\a11y\a11y-report.txt` (and `.json`, plus the `.a11ytest` files Accessibility Insights for Windows opens). Accepted exceptions go in `tests/TimeTug.App.A11y/suppressions.json` with a reason; there are none.

On the VM (resume it first), after `build-msix.ps1 -Stage Build` as SYSTEM and `-Stage Install` as the user (see above): `prlctl exec "Windows 11" --current-user powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\src\timetug-windows\scripts\ci\run-selftest.ps1'`. Build the scanner first with `dotnet build tests\TimeTug.App.A11y -c CI` (as SYSTEM) before `run-a11y.ps1`. Redirect the output to a file inside the VM when the run is long, then read the file: `prlctl exec` sometimes drops its connection while a long command runs.

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
