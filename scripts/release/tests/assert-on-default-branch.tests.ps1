# Tests for the pure logic in scripts/release/assert-on-default-branch.ps1. Plain PowerShell.
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'assert-on-default-branch.ps1')

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }

Check 'the tip and its ancestors are on the default branch' {
    Assert-Equal (Test-OnDefaultBranch 'identical') $true
    Assert-Equal (Test-OnDefaultBranch 'behind') $true
}
Check 'a ref with commits the default branch lacks is not' {
    foreach ($status in 'ahead', 'diverged', '', 'unknown') { Assert-Equal (Test-OnDefaultBranch $status) $false }
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) failed"; exit 1 }
Write-Host 'all passed'
