# Tests for the pure logic in scripts/vm/vm-install.ps1 (finding the package, its dependencies and the certificate).
# Plain PowerShell, no Pester: runs on Windows PowerShell 5.1, PowerShell 7 and Linux/macOS pwsh.
#   pwsh -NoProfile -File scripts/vm/tests/vm-install.tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'vm-install.ps1')

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
function New-Tree([string[]]$Files) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('vm-install-test-' + [guid]::NewGuid().ToString('N'))
    foreach ($file in $Files) {
        $path = Join-Path $root $file
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
        Set-Content -Path $path -Value 'x'
    }
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    return $root
}

$artifact = @(
    'TimeTug.App_0.1.0.0_x64_CI.msix', 'Dependencies/x64/Microsoft.WindowsAppRuntime.2.5.msix', 'Dependencies/x64/Microsoft.VCLibs.msix',
    'Dependencies/arm64/Microsoft.WindowsAppRuntime.2.5.msix', 'dev.cer', 'vm-install.ps1')

Check 'platform comes from the package name' {
    Assert-Equal (Get-PackagePlatform 'TimeTug.App_0.1.0.0_ARM64_CI.msix') 'arm64'
    Assert-Equal (Get-PackagePlatform 'TimeTug.App_0.1.0.0_x64_CI.msix') 'x64'
    Assert-Equal (Get-PackagePlatform 'TimeTug.msix') $null
}

Check 'the processes that block an install (0x80073D02) are recognised' {
    foreach ($name in 'Widgets', 'WidgetService', 'WidgetBoard', 'StartMenuExperienceHost', 'Microsoft.StartExperiencesApp') {
        Assert-Equal (Test-InstallBlocker ([pscustomobject]@{ Name = $name; Path = $null })) $true
    }
    Assert-Equal (Test-InstallBlocker ([pscustomobject]@{ Name = 'Anything'; Path = 'C:\Windows\SystemApps\MicrosoftWindows.Client.WebExperience_x\Widgets.exe' })) $true
    Assert-Equal (Test-InstallBlocker ([pscustomobject]@{ Name = 'explorer'; Path = 'C:\Windows\explorer.exe' })) $false
    Assert-Equal (Test-InstallBlocker ([pscustomobject]@{ Name = 'TimeTug'; Path = $null })) $false
}

Check 'the artifact layout is found, with only its own architecture dependencies' {
    $root = New-Tree $artifact
    $files = Find-PackageFiles -Folder $root
    Assert-Equal (Split-Path -Leaf $files.Package) 'TimeTug.App_0.1.0.0_x64_CI.msix'
    Assert-Equal $files.Platform 'x64'
    Assert-Equal $files.Dependencies.Count 2
    Assert-Equal (Split-Path -Leaf $files.Certificate) 'dev.cer'
    Remove-Item -Recurse -Force $root
}

Check 'the build-msix layout is found (certificate one folder up, MSBuild .cer beside the package)' {
    $root = New-Tree @('dev.cer', 'layout/TimeTug.App_0.1.0.0_ARM64_CI_Test/TimeTug.App_0.1.0.0_ARM64_CI.msix',
        'layout/TimeTug.App_0.1.0.0_ARM64_CI_Test/TimeTug.App_0.1.0.0_ARM64_CI.cer',
        'layout/TimeTug.App_0.1.0.0_ARM64_CI_Test/Dependencies/arm64/Microsoft.WindowsAppRuntime.2.5.msix',
        'layout/TimeTug.App_0.1.0.0_ARM64_CI_Test/Dependencies/x64/Microsoft.WindowsAppRuntime.2.5.msix')
    $files = Find-PackageFiles -Folder (Join-Path $root 'layout')
    Assert-Equal $files.Platform 'arm64'
    Assert-Equal $files.Dependencies.Count 1
    Assert-Equal (Split-Path -Leaf $files.Certificate) 'dev.cer'
    $explicit = Join-Path $root 'layout/TimeTug.App_0.1.0.0_ARM64_CI_Test/TimeTug.App_0.1.0.0_ARM64_CI.cer'
    $files = Find-PackageFiles -Folder (Join-Path $root 'layout') -Certificate $explicit
    Assert-Equal (Split-Path -Leaf $files.Certificate) 'TimeTug.App_0.1.0.0_ARM64_CI.cer'
    Remove-Item -Recurse -Force $root
}

Check 'two packages need a platform to choose between them' {
    $root = New-Tree @('a/TimeTug.App_1_x64_CI.msix', 'b/TimeTug.App_1_ARM64_CI.msix', 'dev.cer')
    Assert-Throws { Find-PackageFiles -Folder $root } 'more than one'
    $files = Find-PackageFiles -Folder $root -Platform ARM64
    Assert-Equal $files.Platform 'arm64'
    Remove-Item -Recurse -Force $root
}

Check 'clear errors for a missing folder, package or certificate' {
    Assert-Throws { Find-PackageFiles -Folder (Join-Path ([IO.Path]::GetTempPath()) 'does-not-exist-vm-install') } 'folder not found'
    $root = New-Tree @('readme.txt')
    Assert-Throws { Find-PackageFiles -Folder $root } 'no TimeTug .msix'
    Remove-Item -Recurse -Force $root
    $root = New-Tree @('TimeTug.App_1_x64_CI.msix')
    Assert-Throws { Find-PackageFiles -Folder $root } 'no public certificate'
    Remove-Item -Recurse -Force $root
}

Check 'a package whose name has no architecture needs -Platform' {
    $root = New-Tree @('TimeTug.msix', 'dev.cer')
    Assert-Throws { Find-PackageFiles -Folder $root } 'cannot tell the architecture'
    $files = Find-PackageFiles -Folder $root -Platform x64
    Assert-Equal $files.Platform 'x64'
    Remove-Item -Recurse -Force $root
}

Check 'a platform that contradicts the package name is refused' {
    $root = New-Tree @('TimeTug.App_1_x64_CI.msix', 'dev.cer')
    Assert-Throws { Find-PackageFiles -Folder $root -Platform ARM64 } 'is a x64 package'
    Remove-Item -Recurse -Force $root
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) test(s) failed"; exit 1 }
Write-Host 'all vm-install tests passed'
