# Tests for scripts/release/resolve-release.ps1. Plain PowerShell: Windows PowerShell 5.1, PowerShell 7 and pwsh on Linux/macOS.
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'resolve-release.ps1')

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }
function Assert-Throws([scriptblock]$Body, [string]$Pattern) {
    $message = $null
    try { & $Body } catch { $message = $_.Exception.Message }
    if ($null -eq $message) { throw 'expected an error but there was none' }
    if ($message -notmatch $Pattern) { throw "error '$message' does not match '$Pattern'" }
}
function Release([string]$Tag, [bool]$Pre = $false) { Resolve-ReleaseRequest 'release' $Tag $Pre '' '' 'main' $false }
function Dispatch([string]$Version, [string]$Ref = '', [bool]$TagExists = $false) { Resolve-ReleaseRequest 'workflow_dispatch' '' $false $Version $Ref 'main' $TagExists }

Check 'a stable release builds the stable package from its tag' {
    $r = Release 'v1.2.3'
    Assert-Equal $r.Go $true; Assert-Equal $r.Version '1.2.3'; Assert-Equal $r.Ref 'v1.2.3'; Assert-Equal $r.Flight $false
}
Check 'a suffixed tag builds the flight, never the stable package' {
    $r = Release 'v1.2.3-beta.1' $true
    Assert-Equal $r.Go $true; Assert-Equal $r.Version '1.2.3-beta.1'; Assert-Equal $r.Flight $true
    Assert-Equal (Release 'v1.2.3-beta.1' $false).Flight $true
}
Check 'a stable-form tag on a pre-release is skipped' {
    $r = Release 'v1.2.3' $true
    Assert-Equal $r.Go $false
}
Check 'tags that are not versions are skipped' {
    foreach ($tag in 'latest', 'v1.2', 'v1.2.3.4', 'vnext', 'release-1', '') { Assert-Equal (Release $tag).Go $false }
}
Check 'a manual run needs an exact version' {
    foreach ($bad in '', '1.2', 'v1.2.3', '1.2.3.4', 'x') { Assert-Throws { Dispatch $bad '' $true } 'version' }
}
Check 'a manual run without a ref needs the tag to exist' {
    Assert-Throws { Dispatch '1.2.3' '' $false } 'does not exist'
    $r = Dispatch '1.2.3' '' $true
    Assert-Equal $r.Ref 'v1.2.3'
}
Check 'a manual run takes the default branch or a version tag whose version matches' {
    Assert-Equal (Dispatch '1.2.3' 'main').Ref 'main'
    Assert-Equal (Dispatch '1.2.3' 'v1.2.3').Ref 'v1.2.3'
    Assert-Equal (Dispatch '1.2.3-beta.1' 'v1.2.3-beta.1').Flight $true
    Assert-Throws { Dispatch '1.2.3' 'v1.2.4' } 'does not match'
    foreach ($ref in 'develop', 'refs/heads/main', 'claude/store', 'v1', 'abc123', 'v1.2.3 ') { Assert-Throws { Dispatch '1.2.3' $ref } 'ref must be' }
}
Check 'a manual flight of a plain version is not possible and a stable run of a suffix is not either' {
    Assert-Equal (Dispatch '1.2.3-rc.1' 'main').Flight $true
    Assert-Equal (Dispatch '1.2.3' 'main').Flight $false
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) failed"; exit 1 }
Write-Host 'all passed'
