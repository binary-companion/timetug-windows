<#
.SYNOPSIS
  Runs inside the VM: builds, trusts, installs and removes a locally signed Direct package for the cross-flavour-instance self-test.
.DESCRIPTION
  Stage Build   (as SYSTEM): scripts\release\build-release-msix.ps1 -Flavour Direct -LocalSign, then trusts the throwaway certificate machine-wide.
  Stage Install (as the user): installs the package and writes its app user model id to %LOCALAPPDATA%\TimeTug\selftest-other-aumid.txt, which the
                scenario reads (self-test builds only).
  Stage Cleanup (user, then SYSTEM for the certificate): removes the package, the id file and the trusted certificate.
  The Direct package has its own identity (BinaryCompanion.TimeTug), so it runs beside the development package: that is the point.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Build', 'Install', 'Cleanup')][string]$Stage,
    [string]$Folder = 'C:\Users\Public\timetug-vm\cross-flavour'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$name = 'BinaryCompanion.TimeTug'
$idFile = Join-Path $env:LOCALAPPDATA 'TimeTug\selftest-other-aumid.txt'

switch ($Stage) {
    'Build' {
        & (Join-Path $repo 'scripts\release\build-release-msix.ps1') -Flavour Direct -Platform ARM64 -Version 0.0.1 -LocalSign -OutputFolder $Folder
        Import-Certificate -FilePath (Join-Path $Folder 'local.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        Write-Host 'The throwaway certificate is trusted on this machine (removed again by -Stage Cleanup).'
    }
    'Install' {
        Get-AppxPackage -Name $name | Remove-AppxPackage
        $msix = Get-ChildItem -LiteralPath $Folder -Filter 'TimeTug_*_arm64.msix' | Select-Object -First 1
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            try { Add-AppxPackage -Path $msix.FullName; break }
            catch {
                if ($attempt -eq 1 -and $_.Exception.Message -match '0x80073D02') {
                    Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*WebExperience*' -or $_.Path -like '*StartExperiencesApp*' -or $_.Name -in 'StartMenuExperienceHost', 'Widgets', 'WidgetService' } | Stop-Process -Force -ErrorAction SilentlyContinue
                    Start-Sleep -Seconds 3
                    continue
                }
                throw
            }
        }
        $package = Get-AppxPackage -Name $name
        if (-not $package) { throw 'the Direct package did not install' }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $idFile) | Out-Null
        Set-Content -LiteralPath $idFile -Value "$($package.PackageFamilyName)!App"
        Write-Host "Installed $($package.PackageFullName)"
    }
    'Cleanup' {
        Get-Process -Name TimeTug -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*$name`_*" } | Stop-Process -Force -ErrorAction SilentlyContinue
        Get-AppxPackage -Name $name | Remove-AppxPackage -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $idFile -Force -ErrorAction SilentlyContinue
        try { Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq 'CN=Binary Companion' } | Remove-Item } catch { Write-Host 'Not elevated: the throwaway certificate was left in place.' }
        Write-Host 'Cleaned up.'
    }
}
