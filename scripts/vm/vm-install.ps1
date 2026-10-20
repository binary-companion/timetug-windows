<#
.SYNOPSIS
  Installs the TimeTug development MSIX and trusts its throwaway certificate.
.DESCRIPTION
  Works with the CI artifact (msix-x64 or msix-arm64: the .msix, Dependencies\<arch>\, dev.cer and this script side by
  side) and with a local build (scripts\ci\build-msix.ps1 calls this script). Windows PowerShell 5.1 and PowerShell 7.

  Two things are needed and they need different rights:
    Trust    puts the public certificate in LocalMachine\TrustedPeople. Needs an elevated session (or SYSTEM).
    Install  registers the package for the CURRENT user (Add-AppxPackage). Run it as the person who will use the app.
  On your own PC run this script once from an elevated PowerShell: Stage All does both. On the Parallels VM the helper
  scripts/vm/vm-selftest.sh runs Trust as SYSTEM and Install as the logged-in user.

  The certificate is a throwaway made by the CI job (valid two days); its private key was deleted there and is not in
  the artifact. Trusting it only lets Windows accept packages signed by it. Remove it again with -Stage Uninstall.
.PARAMETER Folder
  The artifact folder (or build\msix\layout). Default: the folder of this script.
.PARAMETER Package
  The .msix to install. Default: the one package under Folder (not counting Dependencies).
.PARAMETER Certificate
  The public .cer. Default: dev.cer in Folder or its parent, else the .cer next to the package.
.PARAMETER Platform
  x64, ARM64 or x86: which package and which Dependencies folder. Default: taken from the package name.
.PARAMETER Stage
  Trust, Install, All (default) or Uninstall (removes the package and the trusted throwaway certificates).
#>
[CmdletBinding()]
param(
    [string]$Folder = $PSScriptRoot,
    [string]$Package,
    [string]$Certificate,
    [string]$Platform,
    [ValidateSet('Trust', 'Install', 'All', 'Uninstall')]
    [string]$Stage = 'All'
)

$ErrorActionPreference = 'Stop'
$script:PackageName = 'BinaryCompanion.TimeTug.Dev'
$script:Publisher = 'CN=TimeTug Development'

function Get-PackagePlatform([string]$FileName) {
    if ($FileName -match '_(x64|arm64|x86)_') { return $Matches[1].ToLowerInvariant() }
    return $null
}

# Finds what to install under $Folder. Returns Package, Platform (lower case), Dependencies (full paths) and Certificate.
function Find-PackageFiles([string]$Folder, [string]$Package, [string]$Certificate, [string]$Platform) {
    if (-not (Test-Path -LiteralPath $Folder -PathType Container)) { throw "folder not found: $Folder" }
    $Folder = (Resolve-Path -LiteralPath $Folder).Path

    if ($Package) {
        if (-not (Test-Path -LiteralPath $Package -PathType Leaf)) { throw "package not found: $Package" }
        $msix = Get-Item -LiteralPath $Package
    }
    else {
        $found = @(Get-ChildItem -LiteralPath $Folder -Recurse -Filter '*.msix' -File |
            Where-Object { $_.FullName -notmatch '[\\/]Dependencies[\\/]' })
        if ($Platform -and $found.Count -gt 1) {
            $wanted = $Platform.ToLowerInvariant()
            $found = @($found | Where-Object { (Get-PackagePlatform $_.Name) -eq $wanted })
        }
        if ($found.Count -eq 0) { throw "no TimeTug .msix under $Folder (is this the artifact folder?)" }
        if ($found.Count -gt 1) { throw "more than one .msix under ${Folder}: $($found.Name -join ', '); pass -Package or -Platform" }
        $msix = $found[0]
    }

    $arch = Get-PackagePlatform $msix.Name
    if ($Platform) {
        if ($arch -and $arch -ne $Platform.ToLowerInvariant()) { throw "$($msix.Name) is a $arch package but -Platform $Platform was given" }
        $arch = $Platform.ToLowerInvariant()
    }
    if (-not $arch) { throw "cannot tell the architecture of $($msix.Name); pass -Platform" }

    $dependencies = @()
    $dependencyRoot = Join-Path $msix.DirectoryName 'Dependencies'
    if (Test-Path -LiteralPath $dependencyRoot) {
        $archFolder = Get-ChildItem -LiteralPath $dependencyRoot -Directory | Where-Object { $_.Name -ieq $arch } | Select-Object -First 1
        if ($archFolder) { $dependencies = @(Get-ChildItem -LiteralPath $archFolder.FullName -Filter '*.msix' -File | ForEach-Object { $_.FullName }) }
    }

    if ($Certificate) {
        if (-not (Test-Path -LiteralPath $Certificate -PathType Leaf)) { throw "certificate not found: $Certificate" }
        $cer = (Resolve-Path -LiteralPath $Certificate).Path
    }
    else {
        $candidates = @(
            (Join-Path $Folder 'dev.cer'),
            (Join-Path (Split-Path -Parent $Folder) 'dev.cer')
        ) + @(Get-ChildItem -LiteralPath $msix.DirectoryName -Filter '*.cer' -File | ForEach-Object { $_.FullName })
        $cer = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if (-not $cer) { throw "no public certificate (dev.cer) next to the package; pass -Certificate" }
    }

    return [pscustomobject]@{ Package = $msix.FullName; Platform = $arch; Dependencies = $dependencies; Certificate = $cer }
}

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Trust($Files) {
    if (-not (Test-Elevated)) { throw 'trusting the certificate needs an elevated PowerShell (Run as administrator), or SYSTEM' }
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $Files.Certificate
    if ($cert.HasPrivateKey) { throw "refusing to import $($Files.Certificate): it holds a private key; only the public .cer belongs here" }
    if ($cert.Subject -ne $script:Publisher) { throw "unexpected certificate subject '$($cert.Subject)', expected '$($script:Publisher)'" }
    Import-Certificate -FilePath $Files.Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Write-Host "Trusted $($cert.Subject) $($cert.Thumbprint) (valid until $($cert.NotAfter.ToString('yyyy-MM-dd HH:mm')))"

    # A package signed by another certificate would fail at install with a vague error; say so now when we can tell.
    $signature = $null
    try { $signature = Get-AuthenticodeSignature -FilePath $Files.Package } catch { $signature = $null }
    if ($signature -and $signature.SignerCertificate -and $signature.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
        throw "the package is signed by $($signature.SignerCertificate.Thumbprint), not by the certificate $($cert.Thumbprint); use the .cer from the same build"
    }
}

function Invoke-Install($Files) {
    $aliasPath = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\timetug-dev.exe'
    Get-AppxPackage -Name $script:PackageName | Remove-AppxPackage
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            if ($Files.Dependencies.Count -gt 0) { Add-AppxPackage -Path $Files.Package -DependencyPath $Files.Dependencies }
            else { Add-AppxPackage -Path $Files.Package }
            break
        }
        catch {
            $message = $_.Exception.Message
            # 0x80073D02: another package using the Windows App Runtime is running (on the windows-11-arm runner the Widgets pack).
            if ($attempt -eq 1 -and $message -match '0x80073D02') {
                Write-Host "Install blocked by running apps; stopping them and retrying: $message"
                Get-Process -ErrorAction SilentlyContinue |
                    Where-Object { $_.Path -like '*WebExperience*' -or $_.Name -in 'Widgets', 'WidgetService', 'WidgetBoard' } |
                    Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 3
                continue
            }
            if ($message -match '0x800B010A|0x800B0109|0x800B0100|certificate') {
                throw "Windows does not trust the package's certificate. Run this script (or -Stage Trust) from an elevated PowerShell first. $message"
            }
            throw
        }
    }
    $installed = Get-AppxPackage -Name $script:PackageName
    if (-not $installed) { throw 'the package did not install' }
    Write-Host "Installed $($installed.PackageFullName)"
    Write-Host "Start it from the Start menu (TimeTug Dev), or: $aliasPath --selftest hello --out <folder>"
}

function Invoke-Uninstall {
    Get-AppxPackage -Name $script:PackageName | Remove-AppxPackage
    Write-Host "Removed package $($script:PackageName) for this user"
    if (Test-Elevated) {
        $stale = @(Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq $script:Publisher })
        $stale | Remove-Item
        Write-Host "Removed $($stale.Count) trusted throwaway certificate(s)"
    }
    else { Write-Host 'Not elevated: the trusted certificates were left in place (run elevated to remove them)' }
}

if ($MyInvocation.InvocationName -ne '.') {
    if ($Stage -eq 'Uninstall') { Invoke-Uninstall; return }
    $files = Find-PackageFiles -Folder $Folder -Package $Package -Certificate $Certificate -Platform $Platform
    Write-Host "Package:      $($files.Package)"
    Write-Host "Platform:     $($files.Platform) ($($files.Dependencies.Count) dependency package(s))"
    Write-Host "Certificate:  $($files.Certificate)"
    if ($Stage -in 'Trust', 'All') {
        if ($Stage -eq 'Trust' -or (Test-Elevated)) { Invoke-Trust $files }
        else { Write-Host 'Not elevated: skipping the certificate (it must already be trusted, or run this script as administrator)' }
    }
    if ($Stage -in 'Install', 'All') { Invoke-Install $files }
}
