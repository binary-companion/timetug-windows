<#
.SYNOPSIS
  Builds the development MSIX for one architecture, signs it with a throwaway certificate, trusts that
  certificate on this machine and installs the package.
.DESCRIPTION
  The certificate is created here, lives two days and is never exported with its private key, so no
  signing material exists outside this run. Trusting it needs an elevated session (CI runners are; the
  VM's `prlctl exec` runs as SYSTEM). Installing registers the package for the current user, so on the
  VM run -Stage Build as SYSTEM and -Stage Install as the interactive user (see docs/development.md).
.PARAMETER Platform
  x64 or ARM64. Defaults to the architecture of this machine.
.PARAMETER Configuration
  CI (default) or Debug. Both define SELFTEST.
.PARAMETER OutputFolder
  Where the package and the public certificate go. Default: build\msix under the repository.
.PARAMETER Stage
  Build (certificate, publish, trust), Install (install the built package) or All (default).
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'ARM64' } else { 'x64' }),
    [ValidateSet('Debug', 'CI')]
    [string]$Configuration = 'CI',
    [string]$OutputFolder,
    [ValidateSet('Build', 'Install', 'All')]
    [string]$Stage = 'All'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutputFolder) { $OutputFolder = Join-Path $repo 'build\msix' }
$OutputFolder = [System.IO.Path]::GetFullPath($OutputFolder)
$packageName = 'BinaryCompanion.TimeTug.Dev'
$publisher = 'CN=TimeTug Development'
$arch = $Platform.ToLowerInvariant()
$layout = Join-Path $OutputFolder 'layout'
$aliasPath = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\timetug-dev.exe'

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    & $Body
    Write-Host ("[{0}] {1:n1}s" -f $Name, $clock.Elapsed.TotalSeconds)
}

if ($Stage -in 'Build', 'All') {
    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
    if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }

    Invoke-Step 'certificate' {
        $script:cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
            -FriendlyName 'TimeTug development (throwaway)' -CertStoreLocation Cert:\CurrentUser\My `
            -NotAfter (Get-Date).AddDays(2) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Export-Certificate -Cert $script:cert -FilePath (Join-Path $OutputFolder 'dev.cer') | Out-Null
    }

    Invoke-Step 'publish' {
        Push-Location $repo
        try {
            dotnet publish src/TimeTug.App/TimeTug.App.csproj -c $Configuration "-p:Platform=$Platform" `
                -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true `
                "-p:PackageCertificateThumbprint=$($script:cert.Thumbprint)" `
                "-p:AppxPackageDir=$layout\" -p:AppxBundle=Never --nologo
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
        }
        finally { Pop-Location }
    }

    Invoke-Step 'trust' {
        # Machine-wide trust is what lets a standard-user install accept the self-signed package.
        Import-Certificate -FilePath (Join-Path $OutputFolder 'dev.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    }
    Remove-Item -ErrorAction SilentlyContinue "Cert:\CurrentUser\My\$($script:cert.Thumbprint)"
}

if ($Stage -in 'Install', 'All') {
    Invoke-Step 'install' {
        $msix = Get-ChildItem -Path $layout -Recurse -Filter '*.msix' |
            Where-Object { $_.FullName -notmatch '\\Dependencies\\' } | Select-Object -First 1
        if (-not $msix) { throw "no package under $layout; run -Stage Build first" }
        $dependencies = @(Get-ChildItem -Path (Join-Path $msix.DirectoryName "Dependencies\$arch") -Filter '*.msix' -ErrorAction SilentlyContinue | ForEach-Object FullName)

        Get-AppxPackage -Name $packageName | Remove-AppxPackage
        if ($dependencies.Count -gt 0) {
            Add-AppxPackage -Path $msix.FullName -DependencyPath $dependencies
        }
        else {
            Add-AppxPackage -Path $msix.FullName
        }
    }

    $installed = Get-AppxPackage -Name $packageName
    if (-not $installed) { throw 'the package did not install' }
    Write-Host "Installed $($installed.PackageFullName)"
    Write-Host "Launch command: $aliasPath --selftest hello --out <folder>"
}
