<#
.SYNOPSIS
  Builds a release package (Store or Direct flavour) for one architecture: no development scaffolding, runtimes bundled, OAuth ids from the environment.
.DESCRIPTION
  Produces, in -OutputFolder:
    TimeTug_<version>_<arch>.msix        the package, UNSIGNED (the Store re-signs it; the Direct flavour is signed later, when its certificate exists)
    TimeTug_<version>_<arch>.msixupload  a zip holding that package, the shape Partner Center accepts for one architecture
                                         (bundle-msixupload.ps1 makes the two-architecture upload)
    build-info.txt                       flavour, version, architecture, commit and whether it was signed; no ids
  Steps: validate the version, fill the manifest (identity values and version; any __PARTNER_CENTER_*__ placeholder still open is an error),
  write the OAuth ids into the generated source file from the environment (write-oauth-ids.ps1) and delete it afterwards, `dotnet publish`
  -c Release -p:Flavour=<flavour>, then run scripts/ci/check-release-package.ps1 on the result.

  -DryRun is the pull-request check: fake OAuth ids (so the injection is exercised and the leak check has something to look for), a throwaway
  self-signed certificate created here (never exported with its key) and a throwaway identity, so the package installs on a test machine.
  A dry run uses no secret and no Partner Center value.
.PARAMETER Flavour
  Store or Direct.
.PARAMETER Platform
  x64 or ARM64.
.PARAMETER Version
  1.2.3 or v1.2.3 (a release tag). The package version is 1.2.3.0: the Store reserves the revision. Anything else is refused.
.PARAMETER Prerelease
  The version came from a suffixed tag (v1.2.3-beta.1): required for a suffix, refused without one. The package is named ...-flight and the
  version stays X.Y.Z.0 (a Store flight shares the Store identity; the owner picks a higher X.Y.Z for each flight).
.PARAMETER OutputFolder
  Where the results go. Default: build\release\<flavour>-<arch> under the repository.
.PARAMETER IdentityName, Publisher, PublisherDisplayName, AppName
  Partner Center values (Product identity). Default: the environment variables PARTNER_CENTER_PACKAGE_NAME, PARTNER_CENTER_PUBLISHER,
  PARTNER_CENTER_PUBLISHER_DISPLAY_NAME and PARTNER_CENTER_APP_NAME, else what is in the manifest (the placeholders, which fail the build).
.PARAMETER RequireOAuthIds
  Fail when the OAuth ids are not all in the environment (a real release does this).
.PARAMETER DryRun
  See above.
.PARAMETER SkipCheck
  Do not run check-release-package.ps1 (never used by the workflows).
#>
[CmdletBinding()]
param(
    [ValidateSet('Store', 'Direct')]
    [string]$Flavour = 'Store',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'ARM64' } else { 'x64' }),
    [string]$Version,
    [switch]$Prerelease,
    [string]$OutputFolder,
    [string]$IdentityName = $env:PARTNER_CENTER_PACKAGE_NAME,
    [string]$Publisher = $env:PARTNER_CENTER_PUBLISHER,
    [string]$PublisherDisplayName = $env:PARTNER_CENTER_PUBLISHER_DISPLAY_NAME,
    [string]$AppName = $env:PARTNER_CENTER_APP_NAME,
    [switch]$RequireOAuthIds,
    [switch]$DryRun,
    [switch]$SkipCheck
)

$ErrorActionPreference = 'Stop'
$script:Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# The fake values of a dry run. They stand for the real ids and must never be one.
$script:DryRunOAuth = [ordered]@{
    GOOGLE_OAUTH_CLIENT_ID     = 'dryrun-google-client-id.apps.invalid'
    GOOGLE_OAUTH_CLIENT_SECRET = 'dryrun-google-client-secret-0001'
    MICROSOFT_OAUTH_CLIENT_ID  = '00000000-0000-0000-0000-00000000d0d0'
}
$script:DryRunIdentity = @{
    IdentityName         = 'BinaryCompanion.TimeTug.ReleaseDryRun'
    Publisher            = 'CN=TimeTug Release Dry Run'
    PublisherDisplayName = 'Binary Companion LLC (dry run)'
    AppName              = 'TimeTug dry run'
}

# 'v1.2.3' or '1.2.3' -> @{ Semantic = '1.2.3'; Package = '1.2.3.0'; Suffix = '' }. A suffix ('-beta.1') is allowed only with -Prerelease, and -Prerelease needs one.
function ConvertTo-PackageVersion([string]$Text, [switch]$Prerelease) {
    if (-not $Text) { throw 'a version is required: 1.2.3 or v1.2.3' }
    if ($Text -notmatch '^v?(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
        throw "'$Text' is not a release version: it must be X.Y.Z or vX.Y.Z (numbers only, no leading zeros), optionally with a -suffix for a flight"
    }
    $parts = @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    foreach ($part in $parts) { if ($part -gt 65535) { throw "'$Text': each number of a package version must be at most 65535" } }
    $suffix = if ($Matches[4]) { $Matches[4] } else { '' }
    if ($suffix -and -not $Prerelease) { throw "'$Text' has a suffix: only a flight (-Prerelease) may; the stable package is built from a plain vX.Y.Z" }
    if ($Prerelease -and -not $suffix) { throw "-Prerelease needs a suffixed version such as v1.2.3-beta.1 (got '$Text')" }
    $semantic = "$($parts[0]).$($parts[1]).$($parts[2])"
    return @{ Semantic = $semantic; Package = "$semantic.0"; Suffix = $suffix }
}

# The manifest text for a flavour with the values filled in. Throws, naming the placeholders (never any value) that are still open.
function New-ReleaseManifestText([string]$Template, [string]$PackageVersion, [hashtable]$Values) {
    $text = $Template
    $text = [regex]::Replace($text, '(<Identity\b[^>]*?\bVersion=")[^"]*(")', { param($m) $m.Groups[1].Value + $PackageVersion + $m.Groups[2].Value })
    $map = @{
        '__PARTNER_CENTER_PACKAGE_NAME__'              = $Values.IdentityName
        '__PARTNER_CENTER_PUBLISHER__'                 = $Values.Publisher
        '__PARTNER_CENTER_PUBLISHER_DISPLAY_NAME__'    = $Values.PublisherDisplayName
        '__PARTNER_CENTER_APP_NAME__'                  = $Values.AppName
    }
    foreach ($placeholder in $map.Keys) {
        if ($map[$placeholder]) { $text = $text.Replace($placeholder, [System.Security.SecurityElement]::Escape($map[$placeholder])) }
    }
    $open = @([regex]::Matches($text, '__PARTNER_CENTER_[A-Z_]+__') | ForEach-Object { $_.Value } | Sort-Object -Unique)
    if ($open.Count -gt 0) {
        throw ('Partner Center values are missing: ' + ($open -join ', ') + '. Copy them from Partner Center (Product identity) into the release environment variables ' +
            'PARTNER_CENTER_PACKAGE_NAME, PARTNER_CENTER_PUBLISHER, PARTNER_CENTER_PUBLISHER_DISPLAY_NAME and PARTNER_CENTER_APP_NAME (or into the manifest).')
    }
    return $text
}

function Get-ArtifactBaseName([string]$PackageVersion, [string]$Platform, [string]$Suffix) {
    $flight = if ($Suffix) { '-flight' } else { '' }
    return "TimeTug_${PackageVersion}_$($Platform.ToLowerInvariant())$flight"
}

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    & $Body
    Write-Host ('[{0}] {1:n1}s' -f $Name, $clock.Elapsed.TotalSeconds)
}

if ($MyInvocation.InvocationName -ne '.') {
    $parsed = ConvertTo-PackageVersion $Version -Prerelease:$Prerelease
    if ($DryRun -and $Flavour -ne 'Store') { throw '-DryRun builds the Store flavour only' }
    if (-not $OutputFolder) { $OutputFolder = Join-Path $script:Repo ("build\release\" + $Flavour.ToLowerInvariant() + '-' + $Platform.ToLowerInvariant()) }
    $OutputFolder = [System.IO.Path]::GetFullPath($OutputFolder)
    if (Test-Path $OutputFolder) { Remove-Item -Recurse -Force $OutputFolder }
    New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
    $layout = Join-Path $OutputFolder 'layout'
    $baseName = Get-ArtifactBaseName $parsed.Package $Platform $parsed.Suffix
    $generatedIds = Join-Path $script:Repo 'src\TimeTug.Presentation\Startup\ReleaseOAuthIds.g.cs'
    $writeIds = Join-Path $PSScriptRoot 'write-oauth-ids.ps1'
    $checkScript = Join-Path $script:Repo 'scripts\ci\check-release-package.ps1'

    # ---- manifest ----
    $values = @{ IdentityName = $IdentityName; Publisher = $Publisher; PublisherDisplayName = $PublisherDisplayName; AppName = $AppName }
    if ($Flavour -eq 'Direct') {
        # The Direct identity is fixed in its manifest; only the version is filled in.
        $values = @{}
    }
    if ($DryRun) { $values = $script:DryRunIdentity }
    $manifestFolder = Join-Path $OutputFolder 'manifest'
    New-Item -ItemType Directory -Force -Path $manifestFolder | Out-Null
    $manifestPath = Join-Path $manifestFolder "Package.$Flavour.appxmanifest"
    $template = Get-Content -LiteralPath (Join-Path $script:Repo "src\TimeTug.App\Package.$Flavour.appxmanifest") -Raw
    $manifestText = New-ReleaseManifestText $template $parsed.Package $values
    [System.IO.File]::WriteAllText($manifestPath, $manifestText, (New-Object System.Text.UTF8Encoding($false)))

    $sentinels = @()
    $cert = $null
    $savedEnvironment = @{}
    try {
        # ---- OAuth ids ----
        Invoke-Step 'oauth ids' {
            if ($DryRun) {
                foreach ($name in $script:DryRunOAuth.Keys) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $script:DryRunOAuth[$name]) }
                $script:sentinels = @($script:DryRunOAuth.Values)
                & $writeIds -Require
            }
            elseif ($RequireOAuthIds) { & $writeIds -Require }
            else { & $writeIds }
        }

        # ---- signing (dry run only) ----
        $signing = @('-p:AppxPackageSigningEnabled=false')
        if ($DryRun) {
            $cert = New-SelfSignedCertificate -Type Custom -Subject $script:DryRunIdentity.Publisher -KeyUsage DigitalSignature `
                -FriendlyName 'TimeTug release dry run (throwaway)' -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddDays(3) `
                -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
            Export-Certificate -Cert $cert -FilePath (Join-Path $OutputFolder 'dryrun.cer') | Out-Null
            $signing = @('-p:AppxPackageSigningEnabled=true', "-p:PackageCertificateThumbprint=$($cert.Thumbprint)")
        }

        # ---- build ----
        Invoke-Step 'publish' {
            Push-Location $script:Repo
            try {
                dotnet publish src/TimeTug.App/TimeTug.App.csproj -c Release "-p:Platform=$Platform" "-p:Flavour=$Flavour" "-p:ReleaseManifestPath=$manifestPath" `
                    "-p:Version=$($parsed.Semantic)" -p:GenerateAppxPackageOnBuild=true @signing "-p:AppxPackageDir=$layout\" -p:AppxBundle=Never --nologo
                if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
            }
            finally { Pop-Location }
        }
    }
    finally {
        # The ids must not stay on disk, whatever happened.
        & $writeIds -Remove | Out-Null
        foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
        if ($cert) { Remove-Item -ErrorAction SilentlyContinue "Cert:\CurrentUser\My\$($cert.Thumbprint)" }
    }

    # ---- results ----
    $built = @(Get-ChildItem -LiteralPath $layout -Recurse -Filter '*.msix' -File | Where-Object { $_.FullName -notmatch '[\\/]Dependencies[\\/]' })
    if ($built.Count -ne 1) { throw "expected one package under $layout, found $($built.Count)" }
    $msix = Join-Path $OutputFolder "$baseName.msix"
    Copy-Item -LiteralPath $built[0].FullName -Destination $msix
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $upload = Join-Path $OutputFolder "$baseName.msixupload"
    $zip = [System.IO.Compression.ZipFile]::Open($upload, 'Create')
    try { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $msix, "$baseName.msix", 'Optimal') }
    finally { $zip.Dispose() }
    $commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (git -C $script:Repo rev-parse HEAD 2>$null) }
    @(
        "flavour: $Flavour", "version: $($parsed.Package)", "architecture: $Platform", "flight: $([bool]$parsed.Suffix)", "commit: $commit",
        "signed: $([bool]$DryRun) (dry run only; a real release package is unsigned)"
    ) | Set-Content -LiteralPath (Join-Path $OutputFolder 'build-info.txt')
    Remove-Item -Recurse -Force -LiteralPath $manifestFolder
    Write-Host ('Package: {0} ({1:n1} MB)' -f $msix, ((Get-Item $msix).Length / 1MB))

    if (-not $SkipCheck) {
        Invoke-Step 'check' {
            $arguments = @{ Package = $msix; ExpectedVersion = $parsed.Package }
            if ($DryRun) { $arguments.Sentinel = [string[]]$script:sentinels }
            & $checkScript @arguments
            if ($LASTEXITCODE -ne 0) { throw 'check-release-package.ps1 found development scaffolding in the package' }
        }
    }
}
