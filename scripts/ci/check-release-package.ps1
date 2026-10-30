<#
.SYNOPSIS
  Fails when a release package (Store or Direct) carries anything that belongs only in a development build.
.DESCRIPTION
  Works on an .msix (unpacked to a temporary folder) or on an already unpacked folder. Checks:
    - AppxManifest.xml: no execution alias, no `timetug-dev`, not the throwaway publisher CN=TimeTug Development, a name that does not end
      in .Dev, no __PARTNER_CENTER_*__ placeholder left, only the capabilities the app needs, a version of the form X.Y.Z.0 (the Store
      reserves the revision), and the logo files the manifest names are in the package.
    - Assemblies (TimeTug*.dll and TimeTug.exe): no self-test runner type or switch, no reference to the developer OAuth file, the developer
      shortcut file or the development alias.
    - With -Sentinel: none of the given texts appears in any file of the package except in -SentinelHome (the one assembly the release
      script embeds the ids in), and each appears there (so the check cannot pass because the injection silently did nothing).
  Exit code 0 when clean, 1 with one "FAIL:" line per problem.
.PARAMETER Package
  The .msix.
.PARAMETER Folder
  An unpacked package (instead of -Package).
.PARAMETER ExpectedVersion
  The version the package must have (for example 1.2.3.0).
.PARAMETER Sentinel
  Fake OAuth values that the build was given; they must not leak (see above). Never pass a real id.
.PARAMETER SentinelHome
  File name that may, and must, contain the sentinels. Default TimeTug.Presentation.dll.
#>
[CmdletBinding()]
param(
    [string]$Package,
    [string]$Folder,
    [string]$ExpectedVersion,
    [string[]]$Sentinel = @(),
    [string]$SentinelHome = 'TimeTug.Presentation.dll'
)

$ErrorActionPreference = 'Stop'

if (-not ('TimeTugByteSearch' -as [type])) {
    Add-Type -TypeDefinition @'
public static class TimeTugByteSearch
{
    public static bool Contains(byte[] data, byte[] needle)
    {
        if (needle.Length == 0 || data.Length < needle.Length) { return false; }
        int last = data.Length - needle.Length;
        int start = 0;
        while (start <= last)
        {
            int at = System.Array.IndexOf(data, needle[0], start, last - start + 1);
            if (at < 0) { return false; }
            int i = 1;
            while (i < needle.Length && data[at + i] == needle[i]) { i++; }
            if (i == needle.Length) { return true; }
            start = at + 1;
        }
        return false;
    }
}
'@
}

$script:AllowedCapabilities = @('runFullTrust', 'internetClient')
# Strings that only a development build contains. UTF-8 (type and namespace names in the metadata) or UTF-16 (string literals).
$script:ForbiddenInAssemblies = @(
    @{ Text = 'TimeTug.App.SelfTest'; Why = 'the self-test namespace' },
    @{ Text = 'SelfTestRunner'; Why = 'the self-test runner type' },
    @{ Text = '--selftest'; Why = 'the self-test switch' },
    @{ Text = 'dev-oauth'; Why = 'the developer OAuth file' },
    @{ Text = 'dev-hotkey'; Why = 'the developer shortcut file' },
    @{ Text = 'timetug-dev'; Why = 'the development execution alias' }
)

function Get-Needles([string]$Text) {
    return @(
        (New-Object System.Text.UTF8Encoding($false)).GetBytes($Text),
        [System.Text.Encoding]::Unicode.GetBytes($Text)
    )
}

function Test-FileHasText([string]$Path, [string]$Text) {
    $data = [System.IO.File]::ReadAllBytes($Path)
    foreach ($needle in (Get-Needles $Text)) {
        if ([TimeTugByteSearch]::Contains($data, $needle)) { return $true }
    }
    return $false
}

# Problems found in the manifest, as strings (empty when it is clean). $Root is the unpacked package.
function Get-ManifestProblems([string]$Root, [string]$ExpectedVersion) {
    $problems = New-Object System.Collections.Generic.List[string]
    $path = Join-Path $Root 'AppxManifest.xml'
    if (-not (Test-Path -LiteralPath $path)) { $problems.Add('AppxManifest.xml is missing'); return $problems }
    $text = Get-Content -LiteralPath $path -Raw
    [xml]$manifest = $text
    $nodes = $manifest.SelectNodes('//*')

    if (@($nodes | Where-Object { $_.LocalName -in 'ExecutionAlias', 'AppExecutionAlias' }).Count -gt 0) { $problems.Add('the manifest declares an execution alias (development builds only)') }
    if ($text -match 'timetug-dev') { $problems.Add('the manifest names timetug-dev') }
    if ($text -match '__PARTNER_CENTER_[A-Z_]*__') { $problems.Add('the manifest still has a __PARTNER_CENTER_*__ placeholder; copy the value from Partner Center') }

    $identity = $nodes | Where-Object { $_.LocalName -eq 'Identity' } | Select-Object -First 1
    if (-not $identity) { $problems.Add('the manifest has no Identity'); return $problems }
    if ($identity.GetAttribute('Publisher') -eq 'CN=TimeTug Development') { $problems.Add('the publisher is the throwaway development publisher') }
    if ($identity.GetAttribute('Name') -match '\.Dev$') { $problems.Add('the package name is the development identity') }
    $version = $identity.GetAttribute('Version')
    if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+\.0$') { $problems.Add("the version '$version' is not Major.Minor.Build.0 (the Store reserves the revision)") }
    if ($ExpectedVersion -and $version -ne $ExpectedVersion) { $problems.Add("the version is '$version', expected '$ExpectedVersion'") }

    foreach ($capability in @($nodes | Where-Object { $_.LocalName -eq 'Capability' })) {
        $name = $capability.GetAttribute('Name')
        if ($script:AllowedCapabilities -notcontains $name) { $problems.Add("the capability '$name' is not one the app needs") }
    }

    $logos = @()
    foreach ($node in $nodes) {
        if ($node.LocalName -eq 'Logo') { $logos += $node.InnerText }
        if ($node.LocalName -eq 'VisualElements') {
            foreach ($attribute in 'Square150x150Logo', 'Square44x44Logo') { if ($node.HasAttribute($attribute)) { $logos += $node.GetAttribute($attribute) } }
        }
    }
    if ($logos.Count -lt 3) { $problems.Add('the manifest names fewer than three logos (StoreLogo, Square150x150Logo, Square44x44Logo)') }
    foreach ($logo in $logos) {
        $relative = $logo -replace '\\', '/'
        $directory = Join-Path $Root (Split-Path -Parent $relative)
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($relative)
        $extension = [System.IO.Path]::GetExtension($relative)
        $found = (Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -eq "$stem$extension" -or $_.Name -like "$stem.scale-*$extension" -or $_.Name -like "$stem.targetsize-*$extension" }).Count -gt 0
        if (-not $found) { $problems.Add("the logo '$logo' named in the manifest is not in the package") }
    }
    return $problems
}

function Get-AssemblyProblems([string]$Root) {
    $problems = New-Object System.Collections.Generic.List[string]
    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object { $_.Name -like 'TimeTug*.dll' -or $_.Name -eq 'TimeTug.exe' })
    if ($files.Count -eq 0) { $problems.Add('no TimeTug assembly found in the package') }
    foreach ($file in $files) {
        foreach ($forbidden in $script:ForbiddenInAssemblies) {
            if (Test-FileHasText $file.FullName $forbidden.Text) { $problems.Add("$($file.Name) contains $($forbidden.Why)") }
        }
    }
    return $problems
}

function Get-SentinelProblems([string]$Root, [string[]]$Sentinels, [string]$AllowedFile) {
    $problems = New-Object System.Collections.Generic.List[string]
    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -File)
    for ($index = 0; $index -lt $Sentinels.Count; $index++) {
        $text = $Sentinels[$index]
        $label = "sentinel #$($index + 1)"
        $inHome = $false
        foreach ($file in $files) {
            if (-not (Test-FileHasText $file.FullName $text)) { continue }
            if ($file.Name -eq $AllowedFile) { $inHome = $true } else { $problems.Add("$label appears in $($file.FullName.Substring($Root.Length).TrimStart('\', '/'))") }
        }
        if (-not $inHome) { $problems.Add("$label is not in $AllowedFile, so the check proves nothing (was it injected?)") }
    }
    return $problems
}

function Expand-ForCheck([string]$Package) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $target = Join-Path ([System.IO.Path]::GetTempPath()) ('release-check-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Compression.ZipFile]::ExtractToDirectory($Package, $target)
    return $target
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $Package -and -not $Folder) { throw 'pass -Package <file.msix> or -Folder <unpacked package>' }
    $cleanup = $null
    if ($Package) {
        if (-not (Test-Path -LiteralPath $Package -PathType Leaf)) { throw "package not found: $Package" }
        $Folder = Expand-ForCheck $Package
        $cleanup = $Folder
    }
    try {
        $Folder = (Resolve-Path -LiteralPath $Folder).Path
        $all = @()
        $all += @(Get-ManifestProblems $Folder $ExpectedVersion)
        $all += @(Get-AssemblyProblems $Folder)
        if ($Sentinel.Count -gt 0) { $all += @(Get-SentinelProblems $Folder $Sentinel $SentinelHome) }
        foreach ($problem in $all) { Write-Host "FAIL: $problem" }
        if ($all.Count -gt 0) { Write-Host "$($all.Count) problem(s) in the release package"; exit 1 }
        Write-Host 'PASS: the release package carries no development scaffolding'
        $global:LASTEXITCODE = 0
    }
    finally { if ($cleanup -and (Test-Path -LiteralPath $cleanup)) { Remove-Item -Recurse -Force -LiteralPath $cleanup } }
}
