<#
.SYNOPSIS
  Fails unless the commit a release is built from is part of the default branch's history.
.DESCRIPTION
  The release build jobs hold the OAuth secrets and run whatever the checked-out ref contains, and the owner approves a run in the Actions page
  by environment, not by commit. So a tag that points at a commit nobody merged (a branch pushed by someone with write access) must not build:
  the ref has to be the default branch itself or an ancestor of its tip. Asks the GitHub compare API (needs GH_TOKEN and GITHUB_REPOSITORY).
.PARAMETER Ref
  The tag or branch name the release builds.
.PARAMETER DefaultBranch
  The repository's default branch.
#>
[CmdletBinding()]
param(
    [string]$Ref,
    [string]$DefaultBranch = 'main'
)

$ErrorActionPreference = 'Stop'

# The compare API's status for "<default>...<ref>": identical (the ref is the tip) or behind (the ref is an ancestor of the tip) are fine;
# ahead and diverged mean the ref has commits the default branch does not.
function Test-OnDefaultBranch([string]$CompareStatus) {
    return $CompareStatus -in 'identical', 'behind'
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $Ref) { throw 'no ref to check' }
    $status = gh api "repos/$($env:GITHUB_REPOSITORY)/compare/$DefaultBranch...$Ref" --jq '.status'
    if ($LASTEXITCODE -ne 0) { throw "could not compare $Ref with $DefaultBranch" }
    if (-not (Test-OnDefaultBranch ([string]$status).Trim())) {
        throw "$Ref is not part of the history of $DefaultBranch (compare status '$status'); a release is built only from commits that were merged"
    }
    Write-Host "$Ref is part of the history of $DefaultBranch."
}
