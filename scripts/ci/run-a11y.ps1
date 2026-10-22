<#
.SYNOPSIS
  Scans the welcome window and the tug window of the installed development package with axe-windows.
.DESCRIPTION
  Starts the scanner (tests/TimeTug.App.A11y), then the app with --selftest a11y-hold. The scenario opens the welcome window and the
  tug, holds them while the scanner reads them (twelve rounds: each step of the welcome window (the second with its launch-at-login checkbox), the tug as it appears and with the link
  to copy, the flyout, the Settings window with its search results, the Tug Rules pane, the Calendars pane, the Accounts pane, the General pane, the About page, the Appearance pane in the light and in the dark theme, the flyout with its merge menu open), and finishes when the scanner has. Fails when the scanner reports an error-level rule violation (after the accepted ones in
  tests/TimeTug.App.A11y/suppressions.json), when a round could not be scanned, or when the scenario's own checks fail. The
  report (a11y-report.txt and .json, plus the .a11ytest files Accessibility Insights opens) goes to the output folder.
  Needs the package installed for this user and an interactive session, like run-selftest.ps1.
.PARAMETER Configuration
  The configuration the solution was built in (CI by default; Debug also works).
.PARAMETER OutputFolder
  Default: build\a11y under the repository.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'CI')]
    [string]$Configuration = 'CI',
    [string]$OutputFolder
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutputFolder) { $OutputFolder = Join-Path $repo 'build\a11y' }
$OutputFolder = [System.IO.Path]::GetFullPath($OutputFolder)
$alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\timetug-dev.exe'
$rounds = 12   # the same number as A11yHoldScenario.Rounds
$scanner = Get-ChildItem -Path (Join-Path $repo "tests\TimeTug.App.A11y\bin\$Configuration") -Recurse -Filter 'TimeTug.App.A11y.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $scanner) {
    Write-Host 'building the scanner'
    dotnet build (Join-Path $repo 'tests\TimeTug.App.A11y') -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "the scanner did not build ($LASTEXITCODE)" }
    $scanner = Get-ChildItem -Path (Join-Path $repo "tests\TimeTug.App.A11y\bin\$Configuration") -Recurse -Filter 'TimeTug.App.A11y.exe' | Select-Object -First 1
}
if (-not (Test-Path $alias)) { throw "execution alias not found: $alias (is the package installed for this user?)" }
if (Test-Path $OutputFolder) { Remove-Item -Recurse -Force $OutputFolder }
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null

$clock = [System.Diagnostics.Stopwatch]::StartNew()
$scan = Start-Process -FilePath $scanner.FullName -ArgumentList @('--out', "`"$OutputFolder`"", '--rounds', "$rounds", '--wait', '120') -PassThru -NoNewWindow
$app = Start-Process -FilePath $alias -ArgumentList @('--selftest', 'a11y-hold', '--out', "`"$OutputFolder`"", '--hold', '420') -PassThru
if (-not $scan.WaitForExit(300000)) { Stop-Process -Id $scan.Id -Force -ErrorAction SilentlyContinue; $scanExit = -1 } else { $scanExit = $scan.ExitCode }

$resultFile = Join-Path $OutputFolder 'result.json'
$deadline = [DateTime]::UtcNow.AddSeconds(60)
while (-not (Test-Path $resultFile) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (-not $app.WaitForExit(15000)) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
Get-Process -Name 'TimeTug' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host ("a11y run took {0:n1}s" -f $clock.Elapsed.TotalSeconds)

$failures = @()
if ($scanExit -eq 1) { $failures += 'axe-windows reported error-level violations (see a11y-report.txt)' }
elseif ($scanExit -ne 0) { $failures += "the scanner did not finish cleanly (exit $scanExit)" }
if (-not (Test-Path $resultFile)) { $failures += 'the a11y-hold scenario left no result.json' }
else {
    $result = Get-Content -Raw $resultFile | ConvertFrom-Json
    if ($result.ok -ne $true) { $failures += "the a11y-hold scenario failed: $($result.error)" }
}
$report = Join-Path $OutputFolder 'a11y-report.txt'
if (Test-Path $report) { Get-Content $report | ForEach-Object { Write-Host $_ } } else { $failures += 'no a11y-report.txt was written' }
if ($failures.Count -gt 0) { throw ('accessibility check failed: ' + ($failures -join '; ')) }
Write-Host 'accessibility check passed'
