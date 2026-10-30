<#
.SYNOPSIS
  Decides what the Windows release workflow builds, and refuses everything else. Holds no secret and touches no environment.
.DESCRIPTION
  Inputs are the workflow event's facts; the answer goes to $GITHUB_OUTPUT as go, version, ref and flight.
    release / published:  the release tag (vX.Y.Z, or vX.Y.Z-suffix for a flight). A tag that is not a version is skipped (go=false).
                          A plain vX.Y.Z tag on a release marked pre-release is skipped too: the stable package is built only for a
                          stable release.
    workflow_dispatch:    -InputVersion (X.Y.Z, or X.Y.Z-suffix for a flight) and optionally -InputRef. The ref may only be the
                          default branch or a version tag (vX.Y.Z or vX.Y.Z-suffix) whose version equals the input; blank means the
                          tag v<version>, which must already exist (the workflow cannot create a tag).
  The same rules the Mac release workflows use. A suffixed version is a flight (the Store package flight): -Prerelease for the build script.
.PARAMETER EventName
  release or workflow_dispatch.
.PARAMETER TagExists
  Whether the tag v<InputVersion> exists (the workflow looks it up with the API and passes the answer).
#>
[CmdletBinding()]
param(
    [ValidateSet('release', 'workflow_dispatch')]
    [string]$EventName,
    [string]$ReleaseTag,
    [bool]$ReleasePrerelease = $false,
    [string]$InputVersion,
    [string]$InputRef,
    [string]$DefaultBranch = 'main',
    [bool]$TagExists = $false
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')

# @{ Go; Reason; Version; Ref; Flight } - Version is the text for build-release-msix.ps1 (with its suffix, without the leading v).
function Resolve-ReleaseRequest([string]$EventName, [string]$ReleaseTag, [bool]$ReleasePrerelease, [string]$InputVersion, [string]$InputRef, [string]$DefaultBranch, [bool]$TagExists) {
    $skip = { param($why) return @{ Go = $false; Reason = $why; Version = ''; Ref = ''; Flight = $false } }
    if ($EventName -eq 'release') {
        if (-not $ReleaseTag -or $ReleaseTag -notmatch '\Av[0-9]') { return & $skip "the tag '$ReleaseTag' is not a version tag" }
        try { $parsed = ConvertTo-PackageVersion $ReleaseTag -Prerelease:($ReleaseTag -match '-') } catch { return & $skip "the tag '$ReleaseTag' is not a release version" }
        $flight = [bool]$parsed.Suffix
        if (-not $flight -and $ReleasePrerelease) { return & $skip "the release for $ReleaseTag is marked pre-release; the stable package is built only for a stable release" }
        return @{ Go = $true; Reason = ''; Version = $ReleaseTag.Substring(1); Ref = $ReleaseTag; Flight = $flight }
    }

    # workflow_dispatch: the version is required and must be exact.
    $text = if ($InputVersion) { $InputVersion } else { '' }
    if ($text.StartsWith('v')) { throw 'enter the version without the leading v (X.Y.Z, or X.Y.Z-suffix for a flight)' }
    $flight = $text -match '-'
    $parsed = ConvertTo-PackageVersion $text -Prerelease:$flight
    $version = $parsed.Semantic + $parsed.Suffix
    if ($InputRef) {
        if ($InputRef -ne $DefaultBranch) {
            if ($InputRef -notmatch '\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z][0-9A-Za-z.-]*)?\z') {
                throw "ref must be $DefaultBranch or a vX.Y.Z (or vX.Y.Z-suffix) tag, not '$InputRef'"
            }
            if ($InputRef.Substring(1) -ne $version) { throw "the ref $InputRef does not match the version $version" }
        }
        return @{ Go = $true; Reason = ''; Version = $version; Ref = $InputRef; Flight = $flight }
    }
    if (-not $TagExists) { throw "tag v$version does not exist. Publish the release in the GitHub UI, or pass ref=$DefaultBranch for a trial build." }
    return @{ Go = $true; Reason = ''; Version = $version; Ref = "v$version"; Flight = $flight }
}

if ($MyInvocation.InvocationName -ne '.') {
    $result = Resolve-ReleaseRequest $EventName $ReleaseTag $ReleasePrerelease $InputVersion $InputRef $DefaultBranch $TagExists
    if (-not $result.Go) { Write-Host "Nothing to build: $($result.Reason)." }
    else { Write-Host "Building version $($result.Version) from $($result.Ref)$(if ($result.Flight) { ' (a flight)' } else { '' })." }
    if ($env:GITHUB_OUTPUT) {
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value @("go=$($result.Go.ToString().ToLowerInvariant())", "version=$($result.Version)", "ref=$($result.Ref)", "flight=$($result.Flight.ToString().ToLowerInvariant())")
    }
}
