<#
.SYNOPSIS
  Upgrade in place: installs the development package built with an older engine, gives it synthetic engine data, installs the package built with the
  checked-out engine OVER it, and proves the engine reads the same accounts, settings, ledger and learned corrections afterwards.
.DESCRIPTION
  Runs in Windows (Windows PowerShell 5.1 or 7); scripts/vm/vm-upgrade-check.sh drives it from the Mac. Two stages, because they need different rights
  (as in scripts/ci/build-msix.ps1):
    Build  (SYSTEM or an elevated session) builds two development packages from this checkout, the second as it is (package version raised to 0.1.1.0 for this build only) and the first with
           Directory.Packages.props temporarily pinned to -OldEngineVersion, trusts both throwaway certificates. Nothing is installed.
    Run    (the signed-in user) installs the old package, copies the synthetic files of scripts\vm\upgrade-fixtures into its LocalState, runs the
           self-test scenario upgrade-seed (changes three settings through the engine) and upgrade-state (writes state.json), records every file of
           LocalState, installs the new package over the old one (Add-AppxPackage: the new package is built as version 0.1.1.0 because
           Windows refuses a same-version replacement, and a real upgrade has a higher version), records the files again, runs upgrade-state again and compares.
  Passes only when: the account kinds include microsoft-brokered; accounts, account kinds, settings and the engine's ledger line are equal before and
  after; the settings the seed changed are what the new engine reports; no file of the data folder was deleted or emptied by the install or by the
  new engine; the counts in the data files (ledger records, lessons, verdicts, sync entries) are equal. Nothing here reads or prints calendar content:
  the data is synthetic, and event titles are never written to the results.
.PARAMETER Stage
  Build, Run or All (default; needs an elevated user session).
.PARAMETER Platform
  x64 or ARM64. Default: this machine's architecture (an x64 package cannot run on an ARM64 VM without the x64 .NET runtime).
.PARAMETER OldEngineVersion
  The TimeTug.Engine version of the "before" package. Default: the version pinned on origin/main (the Swift engine).
.PARAMETER OutputFolder
  Where packages and results go. Default: build\upgrade-check under the repository.
#>
[CmdletBinding()]
param(
    [ValidateSet('Build', 'Run', 'All')]
    [string]$Stage = 'All',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'ARM64' } else { 'x64' }),
    [string]$OldEngineVersion,
    [string]$OutputFolder
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutputFolder) { $OutputFolder = Join-Path $repo 'build\upgrade-check' }
$OutputFolder = [System.IO.Path]::GetFullPath($OutputFolder)
$oldFolder = Join-Path $OutputFolder 'old'
$newFolder = Join-Path $OutputFolder 'new'
$packageName = 'BinaryCompanion.TimeTug.Dev'
$alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\timetug-dev.exe'
$installScript = Join-Path $PSScriptRoot 'vm-install.ps1'
$fixtures = Join-Path $PSScriptRoot 'upgrade-fixtures'
$fixtureFiles = 'accounts.json', 'sync-state.json', 'dedup-state.json', 'takeover-ledger.json'

function Write-Step([string]$Text) { Write-Host ("[upgrade-check] {0}" -f $Text) }

function Get-PinnedEngineVersion([string]$Text) {
    $match = [regex]::Match($Text, 'PackageVersion Include="TimeTug\.Engine" Version="([^"]+)"')
    if (-not $match.Success) { throw 'no TimeTug.Engine version in Directory.Packages.props' }
    return $match.Groups[1].Value
}

# ---------------------------------------------------------------------------------------------------------------------------------------------
if ($Stage -in 'Build', 'All') {
    $props = Join-Path $repo 'Directory.Packages.props'
    $original = [System.IO.File]::ReadAllText($props)
    $newVersion = Get-PinnedEngineVersion $original
    if (-not $OldEngineVersion) {
        $main = (& git -C $repo show 'origin/main:Directory.Packages.props') -join "`n"
        if ($LASTEXITCODE -ne 0) { throw 'cannot read Directory.Packages.props of origin/main (git fetch origin first)' }
        $OldEngineVersion = Get-PinnedEngineVersion $main
    }
    if ($OldEngineVersion -eq $newVersion) { throw "the old and the new engine are both ${newVersion}: nothing to upgrade from" }
    Write-Step "building the old package (TimeTug.Engine $OldEngineVersion) and the new one ($newVersion), $Platform"

    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
    try {
        $pinned = $original.Replace("Include=`"TimeTug.Engine`" Version=`"$newVersion`"", "Include=`"TimeTug.Engine`" Version=`"$OldEngineVersion`"")
        [System.IO.File]::WriteAllText($props, $pinned)
        & (Join-Path $repo 'scripts\ci\build-msix.ps1') -Platform $Platform -Stage Build -OutputFolder $oldFolder
    }
    finally { [System.IO.File]::WriteAllText($props, $original) }
    # Both packages carry package version 0.1.0.0 and Windows refuses to replace a package by another of the same version (0x80073CFB), but a real
    # upgrade has a higher one: the new package is built as 0.1.1.0 (the manifest is changed for this build only and restored).
    $manifest = Join-Path $repo 'src\TimeTug.App\Package.appxmanifest'
    $manifestOriginal = [System.IO.File]::ReadAllText($manifest)
    try {
        $bumped = [regex]::Replace($manifestOriginal, '(?s)(<Identity\b[^>]*?\bVersion=")0\.1\.0\.0(")', '${1}0.1.1.0${2}')
        if ($bumped -eq $manifestOriginal) { throw 'could not find the package version 0.1.0.0 in Package.appxmanifest' }
        [System.IO.File]::WriteAllText($manifest, $bumped)
        & (Join-Path $repo 'scripts\ci\build-msix.ps1') -Platform $Platform -Stage Build -OutputFolder $newFolder
    }
    finally { [System.IO.File]::WriteAllText($manifest, $manifestOriginal) }
    Write-Step 'both packages built and their certificates trusted'
}

if ($Stage -eq 'Build') { return }

# ---------------------------------------------------------------------------------------------------------------------------------------------
function Get-DataFolder {
    $package = Get-AppxPackage -Name $packageName
    if (-not $package) { throw 'the package is not installed' }
    return Join-Path $env:LOCALAPPDATA ("Packages\{0}\LocalState" -f $package.PackageFamilyName)
}

function Stop-App { Get-Process -Name 'TimeTug' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2 }

# Runs one self-test scenario through the execution alias and returns its output folder (result.json and state.json inside).
function Invoke-Scenario([string]$Name, [string]$Folder) {
    if (-not (Test-Path $alias)) { throw "execution alias not found: $alias" }
    if (Test-Path $Folder) { Remove-Item -Recurse -Force $Folder }
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $process = Start-Process -FilePath $alias -ArgumentList @('--selftest', $Name, '--out', "`"$Folder`"") -PassThru
    $resultFile = Join-Path $Folder 'result.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    while (-not (Test-Path $resultFile) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (-not $process.WaitForExit(30000)) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Stop-App
    if (-not (Test-Path $resultFile)) { throw "${Name}: no result.json (the app crashed, hung or never started)" }
    $result = Get-Content -Raw $resultFile | ConvertFrom-Json
    $failed = @($result.checks | Where-Object { $_ -and $_.ok -ne $true } | ForEach-Object { $_.name })
    if ($result.ok -ne $true -or $failed.Count -gt 0) { throw ("{0} failed: {1} {2}" -f $Name, $result.error, ($failed -join '; ')) }
    if (-not (Test-Path (Join-Path $Folder 'state.json'))) { throw "${Name}: no state.json" }
    Write-Step ("{0}: ok ({1} checks, package {2})" -f $Name, @($result.checks).Count, $result.packageFullName)
}

# Every file under the data folder except the app's own log folder: relative path, size, SHA-256.
function Get-FileRecord([string]$Folder) {
    $records = @{}
    if (-not (Test-Path $Folder)) { return $records }
    $root = (Resolve-Path $Folder).Path.TrimEnd('\')
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        $relative = $file.FullName.Substring($root.Length + 1)
        if ($relative -like 'logs\*') { continue }
        $records[$relative] = [pscustomobject]@{ Path = $relative; Length = $file.Length; Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
    return $records
}

# Counts that must survive: how many records the ledger, the dedup state and the sync state hold. Counts only; no value is read out.
function Get-DataCount([string]$Folder) {
    $counts = [ordered]@{}
    $ledger = Join-Path $Folder 'takeover-ledger.json'
    if (Test-Path $ledger) { $json = Get-Content -Raw -LiteralPath $ledger | ConvertFrom-Json; $counts.ledgerRecords = @($json.records.PSObject.Properties).Count }
    $dedup = Join-Path $Folder 'dedup-state.json'
    if (Test-Path $dedup) {
        $json = Get-Content -Raw -LiteralPath $dedup | ConvertFrom-Json
        $counts.lessons = @($json.lessons.lessons).Count
        $counts.verdicts = @($json.verdicts.entries.PSObject.Properties).Count
    }
    $sync = Join-Path $Folder 'sync-state.json'
    if (Test-Path $sync) { $json = Get-Content -Raw -LiteralPath $sync | ConvertFrom-Json; $counts.syncConnections = @($json.PSObject.Properties).Count }
    $accounts = Join-Path $Folder 'accounts.json'
    if (Test-Path $accounts) { $counts.accountRecords = @(Get-Content -Raw -LiteralPath $accounts | ConvertFrom-Json).Count }
    return $counts
}

function Read-State([string]$Folder) { return Get-Content -Raw -LiteralPath (Join-Path $Folder 'state.json') | ConvertFrom-Json }

function ConvertTo-Comparable($State) {
    $accounts = @($State.accounts | ForEach-Object { '{0}|{1}|{2}' -f $_.connectionId, $_.kindId, $_.displayName })
    $kinds = @($State.availableKinds)
    $settings = $State.settings | ConvertTo-Json -Compress
    $ledger = @($State.engineDiagnostics | Where-Object { $_.category -eq 'ledger' } | ForEach-Object { $_.message })
    return [ordered]@{ accounts = $accounts; availableKinds = $kinds; settings = $settings; ledgerDiagnostics = $ledger }
}

$problems = New-Object System.Collections.Generic.List[string]
function Assert-That([bool]$Condition, [string]$Text) {
    if ($Condition) { Write-Host ("  ok   {0}" -f $Text) } else { Write-Host ("  FAIL {0}" -f $Text); $problems.Add($Text) }
}

foreach ($folder in $oldFolder, $newFolder) {
    if (-not (Test-Path (Join-Path $folder 'layout'))) { throw "no package under $folder\layout; run -Stage Build (as SYSTEM) first" }
}
$results = Join-Path $OutputFolder 'results'
if (Test-Path $results) { Remove-Item -Recurse -Force $results }
New-Item -ItemType Directory -Force -Path $results | Out-Null

# ---- the old package, with synthetic data ----------------------------------------------------------------------------------------------------
Stop-App
Write-Step 'installing the old package (this removes any installed copy, and its data, first)'
& $installScript -Folder (Join-Path $oldFolder 'layout') -Certificate (Join-Path $oldFolder 'dev.cer') -Platform $Platform -Stage Install
$data = Get-DataFolder
New-Item -ItemType Directory -Force -Path $data | Out-Null
foreach ($name in $fixtureFiles) { Copy-Item -LiteralPath (Join-Path $fixtures $name) -Destination (Join-Path $data $name) -Force }
Write-Step ("data folder prepared with {0} synthetic file(s)" -f $fixtureFiles.Count)

Invoke-Scenario 'upgrade-seed' (Join-Path $results 'before-seed')
Invoke-Scenario 'upgrade-state' (Join-Path $results 'before')
$filesBefore = Get-FileRecord $data
$countsBefore = Get-DataCount $data
$oldPackage = Get-AppxPackage -Name $packageName
Write-Step ("old package: {0}; data files: {1}" -f $oldPackage.PackageFullName, (($filesBefore.Keys | Sort-Object) -join ', '))

# ---- the new package over the old one ---------------------------------------------------------------------------------------------------------
Stop-App
$newMsix = Get-ChildItem -LiteralPath (Join-Path $newFolder 'layout') -Recurse -Filter '*.msix' -File | Where-Object { $_.FullName -notmatch '[\\/]Dependencies[\\/]' } | Select-Object -First 1
$dependencies = @()
$dependencyRoot = Join-Path $newMsix.DirectoryName 'Dependencies'
if (Test-Path $dependencyRoot) {
    $archFolder = Get-ChildItem -LiteralPath $dependencyRoot -Directory | Where-Object { $_.Name -ieq $Platform } | Select-Object -First 1
    if ($archFolder) { $dependencies = @(Get-ChildItem -LiteralPath $archFolder.FullName -Filter '*.msix' -File | ForEach-Object { $_.FullName }) }
}
Write-Step ("installing {0} over the installed package" -f $newMsix.Name)
if ($dependencies.Count -gt 0) { Add-AppxPackage -Path $newMsix.FullName -DependencyPath $dependencies }
else { Add-AppxPackage -Path $newMsix.FullName }
$newPackage = Get-AppxPackage -Name $packageName
Assert-That ($null -ne $newPackage) 'the new package is installed'
Assert-That ($newPackage.InstallLocation -ne $oldPackage.InstallLocation -or $newPackage.PackageFullName -ne $oldPackage.PackageFullName) 'the install replaced the package content (a new install location)'
Assert-That ((Get-DataFolder) -eq $data) 'the data folder is the same folder'
$engineDll = @(Get-ChildItem -LiteralPath $newPackage.InstallLocation -Recurse -Filter 'TimeTugEngineC.dll' -File)
# The development package carries the native library of both architectures (the Store and Direct packages carry one): one file per runtime folder.
$engineDirs = @($engineDll | ForEach-Object { Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $_.FullName)) } | Sort-Object)
Assert-That (($engineDirs -join ',') -eq 'win-arm64,win-x64') ("the new package holds exactly one TimeTugEngineC.dll per architecture ({0} found: {1})" -f $engineDll.Count, ($engineDirs -join ', '))
$swiftFiles = @(Get-ChildItem -LiteralPath $newPackage.InstallLocation -Recurse -File | Where-Object { $_.Name -match '^(swift.*|Foundation.*|_FoundationICU|dispatch|BlocksRuntime)\.dll$' })
Assert-That ($swiftFiles.Count -eq 0) ("the new package holds no Swift runtime file ({0} found)" -f $swiftFiles.Count)

# The install alone must not have touched the data.
$filesInstalled = Get-FileRecord $data
Assert-That ((($filesBefore.Keys | Sort-Object) -join '|') -eq (($filesInstalled.Keys | Sort-Object) -join '|')) 'the install left the same set of data files'
$changedByInstall = @($filesBefore.Keys | Where-Object { $filesInstalled.ContainsKey($_) -and $filesInstalled[$_].Sha256 -ne $filesBefore[$_].Sha256 })
Assert-That ($changedByInstall.Count -eq 0) ("the install changed no data file ({0})" -f ($changedByInstall -join ', '))

# ---- the new engine reads the old data --------------------------------------------------------------------------------------------------------
Invoke-Scenario 'upgrade-state' (Join-Path $results 'after')
$filesAfter = Get-FileRecord $data
$countsAfter = Get-DataCount $data

$before = Read-State (Join-Path $results 'before')
$after = Read-State (Join-Path $results 'after')
$kindsBefore = @($before.accounts | ForEach-Object { $_.kindId })
$kindsAfter = @($after.accounts | ForEach-Object { $_.kindId })
Assert-That ($kindsBefore -contains 'microsoft-brokered' -and $kindsAfter -contains 'microsoft-brokered') 'the account kinds include microsoft-brokered, before and after'
Assert-That (@($after.accounts).Count -ge 1 -and @($after.accounts).Count -eq @($before.accounts).Count) ("the same number of accounts: {0} before, {1} after" -f @($before.accounts).Count, @($after.accounts).Count)
$cb = ConvertTo-Comparable $before
$ca = ConvertTo-Comparable $after
Assert-That (($cb.accounts -join ';') -eq ($ca.accounts -join ';')) 'accounts (connection id, kind, display name) are equal'
Assert-That (($cb.availableKinds -join ',') -eq ($ca.availableKinds -join ',')) ("the engine offers the same account kinds ({0})" -f ($ca.availableKinds -join ', '))
Assert-That ($cb.settings -eq $ca.settings) 'settings are equal'
Assert-That ($after.settings.leadTimeSeconds -eq 120 -and $after.settings.requireConferenceLink -eq $true -and $after.settings.skipAllDayEvents -eq $false) 'the three seeded settings (lead time 120 s, conference link required, all-day events not skipped) are what the new engine reports'
Assert-That (($cb.ledgerDiagnostics -join ';') -eq ($ca.ledgerDiagnostics -join ';') -and @($ca.ledgerDiagnostics).Count -gt 0) ("the engine's ledger line is equal ({0})" -f ($ca.ledgerDiagnostics -join '; '))
Assert-That (($countsBefore | ConvertTo-Json -Compress) -eq ($countsAfter | ConvertTo-Json -Compress)) ("data file counts are equal ({0})" -f ($countsAfter | ConvertTo-Json -Compress))
$deleted = @($filesBefore.Keys | Where-Object { -not $filesAfter.ContainsKey($_) })
Assert-That ($deleted.Count -eq 0) ("no data file was deleted ({0})" -f ($deleted -join ', '))
$emptied = @($filesBefore.Keys | Where-Object { $filesAfter.ContainsKey($_) -and $filesAfter[$_].Length -eq 0 -and $filesBefore[$_].Length -gt 0 })
Assert-That ($emptied.Count -eq 0) ("no data file was emptied ({0})" -f ($emptied -join ', '))
$rewritten = @($filesBefore.Keys | Where-Object { $filesAfter.ContainsKey($_) -and $filesAfter[$_].Sha256 -ne $filesBefore[$_].Sha256 })
Write-Step ("files whose bytes differ after the new engine ran (information, not a failure): {0}" -f $(if ($rewritten.Count) { $rewritten -join ', ' } else { 'none' }))
$statusBefore = ($before.accounts | ForEach-Object { $_.status }) -join ','
$statusAfter = ($after.accounts | ForEach-Object { $_.status }) -join ','
Write-Step ("account status before / after (information: no network answers are scripted): {0} / {1}" -f $statusBefore, $statusAfter)

$summary = [ordered]@{
    platform = $Platform
    oldPackage = $oldPackage.PackageFullName
    newPackage = $newPackage.PackageFullName
    dataFilesBefore = @($filesBefore.Keys | Sort-Object)
    dataFilesAfter = @($filesAfter.Keys | Sort-Object)
    countsBefore = $countsBefore
    countsAfter = $countsAfter
    rewrittenByNewEngine = $rewritten
    problems = @($problems)
    passed = ($problems.Count -eq 0)
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $results 'summary.json') -Encoding UTF8
if ($problems.Count -gt 0) {
    Write-Host ('upgrade check FAILED: ' + ($problems -join '; '))
    throw 'upgrade check failed'
}
Write-Host 'upgrade check passed'
