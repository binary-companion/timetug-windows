<#
.SYNOPSIS
  Collects what the "msix-<arch>" workflow artifact contains, and refuses anything else.
.DESCRIPTION
  After build-msix.ps1 has built (and signed) the development package, this copies into one folder:
    the .msix, Dependencies\<arch>\*.msix, dev.cer (the PUBLIC certificate), vm-install.ps1, commit.txt, README.txt.
  Then it checks the folder against an allow-list of file types and fails on anything that could hold a private key
  (.pfx, .p12, .pvk, .key, .pem, .snk, ...) or any file that carries one. The workflow uploads only this folder.
.PARAMETER Platform
  x64 or ARM64: the package that was just built.
.PARAMETER BuildFolder
  The build-msix.ps1 output folder. Default: build\msix.
.PARAMETER Destination
  The folder to fill. Default: build\msix-artifact.
.PARAMETER Commit
  The commit the package was built from, written to commit.txt. Default: $env:GITHUB_SHA, else `git rev-parse HEAD`.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform,
    [string]$BuildFolder,
    [string]$Destination,
    [string]$Commit
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $BuildFolder) { $BuildFolder = Join-Path $repo 'build\msix' }
if (-not $Destination) { $Destination = Join-Path $repo 'build\msix-artifact' }
if (-not $Commit) { $Commit = $env:GITHUB_SHA }
if (-not $Commit) { $Commit = (git -C $repo rev-parse HEAD) }
$arch = $Platform.ToLowerInvariant()
$allowedExtensions = @('.msix', '.cer', '.ps1', '.txt')

function Assert-Staged([string]$Folder) {
    $files = @(Get-ChildItem -LiteralPath $Folder -Recurse -File)
    foreach ($file in $files) {
        if ($allowedExtensions -notcontains $file.Extension.ToLowerInvariant()) { throw "not allowed in the artifact (file type): $($file.FullName)" }
        if ($file.Name -match '(?i)\.(pfx|p12|pvk|key|pem|snk|jks|keystore)') { throw "looks like key material, not allowed in the artifact: $($file.FullName)" }
        if ($file.Extension -in '.ps1', '.txt', '.cer') {
            $text = [System.IO.File]::ReadAllText($file.FullName)
            if ($text -match 'PRIVATE KEY-----') { throw "contains a private key block, not allowed in the artifact: $($file.FullName)" }
        }
    }
    $cer = $files | Where-Object { $_.Name -eq 'dev.cer' }
    if (-not $cer) { throw 'dev.cer is missing from the artifact' }
    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $cer.FullName
    if ($certificate.HasPrivateKey) { throw 'dev.cer carries a private key' }
    if (-not ($files | Where-Object { $_.Extension -eq '.msix' -and $_.FullName -notmatch '[\\/]Dependencies[\\/]' })) { throw 'the package is missing from the artifact' }
    if (-not ($files | Where-Object { $_.Name -eq 'vm-install.ps1' })) { throw 'vm-install.ps1 is missing from the artifact' }
}

$layout = Join-Path $BuildFolder 'layout'
$msix = Get-ChildItem -LiteralPath $layout -Recurse -Filter '*.msix' -File |
    Where-Object { $_.FullName -notmatch '[\\/]Dependencies[\\/]' } | Select-Object -First 1
if (-not $msix) { throw "no package under $layout; run build-msix.ps1 first" }

if (Test-Path $Destination) { Remove-Item -Recurse -Force $Destination }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Copy-Item -LiteralPath $msix.FullName -Destination $Destination
$dependencySource = Join-Path $msix.DirectoryName "Dependencies\$arch"
if (Test-Path $dependencySource) {
    $dependencyTarget = Join-Path $Destination "Dependencies\$arch"
    New-Item -ItemType Directory -Force -Path $dependencyTarget | Out-Null
    Copy-Item -Path (Join-Path $dependencySource '*.msix') -Destination $dependencyTarget
}
Copy-Item -LiteralPath (Join-Path $BuildFolder 'dev.cer') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $repo 'scripts\vm\vm-install.ps1') -Destination $Destination
Set-Content -LiteralPath (Join-Path $Destination 'commit.txt') -Value $Commit
Set-Content -LiteralPath (Join-Path $Destination 'README.txt') -Value @"
TimeTug development package ($Platform), built from commit $Commit.

This is a development build signed with a throwaway certificate (dev.cer, public part only; the key was deleted by the
build and is not here). It is valid for 15 days after the build.

Install (elevated PowerShell, once, trusts the certificate and installs for you):
    powershell -ExecutionPolicy Bypass -File .\vm-install.ps1
Remove it again:
    powershell -ExecutionPolicy Bypass -File .\vm-install.ps1 -Stage Uninstall

It needs the .NET 10 desktop runtime for $Platform. Details: docs/development.md in the repository.
"@

Assert-Staged $Destination
Get-ChildItem -LiteralPath $Destination -Recurse -File |
    ForEach-Object { Write-Host ('{0,12:n0}  {1}' -f $_.Length, $_.FullName.Substring($Destination.Length).TrimStart('\', '/')) }
Write-Host "Staged the $Platform artifact in $Destination"
