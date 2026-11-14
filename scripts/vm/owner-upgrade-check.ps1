<#
.SYNOPSIS
  Checklist item 8.13 on a real PC: installs the trial package OVER the installed Swift-engine package and shows what the install and the first
  start did to the app's data folder. Non-destructive: it never removes a package and never reads account data.
.DESCRIPTION
  Runs in Windows PowerShell 5.1 or PowerShell 7 on the PC that has the real accounts. Three steps, because they need different rights:

    Before   (normal PowerShell)  Read-only preflight. Checks that the architecture of the PC, of the installed package and of the build
                                  folders match, that the trial version is higher than the installed one, that dev-oauth.json exists (only
                                  its presence is tested, it is never opened), and saves the first snapshot of the data folder.
    Trust    (ELEVATED PowerShell) Trusts the trial package's throwaway public certificate (LocalMachine\TrustedPeople). Nothing else.
    Upgrade  (normal PowerShell)  Quit TimeTug from the tray first. Takes a snapshot, installs the trial package over the old one with
                                  Add-AppxPackage, takes a snapshot again (an install must not touch the data), starts TimeTug, waits,
                                  takes the last snapshot and prints a before/after comparison. Exit code 0 only when every check passed.

  A snapshot is the list of files in the package's LocalState folder with size and SHA-256 (the logs folder is left out). Contents are never read
  into the output, never copied and never uploaded; a file name that contains an @ (an e-mail address) is replaced by a masked name. Tokens live
  in the Credential Locker, not in that folder, and are not touched. The comparison therefore proves "no data file was deleted, emptied or
  replaced by the install", not that every account still signs in: that is the owner's part of 8.13 (open Settings > Accounts, change a
  meeting on each calendar and see it arrive).

  This script never calls Remove-AppxPackage and never uses vm-install.ps1 -Stage Install or All (they remove the installed package, and its
  data with it). It only uses the certificate functions of vm-install.ps1 (the one beside this script, or the one in the trial folder).
.PARAMETER Step
  Before (default), Trust or Upgrade.
.PARAMETER TrialFolder
  The unpacked msix-x64 or msix-arm64 artifact of the trial build (the .msix, Dependencies\<arch>\, dev.cer).
.PARAMETER OldFolder
  Optional: the unpacked artifact of the Swift-engine build. Only its architecture is checked (it is how you reinstall the old build later).
.PARAMETER OutputFolder
  Where the snapshots and the result go. Default: %LOCALAPPDATA%\TimeTug-owner-upgrade-check (outside the package, so it survives a reinstall).
.PARAMETER StartWaitSeconds
  How long TimeTug must keep running after the start. Default 20.
.PARAMETER NoStart
  Upgrade: do not start TimeTug (the snapshots before and after the install are still compared).
#>
[CmdletBinding()]
param(
    [ValidateSet('Before', 'Trust', 'Upgrade')]
    [string]$Step = 'Before',
    [string]$TrialFolder,
    [string]$OldFolder,
    [string]$OutputFolder,
    [int]$StartWaitSeconds = 20,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$script:PackageName = 'BinaryCompanion.TimeTug.Dev'
$script:ProcessName = 'TimeTug'
# (LOCALAPPDATA is empty on Linux and macOS, where only the tests load this file.)
$script:LocalAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [IO.Path]::GetTempPath() }
$script:Alias = Join-Path $script:LocalAppData 'Microsoft\WindowsApps\timetug-dev.exe'
$script:StartFailureLine = 'TimeTug could not start.'

function Write-Step([string]$Text) { Write-Host ("[owner-upgrade-check] {0}" -f $Text) }

# ---- pure helpers (tested in scripts/vm/tests/owner-upgrade-check.tests.ps1) ---------------------------------------------------------------

# 'Arm64', 'ARM64', 'arm64' -> 'arm64'; 'X64', 'x64', 'AMD64' -> 'x64'; null when unknown.
function ConvertTo-ArchName([string]$Name) {
    switch -Regex ($Name) {
        '^(arm64|aarch64)$' { return 'arm64' }
        '^(x64|amd64)$' { return 'x64' }
        '^(x86|i386)$' { return 'x86' }
        default { return $null }
    }
}

# The names the architecture checks compare. Returns a list of problem sentences (empty = everything matches).
function Get-ArchitectureProblem([string]$PcArchitecture, [string]$TrialPackageArchitecture, [string]$InstalledArchitecture, [string]$OldPackageArchitecture) {
    $problems = New-Object System.Collections.Generic.List[string]
    $pc = ConvertTo-ArchName $PcArchitecture
    if (-not $pc) { $problems.Add("unknown PC architecture '$PcArchitecture'") }
    $items = @(
        @{ What = 'the trial build folder'; Value = $TrialPackageArchitecture },
        @{ What = 'the installed package'; Value = $InstalledArchitecture },
        @{ What = 'the Swift-engine build folder'; Value = $OldPackageArchitecture })
    foreach ($item in $items) {
        if ([string]::IsNullOrEmpty($item.Value)) { continue }
        $arch = ConvertTo-ArchName $item.Value
        if (-not $arch) { $problems.Add("cannot tell the architecture of $($item.What) ('$($item.Value)')") }
        elseif ($pc -and $arch -ne $pc) { $problems.Add("$($item.What) is $arch but this PC is $pc") }
    }
    return @($problems)
}

# True when version A is higher than version B (both four-part package versions such as 0.1.1.0).
function Test-VersionHigher([string]$Candidate, [string]$Installed) {
    return ([version]$Candidate) -gt ([version]$Installed)
}

# A relative path that contains an @ (it may be an e-mail address) is replaced by a name that carries only the extension and a short hash.
function ConvertTo-SafePath([string]$RelativePath) {
    if ($RelativePath -notmatch '@') { return $RelativePath }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($RelativePath)) | ForEach-Object { $_.ToString('x2') }) -join '' }
    finally { $sha.Dispose() }
    $extension = [IO.Path]::GetExtension($RelativePath)
    if ($extension -match '@') { $extension = '' }
    return ('<masked-{0}>{1}' -f $hash.Substring(0, 8), $extension)
}

function Get-FileSha256([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        # ReadWrite|Delete sharing: the app may still hold a file open.
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        try { return (($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join '') }
        finally { $stream.Dispose() }
    }
    catch { return 'unreadable' }
    finally { $sha.Dispose() }
}

# The snapshot: one record per file under the data folder (not the logs folder): path (masked when it has an @), size, SHA-256. No contents.
function Get-DataSnapshot([string]$Folder) {
    $records = @{}
    if (-not (Test-Path -LiteralPath $Folder)) { return $records }
    $root = (Resolve-Path -LiteralPath $Folder).Path.TrimEnd('\', '/')
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        $relative = $file.FullName.Substring($root.Length + 1)
        if ($relative -match '^logs[\\/]') { continue }
        $key = ConvertTo-SafePath $relative
        $records[$key] = [pscustomobject]@{ Path = $key; Length = [int64]$file.Length; Sha256 = (Get-FileSha256 $file.FullName) }
    }
    return $records
}

# Compares two snapshots (hashtables from Get-DataSnapshot). Deleted: gone. Emptied: had bytes, now has none. Changed: the bytes differ.
function Compare-DataSnapshot($Before, $After) {
    $deleted = @($Before.Keys | Where-Object { -not $After.ContainsKey($_) } | Sort-Object)
    $added = @($After.Keys | Where-Object { -not $Before.ContainsKey($_) } | Sort-Object)
    $emptied = @($Before.Keys | Where-Object { $After.ContainsKey($_) -and $After[$_].Length -eq 0 -and $Before[$_].Length -gt 0 } | Sort-Object)
    $changed = @($Before.Keys | Where-Object { $After.ContainsKey($_) -and $After[$_].Sha256 -ne $Before[$_].Sha256 } | Sort-Object)
    return [pscustomobject]@{ Deleted = $deleted; Added = $added; Emptied = $emptied; Changed = $changed; Identical = (($deleted.Count + $added.Count + $changed.Count) -eq 0) }
}

function Format-SnapshotTable($Snapshot) {
    return @($Snapshot.Keys | Sort-Object | ForEach-Object { '  {0,10} bytes  {1}  {2}' -f $Snapshot[$_].Length, $Snapshot[$_].Sha256.Substring(0, [Math]::Min(12, $Snapshot[$_].Sha256.Length)), $_ })
}

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Lines of the app's log that say it could not start (only these lines are read out; the log's other lines are never printed).
function Get-StartFailureLine([string]$LogPath, [int64]$FromByte) {
    if (-not (Test-Path -LiteralPath $LogPath)) { return @() }
    $stream = [IO.File]::Open($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        if ($FromByte -gt 0 -and $FromByte -le $stream.Length) { $null = $stream.Seek($FromByte, [IO.SeekOrigin]::Begin) }
        $reader = New-Object IO.StreamReader($stream)
        $found = New-Object System.Collections.Generic.List[string]
        while (($line = $reader.ReadLine()) -ne $null) { if ($line.Contains($script:StartFailureLine)) { $found.Add($line) } }
        return @($found)
    }
    finally { $stream.Dispose() }
}

# ---- Windows side ---------------------------------------------------------------------------------------------------------------------------

# Where the certificate functions of vm-install.ps1 come from: the copy beside this script, else the one shipped in the trial artifact.
function Find-InstallHelper([string]$TrialFolder) {
    $candidates = @((Join-Path $PSScriptRoot 'vm-install.ps1'))
    if ($TrialFolder) { $candidates += (Join-Path $TrialFolder 'vm-install.ps1') }
    $found = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $found) { throw 'vm-install.ps1 not found beside this script or in the trial folder' }
    return $found
}

function Get-AppPackageSafe { return Get-AppxPackage -Name $script:PackageName }

function Get-DataFolder($Package) { return Join-Path $script:LocalAppData ("Packages\{0}\LocalState" -f $Package.PackageFamilyName) }

function Get-OutputFolder([string]$Requested) {
    if ($Requested) { $folder = $Requested } else { $folder = Join-Path $script:LocalAppData 'TimeTug-owner-upgrade-check' }
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    return (Resolve-Path -LiteralPath $folder).Path
}

function Save-Snapshot($Snapshot, [string]$Path) {
    @($Snapshot.Values | Sort-Object Path) | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Read-Snapshot([string]$Path) {
    $records = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    # (foreach, not @(): Windows PowerShell 5.1 returns a parsed JSON array as one object, and @() would keep it one item.)
    foreach ($item in (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json)) {
        if ($item) { $records[[string]$item.Path] = [pscustomobject]@{ Path = [string]$item.Path; Length = [int64]$item.Length; Sha256 = [string]$item.Sha256 } }
    }
    return $records
}

# Everything both Before and Upgrade must know before touching anything. Returns the facts; throws on a mismatch.
function Get-Preflight([string]$TrialFolder, [string]$OldFolder) {
    if (-not $TrialFolder) { throw '-TrialFolder is required: the unpacked msix-x64 or msix-arm64 artifact of the trial build' }
    $files = Find-PackageFiles -Folder $TrialFolder -Package $null -Certificate $null -Platform $null
    $manifestVersion = $null
    if ($files.Package -match '_(\d+\.\d+\.\d+\.\d+)_') { $manifestVersion = $Matches[1] }
    $oldArch = $null
    if ($OldFolder) { $oldArch = (Find-PackageFiles -Folder $OldFolder -Package $null -Certificate $null -Platform $null).Platform }
    $installed = Get-AppPackageSafe
    if (-not $installed) { throw "the package $($script:PackageName) is not installed on this PC: there is nothing to upgrade (install the Swift-engine build first, see item 8.13)" }
    $installedArch = [string]$installed.Architecture
    $pcArch = [string][System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $problems = @(Get-ArchitectureProblem $pcArch $files.Platform $installedArch $oldArch)
    if ($files.Dependencies.Count -eq 0) { $problems += "no Dependencies\$($files.Platform) folder with the Windows App Runtime package next to the trial .msix" }
    if ($manifestVersion -and -not (Test-VersionHigher $manifestVersion ([string]$installed.Version))) {
        $problems += "the trial package version $manifestVersion is not higher than the installed $($installed.Version): Windows refuses a same-version replacement (0x80073CFB)"
    }
    if ($problems.Count -gt 0) { throw ("preflight failed:`n  " + ($problems -join "`n  ")) }
    return [pscustomobject]@{ Files = $files; Installed = $installed; PcArchitecture = (ConvertTo-ArchName $pcArch); TrialVersion = $manifestVersion }
}

function Write-OAuthNote {
    # Presence only: the file holds client ids and is never opened here.
    $path = Join-Path $script:LocalAppData 'TimeTug\dev-oauth.json'
    if (Test-Path -LiteralPath $path -PathType Leaf) { Write-Step 'dev-oauth.json is present (not read)' }
    else { Write-Step 'WARNING: dev-oauth.json is missing under %LOCALAPPDATA%\TimeTug: Google and Microsoft accounts cannot be added or checked in this build (see the checklist, section 8)' }
}

function Write-DataCard([string]$Title, $Snapshot) {
    Write-Step ("{0}: {1} file(s), {2} bytes" -f $Title, $Snapshot.Count, (($Snapshot.Values | Measure-Object -Property Length -Sum).Sum))
}

function Invoke-Before {
    if (Test-Elevated) { throw 'run the Before step from a normal (not elevated) PowerShell' }
    $facts = Get-Preflight $TrialFolder $OldFolder
    $out = Get-OutputFolder $OutputFolder
    Write-Step ("PC architecture {0}; installed package {1} (version {2}); trial package {3} (version {4})" -f $facts.PcArchitecture, $facts.Installed.PackageFullName, $facts.Installed.Version, (Split-Path -Leaf $facts.Files.Package), $facts.TrialVersion)
    Write-Step 'architecture check passed: the PC, the installed package and the build folders agree'
    Write-OAuthNote
    $data = Get-DataFolder $facts.Installed
    if (-not (Test-Path -LiteralPath $data)) { throw "the data folder does not exist yet: $data (start TimeTug once, add the accounts, quit it)" }
    $snapshot = Get-DataSnapshot $data
    Save-Snapshot $snapshot (Join-Path $out 'snapshot-before.json')
    Write-DataCard 'data folder now' $snapshot
    Format-SnapshotTable $snapshot | ForEach-Object { Write-Host $_ }
    if (@(Get-Process -Name $script:ProcessName -ErrorAction SilentlyContinue).Count -gt 0) { Write-Step 'TimeTug is running: quit it from the tray before the Upgrade step' }
    Write-Step "snapshot saved in $out. Next: elevated PowerShell with -Step Trust, then a normal one with -Step Upgrade"
}

function Invoke-TrustStep {
    if (-not (Test-Elevated)) { throw 'the Trust step needs an elevated PowerShell (Run as administrator)' }
    $files = Find-PackageFiles -Folder $TrialFolder -Package $null -Certificate $null -Platform $null
    Invoke-Trust $files
    Write-Step 'certificate trusted. Next: a normal PowerShell with -Step Upgrade'
}

function Test-CertificateTrusted($Files) {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $Files.Certificate
    return [bool](Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })
}

function Invoke-Upgrade {
    if (Test-Elevated) { throw 'run the Upgrade step from a normal (not elevated) PowerShell: the app must not start elevated' }
    $facts = Get-Preflight $TrialFolder $OldFolder
    $files = $facts.Files
    if (-not (Test-CertificateTrusted $files)) { throw 'the trial certificate is not trusted yet: run -Step Trust from an elevated PowerShell first' }
    if (@(Get-Process -Name $script:ProcessName -ErrorAction SilentlyContinue).Count -gt 0) { throw 'TimeTug is running: quit it from the tray icon first (a clean quit), then run this step again' }
    $out = Get-OutputFolder $OutputFolder
    $problems = New-Object System.Collections.Generic.List[string]
    function Assert-That([bool]$Condition, [string]$Text) {
        if ($Condition) { Write-Host ("  ok   {0}" -f $Text) } else { Write-Host ("  FAIL {0}" -f $Text); $problems.Add($Text) }
    }
    Write-OAuthNote

    $old = $facts.Installed
    $data = Get-DataFolder $old
    $first = Read-Snapshot (Join-Path $out 'snapshot-before.json')
    $pre = Get-DataSnapshot $data
    Save-Snapshot $pre (Join-Path $out 'snapshot-pre-install.json')
    Write-DataCard 'data folder just before the install' $pre
    Assert-That ($pre.Count -gt 0) 'the data folder holds files (an empty one has nothing to compare)'
    if ($first) {
        $drift = Compare-DataSnapshot $first $pre
        Write-Step ("since the Before step: {0} file(s) changed, {1} added, {2} gone (information: you used the app in between)" -f $drift.Changed.Count, $drift.Added.Count, $drift.Deleted.Count)
    }

    Write-Step ("installing {0} over {1}" -f (Split-Path -Leaf $files.Package), $old.PackageFullName)
    try { Add-AppxPackage -Path $files.Package -DependencyPath $files.Dependencies }
    catch {
        $message = $_.Exception.Message
        if ($message -match '0x80073CFB') { throw "Windows refuses a package of the same identity and version: the trial package must have a higher version than the installed one. $message" }
        if ($message -match '0x80073D02') { throw "another app that uses the Windows App Runtime is running (the Widgets pack, the Start menu host or search). Close it or sign out and in, and run this step again; nothing was changed. $message" }
        if ($message -match '0x800B010A|0x800B0109|0x800B0100|certificate') { throw "Windows does not trust the package's certificate: run -Step Trust from an elevated PowerShell. $message" }
        throw
    }
    $new = Get-AppPackageSafe
    Assert-That ($null -ne $new) 'a package is installed after the upgrade'
    if ($null -eq $new) { throw 'the package is gone after Add-AppxPackage: stop here and tell the agent' }
    Assert-That ([version]$new.Version -gt [version]$old.Version) ("the installed version rose: {0} to {1}" -f $old.Version, $new.Version)
    Assert-That ($new.PackageFamilyName -eq $old.PackageFamilyName) 'same package family (same app, same data folder)'
    Assert-That ((Get-DataFolder $new) -eq $data) 'the data folder is the same folder'
    $engineDll = @(Get-ChildItem -LiteralPath $new.InstallLocation -Recurse -Filter 'TimeTugEngineC.dll' -File)
    Assert-That ($engineDll.Count -ge 1) ("the new package holds the Rust engine library TimeTugEngineC.dll ({0} found)" -f $engineDll.Count)
    $swiftFiles = @(Get-ChildItem -LiteralPath $new.InstallLocation -Recurse -File | Where-Object { $_.Name -match '^(swift.*|Foundation.*|_FoundationICU|dispatch|BlocksRuntime)\.dll$' })
    Assert-That ($swiftFiles.Count -eq 0) ("the new package holds no Swift runtime file ({0} found)" -f $swiftFiles.Count)

    $installed = Get-DataSnapshot $data
    Save-Snapshot $installed (Join-Path $out 'snapshot-post-install.json')
    $byInstall = Compare-DataSnapshot $pre $installed
    Assert-That $byInstall.Identical ("the install itself changed no data file ({0} deleted, {1} added, {2} changed)" -f $byInstall.Deleted.Count, $byInstall.Added.Count, $byInstall.Changed.Count)

    $started = $false
    $logPath = Join-Path $data 'logs\timetug.log'
    $logLength = 0
    if (Test-Path -LiteralPath $logPath) { $logLength = (Get-Item -LiteralPath $logPath).Length }
    if ($NoStart) { Write-Step 'not starting TimeTug (-NoStart)' }
    else {
        Write-Step ("starting TimeTug and watching it for {0} s" -f $StartWaitSeconds)
        if (-not (Test-Path -LiteralPath $script:Alias)) { throw "execution alias not found: $($script:Alias)" }
        Start-Process -FilePath $script:Alias | Out-Null
        $deadline = [DateTime]::UtcNow.AddSeconds($StartWaitSeconds)
        $everSeen = $false
        $lostAt = $null
        $dialog = $null
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Seconds 1
            $running = @(Get-Process -Name $script:ProcessName -ErrorAction SilentlyContinue)
            if ($running.Count -gt 0) { $everSeen = $true } elseif ($everSeen -and -not $lostAt) { $lostAt = [DateTime]::UtcNow }
            $titles = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match "couldn.t start" } | ForEach-Object { $_.MainWindowTitle })
            if ($titles.Count -gt 0 -and -not $dialog) { $dialog = $titles[0] }
        }
        $stillRunning = @(Get-Process -Name $script:ProcessName -ErrorAction SilentlyContinue)
        $started = $stillRunning.Count -gt 0
        Assert-That $started ("TimeTug is running {0} s after the start" -f $StartWaitSeconds)
        if ($started) {
            $path = $null
            try { $path = $stillRunning[0].Path } catch { $path = $null }
            if ($path) { Assert-That ($path -like ($new.InstallLocation + '*')) 'the running process is the new package, not the old one' }
        }
        Assert-That (-not $dialog) ("no window titled 'TimeTug couldn't start' appeared{0}" -f $(if ($dialog) { ": found '$dialog' (leave it open, do not click Close, and tell the agent)" } else { '' }))
        $failureLines = @(Get-StartFailureLine $logPath $logLength)
        Assert-That ($failureLines.Count -eq 0) ("the log has no '{0}' line since the start" -f $script:StartFailureLine)
        foreach ($line in $failureLines) { Write-Host ("       log: {0}" -f $line) }
    }

    $after = Get-DataSnapshot $data
    Save-Snapshot $after (Join-Path $out 'snapshot-after-start.json')
    $result = Compare-DataSnapshot $pre $after
    Assert-That ($result.Deleted.Count -eq 0) ("no data file was deleted ({0})" -f ($result.Deleted -join ', '))
    Assert-That ($result.Emptied.Count -eq 0) ("no data file was emptied ({0})" -f ($result.Emptied -join ', '))
    Write-Step ("files the new engine changed or added since before the install (information, a rewrite is not a failure): changed [{0}], added [{1}]" -f ($result.Changed -join ', '), ($result.Added -join ', '))

    Write-Host ''
    Write-Host '== before / after =='
    Write-Host ("  package:       {0} -> {1}" -f $old.PackageFullName, $new.PackageFullName)
    Write-Host ("  data files:    {0} before, {1} after the install, {2} after the first start" -f $pre.Count, $installed.Count, $after.Count)
    Write-Host ("  bytes:         {0} before, {1} after the first start" -f (($pre.Values | Measure-Object Length -Sum).Sum), (($after.Values | Measure-Object Length -Sum).Sum))
    Write-Host ("  deleted/emptied: {0}/{1}; changed by the install: {2}; changed by the first start: {3}" -f $result.Deleted.Count, $result.Emptied.Count, $byInstall.Changed.Count, $result.Changed.Count)

    $summary = [ordered]@{
        date = (Get-Date).ToString('s'); oldPackage = $old.PackageFullName; newPackage = $new.PackageFullName; architecture = $facts.PcArchitecture
        filesBefore = $pre.Count; filesAfterInstall = $installed.Count; filesAfterStart = $after.Count
        deleted = $result.Deleted; emptied = $result.Emptied; changedByInstall = $byInstall.Changed; changedByFirstStart = $result.Changed; added = $result.Added
        started = $started; problems = @($problems); passed = ($problems.Count -eq 0)
    }
    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
    Write-Step "result and snapshots are in $out (file names, sizes and hashes only)"
    if ($problems.Count -gt 0) { Write-Host ('owner upgrade check FAILED: ' + ($problems -join '; ')); throw 'owner upgrade check failed' }
    Write-Host 'owner upgrade check passed'
    Write-Host 'Now the part only you can check (8.13 Expect): Settings > Accounts shows every account with its calendars, none asks to sign in again, the settings and the dismissed meeting are as you left them, and each account refreshes after a change made on its calendar.'
}

if ($MyInvocation.InvocationName -ne '.') {
    # Dot-sourced here, at script level, so its functions (Find-PackageFiles, Invoke-Trust) are visible below. It defines its own parameters
    # ($Folder, $Package, $Platform, $Stage ...) too; this script names its own differently on purpose. Its main block does not run when dot-sourced.
    . (Find-InstallHelper $TrialFolder)
    switch ($Step) {
        'Before' { Invoke-Before }
        'Trust' { Invoke-TrustStep }
        'Upgrade' { Invoke-Upgrade }
    }
}
