# Shared by the release scripts (dot-sourced): the rule that turns a release tag into a package version.

# 'v1.2.3' or '1.2.3' -> @{ Semantic = '1.2.3'; Package = '1.2.3.0'; Suffix = '' }. A suffix ('-beta.1') is allowed only with -Prerelease, and -Prerelease needs one.
function ConvertTo-PackageVersion([string]$Text, [switch]$Prerelease) {
    if (-not $Text) { throw 'a version is required: 1.2.3 or v1.2.3' }
    if ($Text -notmatch '^v?(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})(-[0-9A-Za-z][0-9A-Za-z.-]*)?\z') {
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
