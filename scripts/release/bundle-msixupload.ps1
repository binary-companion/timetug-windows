<#
.SYNOPSIS
  Makes the two-architecture Store upload from the x64 and ARM64 packages that build-release-msix.ps1 produced.
.DESCRIPTION
  Takes TimeTug_<version>_x64.msix and TimeTug_<version>_arm64.msix (or the -flight names) from -Folder, runs `makeappx bundle` into
  TimeTug_<version>_x64_arm64.msixbundle and zips that into TimeTug_<version>_x64_arm64.msixupload, the file Partner Center takes. Neither is signed:
  the Store signs what it accepts. Nothing is uploaded or submitted; the owner does that in Partner Center (phase 9 may add it).
.PARAMETER Folder
  Where the two .msix files are (searched recursively).
.PARAMETER OutputFolder
  Where the bundle and the upload file go. Default: -Folder.
.PARAMETER Version
  The package version both packages must have (1.2.3.0). Both are read from their file names and must agree with it.
#>
[CmdletBinding()]
param(
    [string]$Folder,
    [string]$OutputFolder,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

# Newest makeappx.exe: the Windows SDK on a CI runner, else the one in the Windows SDK build tools package the project restores.
function Find-MakeAppx {
    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $HOME '.nuget\packages\microsoft.windows.sdk.buildtools')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $found = foreach ($root in $roots) { Get-ChildItem -LiteralPath $root -Recurse -Filter 'makeappx.exe' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '[\\/]x64[\\/]' } }
    $best = $found | Sort-Object { $_.FullName } -Descending | Select-Object -First 1
    if (-not $best) { throw 'makeappx.exe not found (install the Windows SDK, or restore the project once)' }
    return $best.FullName
}

# 'TimeTug_1.2.3.0_x64.msix' / '..._arm64-flight.msix' -> @{ Version; Architecture; Flight }, or $null for any other name.
function Read-PackageName([string]$FileName) {
    if ($FileName -match '\ATimeTug_([0-9]+\.[0-9]+\.[0-9]+\.0)_(x64|arm64)(-flight)?\.msix\z') {
        return @{ Version = $Matches[1]; Architecture = $Matches[2]; Flight = [bool]$Matches[3] }
    }
    return $null
}

function Get-BundleBaseName([string]$Version, [bool]$Flight) {
    $flightPart = if ($Flight) { '-flight' } else { '' }
    return "TimeTug_${Version}_x64_arm64$flightPart"
}

# The two packages, validated: one per architecture, one version, the same flight-ness.
function Select-BundleInputs([string[]]$Files, [string]$ExpectedVersion) {
    $packages = @()
    foreach ($file in $Files) {
        $info = Read-PackageName ([System.IO.Path]::GetFileName($file))
        if ($info) { $info.Path = $file; $packages += , $info }
    }
    foreach ($arch in 'x64', 'arm64') {
        $count = @($packages | Where-Object { $_.Architecture -eq $arch }).Count
        if ($count -ne 1) { throw "expected exactly one $arch package, found $count" }
    }
    if (@($packages | ForEach-Object { $_.Version } | Sort-Object -Unique).Count -ne 1) { throw 'the x64 and ARM64 packages have different versions' }
    if (@($packages | ForEach-Object { $_.Flight } | Sort-Object -Unique).Count -ne 1) { throw 'one package is a flight and the other is not' }
    if ($ExpectedVersion -and $packages[0].Version -ne $ExpectedVersion) { throw "the packages are version $($packages[0].Version), not $ExpectedVersion" }
    return $packages
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $Folder) { throw '-Folder is required' }
    $Folder = (Resolve-Path -LiteralPath $Folder).Path
    if (-not $OutputFolder) { $OutputFolder = $Folder }
    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
    $OutputFolder = (Resolve-Path -LiteralPath $OutputFolder).Path
    $files = @(Get-ChildItem -LiteralPath $Folder -Recurse -Filter '*.msix' -File | ForEach-Object { $_.FullName })
    $inputs = Select-BundleInputs $files $Version
    $base = Get-BundleBaseName $inputs[0].Version $inputs[0].Flight

    $stage = Join-Path ([System.IO.Path]::GetTempPath()) ('bundle-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    try {
        foreach ($package in $inputs) { Copy-Item -LiteralPath $package.Path -Destination $stage }
        $bundle = Join-Path $OutputFolder "$base.msixbundle"
        if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
        & (Find-MakeAppx) bundle /d $stage /bv $inputs[0].Version /p $bundle /o
        if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed ($LASTEXITCODE)" }
    }
    finally { Remove-Item -Recurse -Force -LiteralPath $stage -ErrorAction SilentlyContinue }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $upload = Join-Path $OutputFolder "$base.msixupload"
    if (Test-Path -LiteralPath $upload) { Remove-Item -LiteralPath $upload -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($upload, 'Create')
    try { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $bundle, "$base.msixbundle", 'Optimal') }
    finally { $zip.Dispose() }
    Write-Host ('Bundle: {0} ({1:n1} MB)' -f $bundle, ((Get-Item $bundle).Length / 1MB))
    Write-Host ('Upload: {0} ({1:n1} MB)' -f $upload, ((Get-Item $upload).Length / 1MB))
}
