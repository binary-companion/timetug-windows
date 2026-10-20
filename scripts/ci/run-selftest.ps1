<#
.SYNOPSIS
  Runs the self-test scenarios against the installed development package and checks every result.
.DESCRIPTION
  For each scenario in scripts/ci/selftest-scenarios.txt (or the one named), starts the app through its execution alias
  (so it keeps package identity) with --selftest, waits a bounded time for result.json, and fails unless:
    - the result file exists (a scenario that crashed or hung leaves none),
    - "ok" is true, no check is false and the app had package identity,
    - the scenario produced at least its minimum number of screenshots and none is blank.
  Every scenario is run even after one fails, so the artifacts are complete; the script throws at the end. Must run in an
  interactive session. Scenarios run by the accessibility script (a11y) are skipped unless named.
.PARAMETER Scenario
  One scenario name, or 'all' (default): every scenario run by this script.
.PARAMETER OutputFolder
  Where each scenario's folder (result.json and its numbered PNGs) goes. Default: build\selftest under the repository.
.PARAMETER HoldSeconds
  Keeps the first thing a scenario shows on screen this long (a screen capture or a person looks at it). Single scenario only.
#>
[CmdletBinding()]
param(
    [string]$Scenario = 'all',
    [string]$OutputFolder,
    [int]$HoldSeconds = 0
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutputFolder) { $OutputFolder = Join-Path $repo 'build\selftest' }
$OutputFolder = [System.IO.Path]::GetFullPath($OutputFolder)
$alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\timetug-dev.exe'

function Read-ScenarioList {
    Get-Content (Join-Path $PSScriptRoot 'selftest-scenarios.txt') |
        Where-Object { $_ -and -not $_.TrimStart().StartsWith('#') } |
        ForEach-Object {
            $fields = $_ -split '\s+' | Where-Object { $_ }
            [pscustomobject]@{ Name = $fields[0]; MinShots = [int]$fields[1]; TimeoutSeconds = [int]$fields[2]; Runner = $fields[3] }
        }
}

function Get-DistinctColours([string]$Path, [int]$Limit = 50) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($Path)
    try {
        $seen = [System.Collections.Generic.HashSet[int]]::new()
        $stepX = [Math]::Max(1, [int]($bitmap.Width / 200))
        $stepY = [Math]::Max(1, [int]($bitmap.Height / 200))
        for ($y = 0; $y -lt $bitmap.Height -and $seen.Count -lt $Limit; $y += $stepY) {
            for ($x = 0; $x -lt $bitmap.Width -and $seen.Count -lt $Limit; $x += $stepX) {
                [void]$seen.Add($bitmap.GetPixel($x, $y).ToArgb())
            }
        }
        return $seen.Count
    }
    finally { $bitmap.Dispose() }
}

# Returns the problems found in a finished scenario folder (empty when it passed).
function Test-ScenarioResult($Entry, [string]$Folder) {
    $problems = @()
    $resultFile = Join-Path $Folder 'result.json'
    if (-not (Test-Path $resultFile)) { return @("no result.json after $($Entry.TimeoutSeconds) seconds (the app crashed, hung or never started)") }
    $result = Get-Content -Raw $resultFile | ConvertFrom-Json
    if ($result.scenario -ne $Entry.Name) { $problems += "result.json is for scenario '$($result.scenario)'" }
    if ($result.ok -ne $true) { $problems += "ok is not true: $($result.error)" }
    if (-not $result.packageFullName) { $problems += 'the app ran without package identity' }
    foreach ($check in @($result.checks | Where-Object { $_ })) { if ($check.ok -ne $true) { $problems += "check failed: $($check.name)" } }
    $shots = @($result.screenshots | Where-Object { $_ })
    if ($shots.Count -lt $Entry.MinShots) { $problems += "expected at least $($Entry.MinShots) screenshot(s), found $($shots.Count)" }
    foreach ($shot in $shots) {
        $path = Join-Path $Folder $shot.file
        if (-not (Test-Path $path)) { $problems += "$($shot.file) is missing"; continue }
        $colours = Get-DistinctColours $path
        if ($colours -le 1) { $problems += "$($shot.file) is blank" }
    }
    return $problems
}

if (-not (Test-Path $alias)) { throw "execution alias not found: $alias (is the package installed for this user?)" }
$all = Read-ScenarioList
$selected = @(if ($Scenario -eq 'all') { $all | Where-Object Runner -eq 'selftest' } else { $all | Where-Object Name -eq $Scenario })
if ($selected.Count -eq 0) { throw "unknown scenario '$Scenario'; see scripts/ci/selftest-scenarios.txt" }
if (Test-Path $OutputFolder) { Remove-Item -Recurse -Force $OutputFolder }
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null

$failed = @()
$total = [System.Diagnostics.Stopwatch]::StartNew()
foreach ($entry in $selected) {
    $folder = Join-Path $OutputFolder $entry.Name
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $resultFile = Join-Path $folder 'result.json'
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $arguments = @('--selftest', $entry.Name, '--out', "`"$folder`"")
    if ($HoldSeconds -gt 0) { $arguments += @('--hold', "$HoldSeconds") }
    $process = Start-Process -FilePath $alias -ArgumentList $arguments -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($entry.TimeoutSeconds + $HoldSeconds)
    while (-not (Test-Path $resultFile) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
    $seconds = $clock.Elapsed.TotalSeconds

    # The app exits itself after writing the result; make sure nothing is left behind either way.
    if (-not $process.WaitForExit(15000)) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Get-Process -Name 'TimeTug' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    $problems = @(Test-ScenarioResult $entry $folder)
    if ($problems.Count -eq 0) {
        $result = Get-Content -Raw $resultFile | ConvertFrom-Json
        Write-Host ("PASS {0,-24} {1,3} checks, {2} screenshot(s), {3:n1}s" -f $entry.Name, @($result.checks | Where-Object { $_ }).Count, @($result.screenshots | Where-Object { $_ }).Count, $seconds)
    }
    else {
        Write-Host ("FAIL {0,-24} {1:n1}s" -f $entry.Name, $seconds)
        $problems | ForEach-Object { Write-Host "       - $_" }
        $failed += $entry.Name
    }
}
Write-Host ("{0} scenario(s) in {1:n0}s" -f $selected.Count, $total.Elapsed.TotalSeconds)
if ($failed.Count -gt 0) { throw ('self-test failed: ' + ($failed -join ', ')) }
Write-Host 'self-test passed'
