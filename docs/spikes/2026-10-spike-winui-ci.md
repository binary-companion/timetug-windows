# Spike: a packaged WinUI window renders and is captured (2026-10-16)

Question: can a packaged WinUI 3 app (Windows App SDK 2.5.1) be built into a development MSIX, installed, launched with a command-line switch while keeping package identity, render a window, and have it captured to a PNG on `windows-latest`, `windows-11-arm` and the Parallels Windows 11 ARM VM?

Answer: yes, on all three, with the same scripts. Run 38930759776 on pull request 2 is green on both runners.

## What was built

- `TimeTug.App`: a window showing "TimeTug"; `#if SELFTEST` entry `timetug-dev.exe --selftest hello --out <folder>` writes `selftest-result.json` and `hello.png`, then exits. The result records the process architecture, the package full name (so a run without identity fails), and every capture attempt.
- `TimeTug.Platform.Capture`: `ScreenCapture.WindowAsync(hwnd, path)` (PrintWindow with `PW_RENDERFULLCONTENT`, then `GetDIBits`), `PngWriter` (Windows imaging codec) and `PixelAnalysis` (distinct-colour count, xUnit tested). P/Invoke lives in Platform, as the architecture test requires. The XAML fallback (`RenderTargetBitmap` of the window content) lives in the app's self-test.
- `Package.appxmanifest`: `uap5:Extension windows.appExecutionAlias` with `<uap5:ExecutionAlias Alias="timetug-dev.exe" />`. The alias is what lets the switch reach a process that keeps package identity. The Store and release manifests must not carry it (they get their own manifest later).
- `scripts/ci/build-msix.ps1` and `scripts/ci/run-selftest.ps1`; CI steps in both Windows jobs; artifacts `selftest-x64` and `selftest-arm64`.

## Results

| Environment | Packaged, identity | Window renders | PrintWindow | RenderTargetBitmap | Notes |
| --- | --- | --- | --- | --- | --- |
| windows-latest (x64) | yes | yes | works (144 colours) | works (17 colours) | self-test about 8 s |
| windows-11-arm | yes | yes | works (141 colours) | works (17 colours) | install needed a retry, see below |
| Parallels Windows 11 ARM VM | yes | yes | works (187 colours) | works (17 colours) | needs the interactive user session |

`hello.png` takes PrintWindow first and falls back to RenderTargetBitmap. Both work everywhere, so the fallback has not been needed. Windows.Graphics.Capture was not tried: it needs a D3D device and frame pool and nothing required it. PrintWindow captures the frame and title bar too and has no dependence on a physical display; RenderTargetBitmap captures only the XAML content. A captured window on the VM and runners is mostly dark (dark theme) with white text; the blank check passes because the title bar and text add colours.

## What each environment needs

- Build: `dotnet publish src/TimeTug.App/TimeTug.App.csproj -c CI -p:Platform=<P> -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true -p:PackageCertificateThumbprint=<thumb> -p:AppxPackageDir=<dir>\ -p:AppxBundle=Never` works with the .NET SDK alone (the single-project MSIX tooling in `Microsoft.Windows.SDK.BuildTools.MSIX` is pulled in by the Windows App SDK package). No Visual Studio, no MSBuild.exe. A harmless warning says `mspdbcmf.exe` is missing (no symbols package).
- Certificate: `New-SelfSignedCertificate` in `CurrentUser\My`, subject equal to the manifest publisher (`CN=TimeTug Development`), code-signing EKU, valid two days. Only the public `.cer` is exported; the key is deleted from the store after the build. Trust: `Import-Certificate` into `LocalMachine\TrustedPeople` (needs elevation; runners and SYSTEM have it).
- Install: `Add-AppxPackage -Path <msix> -DependencyPath <layout>\Dependencies\<arch>\*.msix`. The framework package (Windows App Runtime 2) is not on the runners or the VM, so it must be passed as a dependency. Registration is per user.
- ARM64 runner: the first install failed with `0x80073D02`, "resources it modifies are currently in use" by `MicrosoftWindows.Client.WebExperience` (the Widgets pack, which uses the Windows App Runtime). Stopping Widgets and Web Experience processes and retrying once fixed it. The x64 runner did not need this.
- Launch: the alias `%LOCALAPPDATA%\Microsoft\WindowsApps\timetug-dev.exe` (on the user's PATH). Launching it starts the packaged app with the command line and identity. Both runners are interactive enough for WinUI: no extra setup, the window renders and PrintWindow captures it.
- Packaged build time: about 40 s on the VM, about 1 minute on the runners. Whole new CI steps: install 4 to 30 s, self-test 8 s; job totals 2m48s (x64) and 3m30s (arm64).

## The VM recipe

`prlctl exec` alone runs as SYSTEM in session 0 with no desktop, and `Add-AppxPackage` run as SYSTEM would register the package for SYSTEM only. `prlctl exec "Windows 11" --current-user ...` (Parallels Tools) runs the command as the logged-in user in their interactive session (session 1), so windows appear on the real desktop and the package registers for that user. The logged-in user must be signed in at the VM console.

```
prlctl resume "Windows 11"
prlctl exec "Windows 11" cmd /c "cd /d C:\src\timetug-windows && git pull -q"
# build, sign, trust (elevated, so as SYSTEM):
prlctl exec "Windows 11" powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\src\timetug-windows\scripts\ci\build-msix.ps1' -Platform ARM64 -Stage Build
# install for the interactive user:
prlctl exec "Windows 11" --current-user powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\src\timetug-windows\scripts\ci\build-msix.ps1' -Platform ARM64 -Stage Install
# launch, capture, check:
prlctl exec "Windows 11" --current-user powershell -NoProfile -ExecutionPolicy Bypass -File 'C:\src\timetug-windows\scripts\ci\run-selftest.ps1'
prlctl suspend "Windows 11"
```

The results are under `C:\src\timetug-windows\build\selftest\<scenario>` (`result.json`, `01-hello.png`; the spike wrote `hello.png` and `selftest-result.json`). To look at the picture from the Mac, base64 it through `prlctl exec` (`[Convert]::ToBase64String([IO.File]::ReadAllBytes(...))`) and decode it. `prlctl capture "Windows 11" --file x.png` (whole screen) was not needed.

On the VM the `-Stage Install` run was done as the user; `-Stage Build` signs with a certificate in SYSTEM's store, which is fine because only the public certificate is shared.

## What failed

- First manifest used `desktop:ExecutionAlias` inside `uap5:AppExecutionAlias`; MakeAppx rejected it. The element is `uap5:ExecutionAlias`.
- CA1806 (unused `ReleaseDC` result) failed the first build; the analyzer level treats warnings as errors.
- ARM64 runner install conflict, above.

## Concerns for later tasks

- The package is about 117 MB because the Windows App Runtime payload is copied in; the framework package is installed separately. Self-contained deployment was not evaluated.
- The self-test waits 1.5 s before capturing so the first frame is presented; real scenarios should wait on explicit app signals instead.
- The execution alias exists in every build of this manifest. Release and Store manifests must be separate or conditional before phase 9.
- RenderTargetBitmap does not capture popups, window chrome or other windows; PrintWindow captures the target window only. A full-screen tug on a second monitor will need a per-window capture, which both support.
