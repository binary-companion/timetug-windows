<#
.SYNOPSIS
  Launches the installed development package with --selftest and checks the result.
.DESCRIPTION
  Starts the app through its execution alias (so it keeps package identity), waits a bounded time,
  then fails unless selftest-result.json says ok, the process had package identity, and the
  screenshot is not blank (more than one distinct colour). Must run in an interactive session.
#>
[CmdletBinding()]
param(
    [string]$Scenario = 'hello',
    # Screenshots the scenario must produce (non-blank). Defaults to the scenario's own list.
    [string[]]$Screenshots,
    [string]$OutputFolder,
    [int]$TimeoutSeconds = 90,
    # Keeps the first thing the scenario shows on screen this long (a screen capture or a person looks at it).
    [int]$HoldSeconds = 0
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutputFolder) { $OutputFolder = Join-Path $repo 'build\selftest' }
$OutputFolder = [System.IO.Path]::GetFullPath($OutputFolder)
$alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\timetug-dev.exe'
$resultFile = Join-Path $OutputFolder 'selftest-result.json'
if (-not $Screenshots) {
    $Screenshots = switch ($Scenario) {
        'welcome' { @('welcome-1-connect.png', 'welcome-waiting.png', 'welcome-form.png', 'welcome-error.png', 'welcome-2-connected.png') }
        'toast' { @() }
        'tug-basic' { @('tug-basic-primary.png', 'tug-basic-mirror.png', 'tug-basic-joinfailed.png', 'tug-basic-nofocus.png', 'tug-basic-solid.png', 'tug-basic-contrast.png') }
        default { @("$Scenario.png") }
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

if (-not (Test-Path $alias)) { throw "execution alias not found: $alias (is the package installed for this user?)" }
if (Test-Path $OutputFolder) { Remove-Item -Recurse -Force $OutputFolder }
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null

$clock = [System.Diagnostics.Stopwatch]::StartNew()
$arguments = @('--selftest', $Scenario, '--out', "`"$OutputFolder`"")
if ($HoldSeconds -gt 0) { $arguments += @('--hold', "$HoldSeconds") }
$process = Start-Process -FilePath $alias -ArgumentList $arguments -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds + $HoldSeconds)
while (-not (Test-Path $resultFile) -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 500
}
Write-Host ("self-test finished waiting after {0:n1}s" -f $clock.Elapsed.TotalSeconds)

# The app exits itself after writing the result; make sure nothing is left behind either way.
if (-not $process.WaitForExit(15000)) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
Get-Process -Name 'TimeTug' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (-not (Test-Path $resultFile)) { throw "no selftest-result.json after $TimeoutSeconds seconds" }
$result = Get-Content -Raw $resultFile | ConvertFrom-Json
Write-Host (Get-Content -Raw $resultFile)

$failures = @()
if ($result.ok -ne $true) { $failures += "ok is not true: $($result.error)" }
if (-not $result.packageFullName) { $failures += 'the app ran without package identity' }
foreach ($name in $Screenshots) {
    $shot = Join-Path $OutputFolder $name
    if (-not (Test-Path $shot)) { $failures += "$name is missing"; continue }
    $colours = Get-DistinctColours $shot
    Write-Host "$name distinct colours (sampled): $colours"
    if ($colours -le 1) { $failures += "$name is blank" }
}
foreach ($check in @($result.checks)) { if ($check -like 'FAIL:*') { $failures += $check } }
if ($failures.Count -gt 0) { throw ('self-test failed: ' + ($failures -join '; ')) }
Write-Host 'self-test passed'
