# Remote verification on the owner's x64 PC

The Rust-engine cutover (checklist section 8 in `windows-checklist.md`) has to be proven on x64 as well as on the ARM64 VM. The owner's PC is the x64 machine. This runbook lets an agent run what a machine can run on it, over SSH, while the owner keeps everything that needs real accounts, a password or a decision. It has three parts: the owner's one-time setup and teardown (part 1), what the agent runs and what stays with the owner (part 2), and the rules both sides keep (part 3). Part 4 explains why some scripts cannot run directly over SSH and what the repository provides instead.

**What this is not.** The agent never gets the owner's own Windows account, password, real calendar accounts, tokens or `dev-oauth.json`. It works only as a separate standard test user on the PC, over a key-only SSH login that is open on the home network only and that the owner switches on and off.

## 1. Owner setup (one time, about 15 minutes)

Run PowerShell commands in **Windows Terminal or PowerShell as administrator** (Start, type `PowerShell`, right-click, Run as administrator) unless a step says otherwise. Lines starting with `#` are comments.

### 1.1 Make the home network "Private"

Settings > Network & internet > Wi-Fi (or Ethernet) > your network > Network profile type > **Private network**. Check:

```powershell
Get-NetConnectionProfile      # NetworkCategory must say Private
```

### 1.2 Create the standard test user

Settings > Accounts > Other users > Add account > "I don't have this person's sign-in information" > "Add a user without a Microsoft account" > user name `ttagent`, a password you choose. **Keep the password to yourself**: it is only for you to sign in at the PC's screen; SSH never uses it. Or, in the elevated PowerShell:

```powershell
$p = Read-Host 'Password for ttagent (you keep it)' -AsSecureString
New-LocalUser -Name ttagent -FullName 'TimeTug test user' -Password $p
Add-LocalGroupMember -Group 'Users' -Member 'ttagent'
Get-LocalGroupMember -Group 'Administrators'     # ttagent must NOT be listed
```

Then sign in once as `ttagent` (Start > your picture > `ttagent`) and finish the first-run screens, so the profile `C:\Users\ttagent` exists. Switch back to your own account with Start > `ttagent` > your name (do not sign `ttagent` out: the desktop tests below need that session). Do not add any account of yours in `ttagent`.

### 1.3 Install the OpenSSH Server

Settings > System > Optional features > **View features** (Add an optional feature) > search `OpenSSH Server` > Next > Install. (This is a Microsoft download of a few MB through Windows Update.) Or:

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Get-WindowsCapability -Online -Name OpenSSH.Server*          # State : Installed
```

### 1.4 Allow key-only login for `ttagent` and nothing else

Start the service once so it writes its default configuration, stop it again, and put the restrictions at the **top** of the file (sshd uses the first value it reads for each keyword; a line added at the end would fall inside the file's `Match Group administrators` block):

```powershell
Start-Service sshd; Stop-Service sshd
$cfg = 'C:\ProgramData\ssh\sshd_config'
Copy-Item $cfg "$cfg.timetug-backup"
$block = @"
# TimeTug remote verification (removed by the teardown in part 1.9)
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes
AllowUsers ttagent
AllowTcpForwarding no
X11Forwarding no
PermitTunnel no
MaxAuthTries 3
LoginGraceTime 30

"@
Set-Content -Path $cfg -Value ($block + (Get-Content -Raw $cfg))
& "$env:SystemRoot\System32\OpenSSH\sshd.exe" -t          # prints nothing when the file is valid
```

Optional, so that commands run in PowerShell 5.1 instead of `cmd`:

```powershell
New-ItemProperty -Path HKLM:\SOFTWARE\OpenSSH -Name DefaultShell -PropertyType String -Force `
  -Value 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
```

### 1.5 Install the agent's public key

The agent makes a key pair on its Mac (`ssh-keygen -t ed25519 -f ~/.ssh/timetug_x64_test -C timetug-agent`) and gives you **one line** that starts with `ssh-ed25519` (the public half: it is safe to paste in chat). It also tells you its Mac's address on your network. Put both in the file, so the key works only from that address:

```powershell
$line = 'from="MAC_ADDRESS",no-port-forwarding,no-agent-forwarding,no-X11-forwarding ssh-ed25519 AAAA...paste the whole line... timetug-agent'
$dir  = 'C:\Users\ttagent\.ssh'
New-Item -ItemType Directory -Force $dir | Out-Null
Set-Content -Path "$dir\authorized_keys" -Value $line -Encoding ascii
icacls "$dir\authorized_keys" /inheritance:r /grant "SYSTEM:(F)" "BUILTIN\Administrators:(F)" "$($env:COMPUTERNAME)\ttagent:(F)"
```

Replace `MAC_ADDRESS` with the Mac's address the agent gave you (for example `192.168.1.50`) and the rest of the line with the key line.

### 1.6 Open the firewall for the home network only

Settings > Privacy & security > Windows Security > Firewall & network protection > Advanced settings > Inbound Rules > "OpenSSH SSH Server (sshd)" > Properties > Scope (Remote IP address: Local subnet) and Advanced (Profiles: Private only). Or:

```powershell
Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue   # exists after 1.3 on most PCs
Set-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -Enabled True -Profile Private -RemoteAddress LocalSubnet
# if the rule does not exist:
New-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -DisplayName 'OpenSSH SSH Server (TimeTug test, LAN only)' `
  -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 -Profile Private -RemoteAddress LocalSubnet
Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' | Get-NetFirewallAddressFilter      # RemoteAddress: LocalSubnet
```

Do **not** forward port 22 on your router.

### 1.7 Start the service by hand (not at boot)

```powershell
Set-Service sshd -StartupType Manual
Start-Service sshd
ipconfig | Select-String 'IPv4'                      # the PC's address on your network
ssh-keygen -lf C:\ProgramData\ssh\ssh_host_ed25519_key.pub       # the host key fingerprint
```

Give the agent the address and the fingerprint line (`256 SHA256:... (ED25519)`). The agent connects once, compares the fingerprint it is shown with yours and only then trusts the PC. Each connection is logged in Event Viewer > Applications and Services Logs > OpenSSH > Operational (`Get-WinEvent -LogName OpenSSH/Operational -MaxEvents 20`).

### 1.8 For the tests that need the screen

`ttagent` must be signed in and, for the self-test, the tray and the lock check, **be the user at the screen**: switch to `ttagent` (Start > your picture > `ttagent`) and do not touch the PC while the agent works; the agent tells you when. For the sleep check the PC stays plugged in. Whenever the agent asks you to run a prepared command as administrator (a certificate to trust, the power settings), read it first: it names the script and the folder in `C:\Users\ttagent\timetug\`.

### 1.9 Turn it all off again (teardown)

Do this when the agent says it is finished, or at any time you want to stop. Each line undoes one step; run them in the elevated PowerShell:

```powershell
# 1. Stop SSH and remove its door
Stop-Service sshd
Set-Service sshd -StartupType Disabled
Remove-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue
# 2. Restore the sshd configuration, remove the key and the default shell
Copy-Item 'C:\ProgramData\ssh\sshd_config.timetug-backup' 'C:\ProgramData\ssh\sshd_config' -Force -ErrorAction SilentlyContinue
Remove-Item 'C:\Users\ttagent\.ssh' -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path HKLM:\SOFTWARE\OpenSSH -Name DefaultShell -ErrorAction SilentlyContinue
# 3. Put the power settings back (only if the agent had you apply them)
powershell -ExecutionPolicy Bypass -File C:\Users\ttagent\timetug\scripts\vm\power-settings.ps1 -Action Restore
# 4. Remove the throwaway test certificates the agent had you trust
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=TimeTug Development' | Remove-Item
# 5. Remove the OpenSSH Server feature entirely (optional; Settings > System > Optional features > OpenSSH Server > Uninstall)
Remove-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
# 6. Sign ttagent out (Start > ttagent > Sign out), then remove the user and its files
Remove-LocalUser -Name ttagent
Get-CimInstance Win32_UserProfile | Where-Object LocalPath -eq 'C:\Users\ttagent' | Remove-CimInstance
```

Step 3 must run **before** step 6 deletes the folder it lives in; if the folder is already gone and the agent had you change power settings, run `powercfg /restoredefaultschemes` only if you are happy with Windows' defaults, or ask the agent for the saved values (`C:\ProgramData\TimeTug-power-backup.json` stays until `Restore` ran). Check afterwards: `Get-Service sshd` (Stopped, Disabled), `Get-LocalUser ttagent` (not found), `Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP'` (not found). The agent also deletes its public key on the Mac.

## 2. What the agent runs, and what stays with the owner

The agent connects with `scripts/vm/ssh-ps.sh --host ttagent@<address>` (code is sent encoded, so no quoting problems; `--put` copies files with `scp`). Everything below runs as `ttagent`. The builds come from the CI artifacts of `timetug-windows` (the same ones as in section 8 of the checklist: `msix-x64` of a green `main` run from before the engine change, and of the newest green-jobs `rust-engine-trial` run), downloaded on the agent's Mac, with their `commit.txt`.

**Before anything is copied or installed**, the agent says what it wants to put on the PC (file names, commit, sizes, the SHA-256 of `dev.cer`) and waits for the owner's yes. The same goes for anything else that downloads or installs: for example the .NET 10 Desktop Runtime x64 the development package needs (`dotnet --list-runtimes` shows `Microsoft.WindowsDesktop.App 10.`): the agent reports it missing and the owner installs it, the agent never does.

| # | What | How, over SSH | Needs | Result |
|---|---|---|---|---|
| 1 | Read-only look at the PC | `ssh-ps.sh` runs `[Environment]::OSVersion`, `[Runtime.InteropServices.RuntimeInformation]::OSArchitecture`, `dotnet --list-runtimes`, `Get-AppxPackage BinaryCompanion.TimeTug*`, `scripts\vm\power-settings.ps1 -Action Status`, `query user` | nothing | architecture is x64, runtimes, what is installed, power plan values |
| 2 | Put the scripts and builds on the PC | `ssh-ps.sh --put` copies `scripts/vm`, `scripts/ci/run-selftest.ps1`, `selftest-scenarios.txt` and the artifact folders to `C:\Users\ttagent\timetug\` | the owner's yes (see above) | files under `ttagent`'s home only |
| 3 | Trust the throwaway certificate | **the owner** runs `vm-install.ps1 -Folder <folder> -Stage Trust` elevated (the agent prints the exact line) | the owner, elevated | `CN=TimeTug Development` in TrustedPeople; removed by teardown step 4 |
| 4 | Install a build for `ttagent` | `run-in-user-session.ps1` runs `vm-install.ps1 -Stage Install` (this removes any installed copy of the dev package, **for `ttagent` only**) | step 3 | package installed for `ttagent` |
| 5 | **x64 self-test** | `run-in-user-session.ps1` runs `run-selftest.ps1 -OutputFolder ...` (every scenario in `selftest-scenarios.txt`); the agent copies `result.json` files and screenshots back | `ttagent` at the screen (1.8) | scenario count and pass/fail; the same count as ARM64 and CI |
| 6 | **8.1 repeated starts** | `run-in-user-session.ps1` runs `start-cycles.ps1 -Cycles 10`; then again after the sleep check and after the owner signs `ttagent` out and in | `ttagent` at the screen | cycles done, no "TimeTug couldn't start"; if it appears the dialog text and the `TimeTug could not start.` log line are reported verbatim and nothing is dismissed |
| 7 | Scheduled-wake sleep and resume (8.7 without a meeting) | the owner applies the power settings (part 3); `run-in-user-session.ps1 -Detach` runs `session-check.ps1 -Mode Sleep`; the PC sleeps, wakes itself, the agent reconnects and reads the job with `-Collect` | power settings applied; PC plugged in; the owner does not touch it | same process alive and responding, no dialog, idle CPU after the wake, the wake task fired near the plan |
| 8 | Lock and unlock (8.6 without a meeting) | `run-in-user-session.ps1 -Detach` runs `session-check.ps1 -Mode Lock`; **the owner unlocks** `ttagent` with the password | the owner at the PC | same as 7 |
| 9 | Synthetic-data upgrade check (8.13 without real accounts) | install the Swift-engine build for `ttagent` (step 4), copy the four files of `scripts/vm/upgrade-fixtures/` of the `rust-engine-trial` branch into its `LocalState` (names, sizes and hashes only are ever printed), then `owner-upgrade-check.ps1 -Step Before`, **the owner** runs `-Step Trust` for the trial certificate elevated, then `-Step Upgrade` through `run-in-user-session.ps1` | step 3 twice (two builds, two certificates) | the comparison table: nothing deleted or emptied, no dialog, the new package holds the Rust engine library |
| 10 | x64 measurement | `run-in-user-session.ps1 -Detach` runs `vm-measure.ps1 -Label swift` and later `-Label rust` (about 46 minutes each; the script keeps the PC awake) | `ttagent` signed in; the PC left alone | the two JSON files, copied back for `baselines.md` of `timetug-shared` |

**Stays with the owner** (nothing here is run by the agent, because it needs a real account, a password, a person or a decision): every item that needs real Google, Microsoft, iCloud or iCal accounts (8.2 to 8.5, 8.9 to 8.12); a takeover at the lead time, while the screen is locked and across a real meeting start with sleep and lid close (8.4 to 8.7: the sleep check above proves the process survives, not that exactly one late takeover appears); the time-zone change (8.8); the upgrade of the owner's own account with real accounts (8.13, with `owner-upgrade-check.ps1` as its helper); the footprint on the owner's own account (8.14); the clean launch after the owner's own sign-in with launch at sign-in on; unlocking, signing in and any password; copying `dev-oauth.json` (by hand, never read or printed); the decision to install, to trust a certificate, to merge or to release.

## 3. Rules for both sides

- **Nothing is downloaded or installed on the PC without the owner's yes.** That includes the builds, the scripts, a runtime, a certificate and any Windows feature. The agent asks with the file names, the commit and the size; the owner answers in chat. A yes covers that list, not a later one.
- **The agent never** changes the owner's own account, its files or settings, reads `dev-oauth.json`, tokens, the Credential Locker or calendar content, enters or asks for a password, disables Windows Security or the firewall, or leaves a service, user or rule behind.
- **Settings the agent may change, and must restore.** Only power settings, and only through the owner: `power-settings.ps1 -Action Apply` run elevated by the owner changes the **wake timers** (Allow wake timers: Enable) and, if asked, the **sleep time-out** and the **display time-out** of the active plan, after saving the old values to `C:\ProgramData\TimeTug-power-backup.json`; `-Action Restore` puts every saved value back and deletes the file. The agent asks for `Restore` the moment the sleep and measurement tests are over (it reads `-Action Status` before and after and reports the values). The standard test user cannot change them, by design. The agent's own scheduled tasks (`TimeTug-user-session-*`, `TimeTug-session-check-wake`) belong to `ttagent` and are removed by the scripts that create them; the agent checks `Get-ScheduledTask TimeTug-*` is empty when it finishes.
- **Never committed or sent on:** screenshots, dumps and logs from the PC stay on the agent's Mac in a scratch folder; only numbers, pass/fail lines and verbatim dialog text and log reasons go in reports. The throwaway certificate is public-only (`dev.cer`).
- **The owner can stop at any moment**: `Stop-Service sshd` ends every connection (part 1.9, step 1).

## 4. Which scripts need the desktop, and how that works over SSH

A command that sshd starts runs in a **non-interactive logon session**: it has the user's profile and files but no desktop, no windows, no tray, no UI Automation tree, no screen capture and no `SendInput`. The Parallels scripts already work around the same limit: `vm-run.sh` runs as SYSTEM in session 0, and `--user` (`prlctl exec --current-user`) runs in the user's desktop session. Over SSH there is no `--current-user`, so `scripts/vm/run-in-user-session.ps1` does the same job: it registers a one-shot **scheduled task for the same user with the Interactive logon type** (a user needs no administrator rights to schedule a task for themselves, no password is stored), starts it, follows its log file and prints it, waits for an exit-code file, and removes the task. The user must be signed in at the console (a locked session still runs the app; it cannot take input or screen captures). It was tried on the ARM64 VM from a SYSTEM session (session 0, like SSH) with the target script running in session 1 and seeing the taskbar through UI Automation.

| Script | Where it can run | Over SSH |
|---|---|---|
| `scripts/ci/run-selftest.ps1`, `run-a11y.ps1` | desktop session (it says so: "Must run in an interactive session") | through `run-in-user-session.ps1` |
| `scripts/vm/start-cycles.ps1`, `session-check.ps1`, `vm-measure.ps1` | desktop session (UI Automation, the tray, a window, the mouse) | through `run-in-user-session.ps1` (`-Detach` for the long ones, then `-Collect <name>`) |
| `scripts/vm/vm-install.ps1` `-Stage Trust` | elevated (administrator or SYSTEM) | **the owner** runs it; the standard user cannot |
| `scripts/vm/vm-install.ps1` `-Stage Install` | the user's own session (per-user registration of the package) | through `run-in-user-session.ps1`, the proven path of `vm_job user` |
| `scripts/vm/owner-upgrade-check.ps1` | `-Step Before` any session; `-Step Trust` elevated; `-Step Upgrade` starts the app, so desktop | `Before` directly, `Trust` by the owner, `Upgrade` through `run-in-user-session.ps1` |
| `scripts/vm/power-settings.ps1` | elevated for `Apply` and `Restore`; `Status` any | `Status` directly, the others by the owner |
| `scripts/vm/vm-*.sh`, `lib.sh` | the Mac, driving Parallels (`prlctl`) | not applicable: `ssh-ps.sh` plays their part for a real PC |

Adapted for this runbook (all with tests in `scripts/vm/tests`, run by the `architecture` CI job): `run-in-user-session.ps1`, `ssh-ps.sh`, `start-cycles.ps1`, `session-check.ps1`, `power-settings.ps1` and `owner-upgrade-check.ps1` are new; `vm-install.ps1`, `run-selftest.ps1` and `vm-measure.ps1` are unchanged. **Not tested on a real SSH connection** (only from a SYSTEM session, which has the same no-desktop property): the first real run starts with item 1 above and a one-cycle `start-cycles.ps1`. `start-cycles.ps1` clicks the Quit item of the tray menu by its place at the bottom of the menu, scaled by the system DPI (the menu's items are not visible to UI Automation); it was proven at 150% on the ARM64 VM, and if the click point is wrong on the owner's PC the script stops with the app still running and says so, it does not kill anything.
