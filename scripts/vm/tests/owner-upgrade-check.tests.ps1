# Tests for the pure logic of scripts/vm/owner-upgrade-check.ps1 (architecture and version checks, the data snapshot and its comparison, name masking,
# the log scan) and for the promises it makes (it never removes a package, never opens dev-oauth.json).
# Plain PowerShell, no Pester: runs on Windows PowerShell 5.1, PowerShell 7 and Linux/macOS pwsh.
#   pwsh -NoProfile -File scripts/vm/tests/owner-upgrade-check.tests.ps1
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'owner-upgrade-check.ps1'
. $scriptPath

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }
function Assert-True($Condition, [string]$Text) { if (-not $Condition) { throw $Text } }
function Count-Of($Items) { if ($null -eq $Items) { return 0 } return @($Items).Count }
function New-DataTree([hashtable]$Files) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('owner-upgrade-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    foreach ($name in $Files.Keys) {
        $path = Join-Path $root $name
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
        [IO.File]::WriteAllText($path, [string]$Files[$name])
    }
    return $root
}

Check 'architecture names are normalised' {
    Assert-Equal (ConvertTo-ArchName 'Arm64') 'arm64'
    Assert-Equal (ConvertTo-ArchName 'ARM64') 'arm64'
    Assert-Equal (ConvertTo-ArchName 'X64') 'x64'
    Assert-Equal (ConvertTo-ArchName 'AMD64') 'x64'
    Assert-Equal (ConvertTo-ArchName 'Neutral') $null
}

Check 'matching architectures give no problem' {
    Assert-Equal (Count-Of (Get-ArchitectureProblem 'Arm64' 'arm64' 'Arm64' 'arm64')) 0
    Assert-Equal (Count-Of (Get-ArchitectureProblem 'X64' 'x64' 'X64' $null)) 0
}

Check 'an x64 trial build on an ARM64 PC is refused' {
    $problems = @(Get-ArchitectureProblem 'Arm64' 'x64' 'Arm64' $null)
    Assert-Equal $problems.Count 1
    Assert-True ($problems[0] -match 'trial build folder is x64 but this PC is arm64') $problems[0]
}

Check 'an installed package of another architecture and an old build folder of another architecture are both named' {
    $problems = @(Get-ArchitectureProblem 'X64' 'x64' 'Arm64' 'arm64')
    Assert-Equal $problems.Count 2
    Assert-True (($problems -join ';') -match 'installed package is arm64') 'installed package not named'
    Assert-True (($problems -join ';') -match 'Swift-engine build folder is arm64') 'old build not named'
}

Check 'an unknown architecture is a problem, not a pass' {
    Assert-Equal (Count-Of (Get-ArchitectureProblem 'Arm64' 'sparc' $null $null)) 1
    Assert-Equal (Count-Of (Get-ArchitectureProblem 'sparc' $null $null $null)) 1
}

Check 'only a higher package version may replace the installed one' {
    Assert-True (Test-VersionHigher '0.1.1.0' '0.1.0.0') 'higher'
    Assert-True (-not (Test-VersionHigher '0.1.0.0' '0.1.0.0')) 'same'
    Assert-True (-not (Test-VersionHigher '0.1.0.0' '0.1.1.0')) 'lower'
    Assert-True (Test-VersionHigher '0.10.0.0' '0.9.0.0') 'numeric, not text, comparison'
}

Check 'a path that may be an e-mail address is masked, other paths are not' {
    Assert-Equal (ConvertTo-SafePath 'accounts.json') 'accounts.json'
    Assert-Equal (ConvertTo-SafePath 'cache\sync-state.json') 'cache\sync-state.json'
    $masked = ConvertTo-SafePath 'cache\dave@contoso.com.json'
    Assert-True ($masked -notmatch 'dave|contoso|@') "still readable: $masked"
    Assert-True ($masked -match '^<masked-[0-9a-f]{8}>\.json$') "unexpected shape: $masked"
    Assert-Equal (ConvertTo-SafePath 'cache\dave@contoso.com.json') $masked
    Assert-True ((ConvertTo-SafePath 'a@x.json') -ne (ConvertTo-SafePath 'b@x.json')) 'different names must stay different'
}

Check 'a snapshot lists size and hash, leaves out the logs folder and holds no file content' {
    $root = New-DataTree @{ 'accounts.json' = 'SECRET-CONTENT-1'; 'dedup-state.json' = ''; 'sub\a@b.json' = 'SECRET-CONTENT-2'; 'logs\timetug.log' = 'log line' }
    try {
        $snapshot = Get-DataSnapshot $root
        Assert-Equal $snapshot.Count 3
        Assert-Equal $snapshot['accounts.json'].Length 16
        Assert-Equal $snapshot['accounts.json'].Sha256.Length 64
        Assert-Equal $snapshot['dedup-state.json'].Length 0
        Assert-True (-not ($snapshot.Keys -match 'logs')) 'the logs folder must not be listed'
        Assert-True (-not ($snapshot.Keys -match '@')) 'an @ name must be masked'
        $json = (@($snapshot.Values) | ConvertTo-Json -Depth 3)
        Assert-True ($json -notmatch 'SECRET') 'file content leaked into the snapshot'
        Assert-True ((Format-SnapshotTable $snapshot | Out-String) -notmatch 'SECRET') 'file content leaked into the table'
    }
    finally { Remove-Item -Recurse -Force $root }
}

Check 'a folder that does not exist gives an empty snapshot' {
    Assert-Equal (Get-DataSnapshot (Join-Path ([IO.Path]::GetTempPath()) ('nope-' + [guid]::NewGuid().ToString('N')))).Count 0
}

Check 'identical data compares as identical' {
    $root = New-DataTree @{ 'accounts.json' = 'one'; 'sync-state.json' = 'two' }
    try {
        $a = Get-DataSnapshot $root
        $b = Get-DataSnapshot $root
        $result = Compare-DataSnapshot $a $b
        Assert-True $result.Identical 'identical'
        Assert-Equal $result.Deleted.Count 0
    }
    finally { Remove-Item -Recurse -Force $root }
}

Check 'deleted, emptied, changed and added files are told apart' {
    $root = New-DataTree @{ 'accounts.json' = 'one'; 'sync-state.json' = 'two'; 'dedup-state.json' = 'three'; 'takeover-ledger.json' = 'four' }
    try {
        $before = Get-DataSnapshot $root
        Remove-Item (Join-Path $root 'accounts.json')
        [IO.File]::WriteAllText((Join-Path $root 'sync-state.json'), '')
        [IO.File]::WriteAllText((Join-Path $root 'dedup-state.json'), 'three-changed')
        [IO.File]::WriteAllText((Join-Path $root 'settings.json'), 'new')
        $after = Get-DataSnapshot $root
        $result = Compare-DataSnapshot $before $after
        Assert-Equal ($result.Deleted -join ',') 'accounts.json'
        Assert-Equal ($result.Emptied -join ',') 'sync-state.json'
        Assert-Equal ($result.Changed -join ',') 'dedup-state.json,sync-state.json'
        Assert-Equal ($result.Added -join ',') 'settings.json'
        Assert-True (-not $result.Identical) 'must not be identical'
    }
    finally { Remove-Item -Recurse -Force $root }
}

Check 'a snapshot survives being saved and read back' {
    $root = New-DataTree @{ 'accounts.json' = 'one'; 'sync-state.json' = 'two'; 'dedup-state.json' = 'three' }
    $file = Join-Path ([IO.Path]::GetTempPath()) ('snap-' + [guid]::NewGuid().ToString('N') + '.json')
    try {
        $snapshot = Get-DataSnapshot $root
        Save-Snapshot $snapshot $file
        $back = Read-Snapshot $file
        Assert-Equal $back.Count 3
        Assert-Equal $back['accounts.json'].Length 3
        $same = Compare-DataSnapshot $snapshot $back
        Assert-True $same.Identical 'not identical after a round trip'
        Assert-Equal (Read-Snapshot (Join-Path ([IO.Path]::GetTempPath()) 'does-not-exist.json')) $null
    }
    finally { Remove-Item -Recurse -Force $root; Remove-Item -Force $file -ErrorAction SilentlyContinue }
}

Check 'the log scan prints only the could-not-start lines of the new part of the log' {
    $log = Join-Path ([IO.Path]::GetTempPath()) ('log-' + [guid]::NewGuid().ToString('N') + '.log')
    try {
        $old = "2026-11-14 start ok`nTimeTug could not start. OldReason: x`n"
        [IO.File]::WriteAllText($log, $old)
        $length = (Get-Item $log).Length
        [IO.File]::AppendAllText($log, "meeting 'Private title' refreshed`nTimeTug could not start. System.IO.IOException: file in use`nlater line`n")
        $lines = @(Get-StartFailureLine $log $length)
        Assert-Equal $lines.Count 1
        Assert-True ($lines[0] -match 'IOException') 'the reason must be printed'
        Assert-True (($lines -join '') -notmatch 'Private title') 'other lines must not be printed'
        Assert-Equal (Count-Of (Get-StartFailureLine $log 0)) 2
        Assert-Equal (Count-Of (Get-StartFailureLine (Join-Path ([IO.Path]::GetTempPath()) 'missing.log') 0)) 0
    }
    finally { Remove-Item -Force $log -ErrorAction SilentlyContinue }
}

# ---- the promises, checked in the script text --------------------------------------------------------------------------------------------
$text = Get-Content -Raw -LiteralPath $scriptPath
$code = [regex]::Replace($text, '(?s)<#.*?#>', '')   # the comment-based help may name what the script does not do
$codeLines = @(($code -split "`n") | Where-Object { $_ -notmatch '^\s*#' })

Check 'the script never removes a package or calls the installing stages of vm-install.ps1' {
    foreach ($line in $codeLines) {
        Assert-True ($line -notmatch 'Remove-AppxPackage') "removes a package: $line"
        Assert-True ($line -notmatch 'Invoke-Install\b') "calls the installer that removes the package: $line"
        Assert-True ($line -notmatch '-Stage\s+(Install|All|Uninstall)') "uses a stage that removes the package: $line"
        Assert-True ($line -notmatch 'Remove-Item') "deletes something: $line"
    }
}

Check 'dev-oauth.json is only ever tested for presence, never opened' {
    $mentions = @($codeLines | Where-Object { $_ -match 'dev-oauth' })
    Assert-True ($mentions.Count -gt 0) 'the script should check the file is present'
    foreach ($line in $mentions) {
        Assert-True ($line -match 'Test-Path|Join-Path' -or $line -match "Write-Step '") "dev-oauth.json is used by more than a presence test: $line"
        Assert-True ($line -notmatch 'Get-Content|ReadAll|Copy-Item|Import-|Invoke-') "dev-oauth.json is read or copied: $line"
    }
}

Check 'nothing leaves the PC and no account file is copied' {
    foreach ($line in $codeLines) {
        Assert-True ($line -notmatch 'Invoke-WebRequest|Invoke-RestMethod|\bcurl\b|\bscp\b|Send-MailMessage|Copy-Item|Compress-Archive') "moves data: $line"
    }
}

Check 'the install step is an Add-AppxPackage over the existing package' {
    Assert-True ($text -match 'Add-AppxPackage -Path \$files\.Package -DependencyPath \$files\.Dependencies') 'Add-AppxPackage call not found'
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) check(s) failed"; exit 1 }
Write-Host 'all checks passed'
