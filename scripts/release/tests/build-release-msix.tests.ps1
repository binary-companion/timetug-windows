# Tests for the pure logic in scripts/release/build-release-msix.ps1: version mapping, manifest filling, artifact names.
# Plain PowerShell, no Pester: Windows PowerShell 5.1, PowerShell 7 and pwsh on Linux/macOS.
#   pwsh -NoProfile -File scripts/release/tests/build-release-msix.tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'build-release-msix.ps1')

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }
function Assert-True($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Body, [string]$Pattern) {
    $message = $null
    try { & $Body } catch { $message = $_.Exception.Message }
    if ($null -eq $message) { throw 'expected an error but there was none' }
    if ($message -notmatch $Pattern) { throw "error '$message' does not match '$Pattern'" }
}

Check 'a release tag maps to X.Y.Z.0' {
    Assert-Equal (ConvertTo-PackageVersion 'v1.2.3').Package '1.2.3.0'
    Assert-Equal (ConvertTo-PackageVersion '1.2.3').Package '1.2.3.0'
    Assert-Equal (ConvertTo-PackageVersion 'v10.0.65535').Package '10.0.65535.0'
    Assert-Equal (ConvertTo-PackageVersion 'v1.2.3').Semantic '1.2.3'
}

Check 'anything that is not X.Y.Z is rejected' {
    foreach ($bad in '', 'v1.2', 'v1.2.3.4', 'v01.2.3', 'v1.2.x', 'release-1.2.3', 'v1.2.3+build', 'v1.2.65536', ' v1.2.3', 'v1.2.3 ') {
        Assert-Throws { ConvertTo-PackageVersion $bad } 'version'
    }
}

Check 'a suffix is allowed only for a flight, and a flight needs a suffix' {
    Assert-Throws { ConvertTo-PackageVersion 'v1.2.3-beta.1' } 'only a flight'
    $flight = ConvertTo-PackageVersion 'v1.2.3-beta.1' -Prerelease
    Assert-Equal $flight.Package '1.2.3.0'
    Assert-Equal $flight.Suffix '-beta.1'
    Assert-Throws { ConvertTo-PackageVersion 'v1.2.3' -Prerelease } 'needs a suffixed version'
}

Check 'artifact names carry the version, the architecture and -flight for a flight' {
    Assert-Equal (Get-ArtifactBaseName '1.2.3.0' 'ARM64' '') 'TimeTug_1.2.3.0_arm64'
    Assert-Equal (Get-ArtifactBaseName '1.2.3.0' 'x64' '-beta.1') 'TimeTug_1.2.3.0_x64-flight'
}

$template = @'
<Package><Identity Name="__PARTNER_CENTER_PACKAGE_NAME__" Publisher="__PARTNER_CENTER_PUBLISHER__" Version="0.0.0.0" />
<Properties><DisplayName>__PARTNER_CENTER_APP_NAME__</DisplayName><PublisherDisplayName>__PARTNER_CENTER_PUBLISHER_DISPLAY_NAME__</PublisherDisplayName></Properties></Package>
'@
$filled = @{ IdentityName = 'Example.Name'; Publisher = 'CN=Example & Co'; PublisherDisplayName = 'Example Co'; AppName = 'Example App' }

Check 'the manifest gets its identity values and the package version' {
    $text = New-ReleaseManifestText $template '1.2.3.0' $filled
    Assert-True ($text.Contains('Name="Example.Name"')) 'name'
    Assert-True ($text.Contains('Publisher="CN=Example &amp; Co"')) 'publisher is escaped'
    Assert-True ($text.Contains('Version="1.2.3.0"')) 'version'
    Assert-True ($text.Contains('<DisplayName>Example App</DisplayName>')) 'app name'
    Assert-True (-not $text.Contains('__PARTNER_CENTER')) 'a placeholder is left'
    [xml]$null = $text
}

Check 'an open placeholder fails the build and names the placeholder, not any value' {
    $partial = @{ IdentityName = 'Example.Name'; Publisher = 'CN=Secret Co'; PublisherDisplayName = ''; AppName = $null }
    $thrown = $null
    try { New-ReleaseManifestText $template '1.2.3.0' $partial } catch { $thrown = $_.Exception.Message }
    Assert-True ($null -ne $thrown) 'expected an error'
    Assert-True ($thrown.Contains('__PARTNER_CENTER_APP_NAME__') -and $thrown.Contains('__PARTNER_CENTER_PUBLISHER_DISPLAY_NAME__')) 'open placeholders not named'
    Assert-True (-not $thrown.Contains('Secret Co')) 'a value was repeated'
}

Check 'the real Store manifest in the repository fails until Partner Center values are given, and passes with them' {
    $repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    $store = Get-Content -LiteralPath (Join-Path $repo 'src/TimeTug.App/Package.Store.appxmanifest') -Raw
    Assert-Throws { New-ReleaseManifestText $store '1.2.3.0' @{} } 'Partner Center values are missing'
    $text = New-ReleaseManifestText $store '1.2.3.0' $filled
    [xml]$null = $text
    $direct = Get-Content -LiteralPath (Join-Path $repo 'src/TimeTug.App/Package.Direct.appxmanifest') -Raw
    $directText = New-ReleaseManifestText $direct '1.2.3.0' @{}
    Assert-True ($directText.Contains('Version="1.2.3.0"')) 'direct version'
}

Check 'a dry run uses fake ids that cannot be mistaken for real ones' {
    foreach ($value in $script:DryRunOAuth.Values) { Assert-True ($value -match 'dryrun|d0d0') "'$value' does not look fake" }
    Assert-True ($script:DryRunIdentity.Publisher -ne 'CN=TimeTug Development') 'the dry run must not use the development publisher'
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) failed"; exit 1 }
Write-Host 'all passed'
