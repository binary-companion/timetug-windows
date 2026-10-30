# Tests for the pure logic in scripts/release/bundle-msixupload.ps1. Plain PowerShell: Windows PowerShell 5.1, PowerShell 7 and pwsh on Linux/macOS.
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'bundle-msixupload.ps1')

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

Check 'package names are read, anything else is not a package' {
    $p = Read-PackageName 'TimeTug_1.2.3.0_x64.msix'
    Assert-Equal $p.Version '1.2.3.0'; Assert-Equal $p.Architecture 'x64'; Assert-Equal $p.Flight $false
    Assert-Equal (Read-PackageName 'TimeTug_1.2.3.0_arm64-flight.msix').Flight $true
    foreach ($bad in 'TimeTug_1.2.3.1_x64.msix', 'TimeTug_1.2.3.0_x86.msix', 'TimeTug_1.2.3.0_x64.msixupload', 'Other_1.2.3.0_x64.msix', 'TimeTug.App_1.2.3.0_x64.msix') { Assert-Equal ($null -eq (Read-PackageName $bad)) $true }
}
Check 'bundle names carry the version, both architectures and -flight' {
    Assert-Equal (Get-BundleBaseName '1.2.3.0' $false) 'TimeTug_1.2.3.0_x64_arm64'
    Assert-Equal (Get-BundleBaseName '1.2.3.0' $true) 'TimeTug_1.2.3.0_x64_arm64-flight'
}
Check 'one package per architecture, one version, one kind' {
    $ok = Select-BundleInputs @('a/TimeTug_1.2.3.0_x64.msix', 'b/TimeTug_1.2.3.0_arm64.msix', 'b/Dependencies/x.msix') '1.2.3.0'
    Assert-Equal $ok.Count 2
    Assert-Throws { Select-BundleInputs @('TimeTug_1.2.3.0_x64.msix') '' } 'exactly one arm64'
    Assert-Throws { Select-BundleInputs @('TimeTug_1.2.3.0_x64.msix', 'TimeTug_1.2.3.0_arm64.msix', 'c/TimeTug_1.2.3.0_arm64.msix') '' } 'exactly one arm64'
    Assert-Throws { Select-BundleInputs @('TimeTug_1.2.3.0_x64.msix', 'TimeTug_1.2.4.0_arm64.msix') '' } 'different versions'
    Assert-Throws { Select-BundleInputs @('TimeTug_1.2.3.0_x64.msix', 'TimeTug_1.2.3.0_arm64-flight.msix') '' } 'flight'
    Assert-Throws { Select-BundleInputs @('TimeTug_1.2.3.0_x64.msix', 'TimeTug_1.2.3.0_arm64.msix') '1.2.4.0' } 'not 1.2.4.0'
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) failed"; exit 1 }
Write-Host 'all passed'
