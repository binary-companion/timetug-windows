# timetug-windows agent guide

## What this is

TimeTug for Windows: a packaged WinUI 3 app (.NET 10, Windows App SDK, MSIX) that runs the shared engine. The engine decides "what and when"; this app decides how it looks and where it lives. Windows 11 22H2 (build 22621) or later, x64 and ARM64.

## Repositories

- `calendar-connectors`: the connector library.
- `timetug-shared`: the portable engine. It publishes the `TimeTug.Engine` NuGet package (native win-x64 and win-arm64 libraries, a managed wrapper over the C interface, the golden fixtures). Dependency direction: `calendar-connectors` <- `timetug-shared` <- the apps.
- `timetug`: the Mac app.
- `timetug-windows` (this repository): the Windows app. It consumes `TimeTug.Engine` from nuget.org at an exact version pinned in `Directory.Packages.props`.

## Layout

```
src/TimeTug.Presentation/   plain net10.0: view models, the tug state machine, every interface to OS state
src/TimeTug.Platform/       net10.0-windows: engine host services and the OS-state implementations
src/TimeTug.App/            WinUI 3 packaged app: views, tray, window management, selftest
tests/                      TimeTug.Presentation.Tests, TimeTug.Platform.Tests, TimeTug.Architecture.Tests
docs/parity.md             every Mac capability and its Windows status (the release gate for phase 9)
docs/development.md        how to build and test, including on the Windows 11 ARM VM
docs/manual-tests/         the owner's manual checklist
scripts/                    verify.ps1 and friends; logic lives in scripts, not workflow YAML
artwork/                    brand artwork, all rights reserved (artwork/LICENSE.md)
```

## Rules

- No scheduling, dedup, rule or default logic in C#. View models talk only to `IEngineClient` (this repository's abstraction over the engine), never to P/Invoke. An architecture test enforces both.
- Platform services are small classes behind interfaces, each with tests. Every OS read (session lock and unlock, notification state, monitor list, transparency and animation settings, toast service, `TimeProvider`) sits behind an injectable interface.
- The app never runs elevated. No focus-stealing tricks (no simulated key presses, no thread-input attachment).
- All HTTP goes through `HttpClient`. Secrets only in Credential Locker. Never log event titles or calendar content.
- `SELFTEST` (the self-test and the `--selftest` switch) is defined only in the `Debug` and `CI` configurations, never in `Release` or Store builds.
- Display strings: the app owns its localization resources (`Strings/en-US/Resources.resw`); the engine supplies none.
- Pin third-party GitHub Actions by commit SHA (look it up with `gh api`, never guess). Pull-request jobs get none of the owner's signing material; the development MSIX uses a throwaway self-signed certificate created in the job.
- OAuth client ids come from a git-ignored local file (`local.settings.json`, VM builds) and are never printed or committed; CI embeds none.
- Every package version in `Directory.Packages.props` is exact. Nothing floats.
- Windows projects target `net10.0-windows10.0.22621.0`. Libraries and tests are AnyCPU; only `TimeTug.App` picks an architecture (`-p:Platform=x64` or `-p:Platform=ARM64`).

## Backward compatibility

Owner rule (2026-10-16): within a major version, a release of `TimeTug.Engine` must drop into this app with no change to the app. A non-major `TimeTug.Engine` bump is therefore merged when CI is green and requires no app change; Dependabot or the bump workflow opens it. The app is released only by the owner. A need that the engine cannot meet goes back to `timetug-shared` as an additive change.

## Verify

`scripts/verify.ps1` (build and test for the current architecture; see `docs/development.md` for the Windows 11 ARM VM recipe). The WinUI project builds only on Windows.

## Process

- Commit trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`; pull request bodies end with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Squash-merge when all checks pass.
- The owner applies rulesets, environments and required checks (owner-only); the agent never changes organization settings.
- Reviews use the `deepseek-review` skill; never send secrets; verify findings against the repository.
- Never print or commit any secret or OAuth id.
