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
    [string]$Folder = 'C:\Users\Public\timetug-vm\cross-flavour',
    # Cleanup only: leave the Direct package installed (the id file and the trusted certificate still go).
    [switch]$KeepPackage
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
        $problems = @()
        if (-not $KeepPackage) {
            $package = Get-AppxPackage -Name $name
            if ($package) {
                Get-Process -Name TimeTug -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($package.InstallLocation, [System.StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force -ErrorAction SilentlyContinue
                try { $package | Remove-AppxPackage } catch { $problems += "the Direct package could not be removed: $($_.Exception.Message)" }
                if (Get-AppxPackage -Name $name) { $problems += 'the Direct package is still installed' }
            }
        }
        Remove-Item -LiteralPath $idFile -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $idFile) { $problems += 'the id file is still there' }
        # Only the certificate this test made (its thumbprint, read from the public file it left), never another with the same subject.
        $cer = Join-Path $Folder 'local.cer'
        if (Test-Path -LiteralPath $cer) {
            $thumbprint = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $cer).Thumbprint
            $trusted = @(Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $thumbprint })
            if ($trusted.Count -gt 0) {
                try { $trusted | Remove-Item } catch { Write-Host 'Not elevated: the throwaway certificate is removed by the SYSTEM pass.' }
            }
        }
        if ($problems.Count -gt 0) { throw ($problems -join '; ') }
        Write-Host 'Cleaned up.'
    }
}
