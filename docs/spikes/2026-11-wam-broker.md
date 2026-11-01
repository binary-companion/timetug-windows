# WAM broker: packaging, accounts, errors, cancellation and parenting (2026-11-01)

Task 1 of the phase 6 plan (`docs/superpowers/plans/2026-11-01-phase-6-wam-sign-in.md` in `binary-companion/timetug`). Measured with a throwaway probe (a self-test scenario over `Microsoft.Identity.Client` and the broker package, on a scratch branch that is not merged) in four places: the Windows 11 ARM64 VM (Parallels, build 26200, .NET 10.0.13) with the packaged development app, the `windows-latest` x64 runner and the `windows-11-arm` runner (one CI run of the same probe, pull request 36, closed unmerged), and local builds of the Store and Direct flavours on the VM. The Entra registration already carried the broker redirect URI (Task 0); the probe never set a redirect URI itself and it worked.

This document contains no email, account id, tenant id or token. Account and token values were reduced to eight-character hashes in the probe and are not repeated here.

## What was and was not observed

The VM's Windows sign-in is a personal Microsoft account (`Get-LocalUser` reports `PrincipalSource` `MicrosoftAccount`), and every silent request for it succeeded with no dialog, so TimeTug's registration had evidently been consented for it already (an earlier browser sign-in is the likely cause). One silent request for a broader scope that the product will never ask for (a calendar write scope, used only to learn whether the error would differ) also succeeded for this account; its token was discarded unused. This means the **never-consented** and **password-changed** states could not be produced by the agent. Nothing here involved typing a password or accepting a consent page. Interactive requests were opened to look at the window and were ended by cancelling the token, never completed.

| Observed by the agent | Needs the owner (section "Still to run") |
| --- | --- |
| S1 packaging on both architectures and all three flavours; the native library loads on x64 and ARM64 | S2 with a work account, with a Microsoft account added under Email & accounts, and with a fresh (never consented) account |
| S2 on a Windows-sign-in Microsoft account and on a local-only account (the hosted runners) | S3 after consent: password change, consent withdrawn, account removed, tenant that blocks consent, MFA |
| S3 for: no account, unknown login hint, an unconsented scope, an invalid scope, no network, a cancelled dialog, no window handle | S4 across a reboot of the PC |
| S4 across process restarts, S5, S8 | S6 with a person completing or closing the dialog |
| S6 with token cancellation; S7 without a person at the screen | S7 with a person at the screen |

## Answers

### S1 Packaging

- **Pins (exact):** `Microsoft.Identity.Client` 4.91.0 and `Microsoft.Identity.Client.Broker` 4.91.0. The broker package pulls `Microsoft.Identity.Client.NativeInterop` 0.20.6 (transitively; `CentralPackageTransitivePinningEnabled` is false in this repository, so list it in `Directory.Packages.props` only if the build should be insulated from a later broker release). The native runtime reports itself as 0.20.6. 4.92.0 (NativeInterop 0.20.7) exists on nuget.org and was not evaluated.
- **Where MSAL goes:** referencing both packages from `TimeTug.Platform` is enough. `TimeTug.App` packages the files; no change to the App project was needed.
- **Dev flavour (`build-msix.ps1`, framework-dependent, not RID-specific):** the package carries `Microsoft.Identity.Client.dll`, `Microsoft.Identity.Client.Broker.dll`, `Microsoft.Identity.Client.NativeInterop.dll` at the root and the native libraries for every runtime under `runtimes\<rid>\native\`: `win-x64\msalruntime.dll` (3,060,576 bytes), `win-arm64\msalruntime_arm64.dll` (3,264,352), `win-x86\msalruntime_x86.dll`, `linux-x64\libmsalruntime.so` (38 MB), `osx-x64\msalruntime.dylib` and `osx-arm64\msalruntime_arm64.dylib`. The Linux and macOS files are about 54 MB of dead weight in the development package. They are harmless and a trimming step is optional (release flavours do not have them, below).
- **Store and Direct flavours (self-contained, RID-specific):** only the library for the package's architecture, flat at the package root: `msalruntime.dll` in the x64 package, `msalruntime_arm64.dll` in the ARM64 package. Both flavours and both architectures were built on the VM with `build-release-msix.ps1` (store: `-DryRun`; Direct: `-LocalSign`), which runs `check-release-package.ps1`, and passed it with the MSAL packages referenced; the CI `release-dry-run` jobs (x64, ARM64, Store flavour) passed on the scratch branch too.
- **Does it load?** In the packaged development app, `NativeLibrary.TryLoad` of the matching library succeeds and the wrong-architecture one fails, on the ARM64 VM, the x64 runner and the ARM64 runner (the probe calls `GetAccountsAsync` and a silent request on all three: the broker answers). The Store and Direct packages were not installed and run (they carry no self-test); their contents match the layout of the development package for the same architecture, apart from the flattening.
- **The recipe for the package check (Task 15 in the plan):** `scripts/ci/check-release-package.ps1` must fail a Store or Direct package that lacks `msalruntime.dll` (x64) or `msalruntime_arm64.dll` (ARM64) at the package root, and a development package that lacks `runtimes\win-x64\native\msalruntime.dll` or `runtimes\win-arm64\native\msalruntime_arm64.dll`. Check the managed `Microsoft.Identity.Client.Broker.dll` and `Microsoft.Identity.Client.NativeInterop.dll` in every flavour as well. Do not require the absence of the other runtimes in the development package.
- **Not seen:** a Store submission containing the library (phase 9), and anything about an MSIX built with a different MSAL version.

### S2 Listing

| Case | `GetAccountsAsync` with `ListOperatingSystemAccounts` | Without the option |
| --- | --- | --- |
| Windows sign-in is a personal Microsoft account (VM) | one account; its user name carries an `@` and a consumer mail domain; the home account id has 73 characters; the first listing after the app had been idle took 1.6 s, later ones about 0.1 s | zero accounts |
| Local-only Windows account (x64 and ARM64 runners) | zero accounts | not run |
| Microsoft account added under Settings > Accounts > Email & accounts but not the Windows sign-in | not observed (owner) | not observed |
| Work or school account | not observed (owner) | not observed |

The MSAL log for the listing says the broker's work-and-school discovery returned 0 accounts, and that the personal-account discovery "returned failure ... this is an expected condition", after which the runtime still returned the one OS account. The reading that fits is that the Windows sign-in account is listed and other personal accounts are not enumerated; only the owner run can confirm it. Without the option the list is empty, so the option is required.

Because the VM account had consented before, "before consent" is not proven. The listing itself asks the identity service for no token, so the card can probably name the account ("Use name@example.com") with no prior consent; this is the first thing the owner run must settle.

**Verdict S2:** the card can name the Windows account when the listing returns exactly one account whose user name contains an `@`, and otherwise reads "Microsoft account on this PC" (the spec's fallback). Tasks 14 to 17 are unchanged by this, except that the naming rule is exactly that one.

### S3 Errors

Every row is an observed result except where marked. `Classification` is `MsalUiRequiredException.Classification`; the WAM codes appear only in the exception message, as `Error Code: <decimal>`.

| Request | Exception type | `ErrorCode` | Notes | Maps to |
| --- | --- | --- | --- | --- |
| Silent, OS account, none exists (the runners; also the personal OS account with an organizations-only authority) | `MsalUiRequiredException` | `failed_to_acquire_token_silently_from_broker` | `Classification` None; WAM `0xCAA10001` (3399548929, "interaction required") | `UiRequired` (`signedOut` or `accountMissing`) |
| Silent, unknown login hint | `MsalUiRequiredException` | `no_account_for_login_hint` | `Classification` `AcquireTokenSilentFailed`; raised by MSAL before the broker is called | `UiRequired` (`accountMissing`) |
| Silent, scope the account never granted (a Graph delegated scope TimeTug does not request) | `MsalUiRequiredException` | `failed_to_acquire_token_silently_from_broker` | WAM `0x8086000C` (2156265484); same type and code as the row above | `UiRequired` (`consent`, best effort) |
| Silent, malformed scope | `MsalServiceException` | `WAM_provider_error_2156265473` | WAM `0x80860001`, "ApiContractViolation" | `Failed` |
| Silent or forced refresh, no network | `MsalServiceException` | `2147943631` (the decimal of `0x800704CF`) | message says "NoNetwork" and "no Internet connection"; **even a request that a cached token could have served failed** | `Network` |
| Interactive, dialog closed by cancelling the token; also the dialog closing by itself | `MsalClientException` | `authentication_canceled` | message "User canceled authentication" | `Cancelled` |
| Interactive with no parent window handle | `MsalClientException` | `window_handle_required` | raised at once | `Failed` (a programming error) |
| Silent, after a password change | not observed (owner) | | | expected `UiRequired` (`signedOut`) |
| Interactive, tenant blocks user consent | not observed (owner) | | | expected `Blocked` (`adminConsentRequired`) |
| Silent or interactive, MFA required | not observed (owner) | | | expected `UiRequired` (`mfa`) |

Consequences for the mapper (Task 14): classify by exception type and `ErrorCode` first; treat any `MsalUiRequiredException` as `UiRequired`; use the WAM numeric code from the message only to choose the documented `reason` and only when it is one of a short, tested set (otherwise `other`); never copy message text into a log, only the `ErrorCode` and the numeric WAM code. A request offline did not fall back to the cache, so "Sign in again" must never be shown for `Network`.

An interactive request made while the PC was offline opened no Sign in window and stayed pending until the token was cancelled (15 s observed), so the sign-in request needs the cancellation path the plan already has, and the card should not promise a dialog.

### S4 Ids

`HomeAccountId.Identifier` was identical (compared as a hash) in four separate processes. In one run the probe stored it; in a later process `GetAccountAsync(storedId)` returned the account in both of its two attempts (one silent request without and one with a forced refresh followed). Only that single later process exercised the lookup by stored id. A reboot of the PC was not tried (owner). **Verdict S4:** the plan's primary path holds at process-restart level; keep the fallback (match `Username` to the stored email) because a reboot, a re-added account or a sign-in-method change may change the id. Tasks 14 to 17 are unchanged.

### S5 Refresh

`WithForceRefresh(true)` is honoured: three requests with it returned three different access tokens (each valid for 60 minutes), while repeated requests without it returned the same token from the broker's cache. Token lifetime here was 60 minutes, so the 60 s early-refresh margin in the library provider is comfortable.

### S6 Cancellation

Cancelling the token while the Sign in window was open made the call return in 3 ms to 215 ms (seven runs) with `MsalClientException` `authentication_canceled`, and the Sign in window was gone in the next window survey 1.5 s later. No late result can arrive in these runs because the dialog closes. The dialog also closed by itself, with the same exception, when the foreground was taken away in the first runs (the VM's automation helper does this whenever a command runs in the user session). One thing stays unexplained: a 22 by 56 pixel top-level window (`#32770`, in the TimeTug process; owned by the passed parent window when that was the visible or the hidden owner window, and with no owner when the desktop window was the parent) appeared with the dialog and stayed after the call returned, in each run where the dialog opened. It is probably the runtime's modal helper, it has no visible content, and it should be checked in slice A by counting the process's top-level windows after a cancelled sign-in (the probe's survey code in the scratch branch does this).

**Verdict S6:** the engine's cancel path (`account.cancel`, then the token) works; the plan's statement that a late result is dropped stays as a safety net, not as something normally reached. Tasks 14 to 17 are unchanged. A person finishing the dialog in the instant of a cancel is not tested (owner).

### S7 Parenting

- With a visible TimeTug window as the parent, the Sign in window (an `ApplicationFrameWindow`, 706 by 784) appeared and was the foreground window within 4 s, even though the TimeTug window was not the foreground window when the call started.
- A hidden owner window (a never-shown `WS_POPUP` window) worked the same way **while the process also had a visible window**.
- With **every** TimeTug window hidden, a hidden owner window as parent made the call end by itself after about 3 s with `authentication_canceled`, twice, and no Sign in window was seen. The desktop window (`GetDesktopWindow`) as parent opened the dialog in one of two runs. These runs were unattended and the foreground was the desktop, so this is evidence against relying on a hidden owner, not proof.
- With no handle at all the call fails at once (`window_handle_required`).
- An interactive request that names the Windows account (`.WithAccount(PublicClientApplication.OperatingSystemAccount)`) returned a token with no window when consent existed; without it the picker opened even for a consented account.

**Verdict S7 (D3):** a hidden owner window is **not** a dependable parent for the tray case (no visible window). D3 stands as decided: the flyout row says "Open Accounts" and the Windows dialog is opened only from Settings > Accounts or the welcome window, which are visible windows. If the owner's attended run (below) shows the hidden owner working, D3 can be revisited after the phase.

### S8 Scope spelling

Both spellings work with the broker for a silent request: the four short names of Global Constraints (`User.Read`, `MailboxSettings.Read`, `Calendars.Read`, `Calendars.Read.Shared`) and the same names prefixed with `https://graph.microsoft.com/`. The returned scopes echo the spelling asked for. Use the short names, as `MicrosoftConnectorKind.scopes` does. The authority must allow personal accounts (`common`): with an organizations-only authority the personal Windows account fails as in the first row of S3, and with `consumers` it works.

## Verdicts and plan impact

| Spec risk | Verdict | Fallback that applies |
| --- | --- | --- |
| S2 account listing | works for the Windows sign-in account; before-consent and other accounts to be confirmed | the unlabelled card text when the listing does not give exactly one named account |
| S4 ids | stable across process restarts | match `Username` to the stored email (keep it) |
| S6 cancellation | token cancellation closes the dialog at once | none needed; late results are dropped by the engine anyway |
| S7 hidden owner window | not dependable with no visible window | D3: reconnect only from Settings > Accounts |

Tasks 14 to 17 do not change in substance. The plan (which lives in `binary-companion/timetug`, not in this repository) needs these small edits in a later docs change there: Task 14's error table is the S3 table above; the first-run card naming rule is the S2 verdict; the package check recipe in the plan's packaging task is the S1 recipe; the MSAL pin is the S1 pin. They were not made in this pull request because it is in another repository.

## Still to run (owner, on the ARM64 VM, at the console)

The installed development package on the VM contains the probe (`BinaryCompanion.TimeTug.Dev`, built from the scratch branch `claude/spike-wam-probe`; a later `vm-selftest.sh` or `vm-sync.sh` run replaces it). Run each line in a normal (not elevated) PowerShell on the VM's desktop. Each prints what it observed and writes `C:\Users\Public\timetug-vm\probe\<Name>\result.json`; it contains hashes and error codes, no email, id or token, and may be shared. Type passwords and approve MFA yourself; the probe never asks for them. `-Hold 900` lets a run wait up to 15 minutes for you.

1. **Work account with MFA (D4), S2 and S3.** Settings > Accounts > Email & accounts > Add a work or school account; sign in. Then:
   `& 'C:\Users\Public\timetug-vm\probe.ps1' -Name o1 -Spec 'list;os=1|silent;kind=os|interactive;parent=visible;prompt=selectaccount;save=1' -Hold 900`
   In the Sign in window pick the work account, sign in, approve MFA and allow if a consent page appears. Write down: whether a consent page appeared and what its first line says; whether the account was named in the list before you signed in (the `list` lines of the result).
2. **Personal test account added under Email & accounts (not the Windows sign-in).** Add it (Settings > Accounts > Email & accounts > Add an account), then the same command with `-Name o2`. Note whether the first `list` shows one account (the Windows sign-in) or two.
3. **Stable ids after a restart of the PC.** After step 1: restart Windows, sign in, then
   `& 'C:\Users\Public\timetug-vm\probe.ps1' -Name o3 -Spec 'list;os=1|silent;kind=saved'`
4. **Consent declined.** With an account that has never used TimeTug (a new test account or the work account before step 1), run step 1's command and press Cancel or the window's close button on the consent page. The result records the error code.
5. **Silent errors after a change at the provider.** After each of the following, run
   `& 'C:\Users\Public\timetug-vm\probe.ps1' -Name o5a -Spec 'silent;kind=saved'` (use `o5b`, `o5c` for the next ones):
   (a) change the test account's password at account.microsoft.com (or the work account's at its portal) and, if prompted, sign in to Windows again; (b) withdraw consent at account.live.com/consent/Manage (personal) or the organization's My Apps page (work); (c) remove the account under Settings > Accounts > Email & accounts. Re-add and re-run step 1 between them.
6. **A tenant that blocks user consent**, if you have one: step 1's command with an account of that tenant; copy the text the dialog shows.
7. **Parenting with a person at the screen (S7, D3).** Run
   `& 'C:\Users\Public\timetug-vm\probe.ps1' -Name o7 -Spec 'minimize|sleep;s=2|interactive;parent=hidden;prompt=selectaccount;cancelAfter=60;survey=5' -Hold 900`
   and say whether a Sign in window appeared in front, and whether closing it with its X ended the call (the result shows `authentication_canceled` and how long it took). Repeat with `parent=desktop` in place of `parent=hidden`.
8. **A local-only Windows account.** Needs a PC or a second Windows user without a Microsoft account (the hosted runners already show the empty listing, so this is optional).
9. **Cleanup.** `& 'C:\Users\Public\timetug-vm\probe.ps1' -Name o9 -Spec 'forget'` deletes the stored account id the probe keeps in the package's data folder. Remove the extra test accounts from Windows when done.

If any step differs from the observations above, the agent amends this document and, if needed, the Task 14 error table.
