<#
.SYNOPSIS
  Checklist item 8.1 by machine: starts the installed TimeTug package again and again, watches each start for a window titled "TimeTug couldn't
  start", and quits it from its real tray menu between starts. It never dismisses or kills anything.
.DESCRIPTION
  Needs the signed-in user's desktop session (UI Automation, the tray and the mouse). Over SSH run it through run-in-user-session.ps1.
  For every cycle: start the app through its execution alias, watch -WatchSeconds for the dialog (UI Automation top-level windows and the title of
  every process window), check that the process is still alive and that the app's log has no new line "TimeTug could not start.", then quit through
  the tray (open the hidden icons, right-click the TimeTug icon, click Quit TimeTug at the bottom of the menu) and check that the process is gone.

  If the dialog appears it is LEFT OPEN: the script prints the dialog's title and every text in it, the log lines "TimeTug could not start. <reason>"
  of that start (the reason is the exception type and message and is the whole diagnosis), and stops with exit code 9. If a start leaves no process
  and no dialog the exit code is 5, if the tray quit fails 6 (the app is left running), if the app is already running 4. Exit code 0: every cycle was
  clean. Only the app's own failure lines are read from its log; no other line, no account data and no calendar content is read or printed.

  The tray menu's items are not visible to UI Automation (the menu is drawn by the app's tray library), so Quit, the last item, is clicked by its
  place at the bottom of the menu, scaled by the system DPI. -Quit None leaves the quitting to a person (the script waits for the app to go).
.PARAMETER Cycles
  How many start and quit cycles. Default 10.
.PARAMETER WatchSeconds
  How long to watch each start. Default 12.
.PARAMETER Quit
  Tray (default) or None.
.PARAMETER Tag
  A word printed in front of each line. Default cycle.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 200)][int]$Cycles = 10,
    [ValidateRange(3, 120)][int]$WatchSeconds = 12,
    [ValidateSet('Tray', 'None')][string]$Quit = 'Tray',
    [string]$Tag = 'cycle'
)

$ErrorActionPreference = 'Stop'
$script:PackageName = 'BinaryCompanion.TimeTug.Dev'
$script:FailureLine = 'TimeTug could not start.'
# The dialog's title is "TimeTug couldn't start": the apostrophe may be straight or curly, so any one character matches.
$script:DialogPattern = 'couldn.t start'

# ---- pure helpers (tested in scripts/vm/tests/start-cycles.tests.ps1) -------------------------------------------------------------------------

function Test-DialogTitle([string]$Title) { return [bool]($Title -match $script:DialogPattern) }

# Lines of the log at or after byte $FromByte that say the app could not start.
function Get-StartFailureLine([string]$LogPath, [int64]$FromByte) {
    if (-not (Test-Path -LiteralPath $LogPath)) { return @() }
    $stream = [IO.File]::Open($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        if ($FromByte -gt 0 -and $FromByte -le $stream.Length) { $null = $stream.Seek($FromByte, [IO.SeekOrigin]::Begin) }
        $reader = New-Object IO.StreamReader($stream)
        $found = New-Object System.Collections.Generic.List[string]
        while (($line = $reader.ReadLine()) -ne $null) { if ($line.Contains($script:FailureLine)) { $found.Add($line) } }
        return @($found)
    }
    finally { $stream.Dispose() }
}

# Where to click for the last item of the tray menu: 70 px in from the menu's left edge and 13 px up from its bottom edge at 96 dpi, scaled by dpi/96.
function Get-QuitClickPoint([double]$MenuLeft, [double]$MenuTop, [double]$MenuWidth, [double]$MenuHeight, [int]$Dpi) {
    $scale = [Math]::Max(1.0, $Dpi / 96.0)
    $x = [int][Math]::Floor($MenuLeft + [Math]::Min(70 * $scale, $MenuWidth - 4))
    $y = [int][Math]::Floor($MenuTop + $MenuHeight - 13 * $scale)
    return [pscustomobject]@{ X = $x; Y = $y }
}

# True for a tray button whose tooltip is the TimeTug state ("No more meetings today", a countdown, ...) or names the app.
function Test-TrayIconName([string]$Name) { return [bool]($Name -match 'meeting|TimeTug|Tug|Next') }

if ($MyInvocation.InvocationName -eq '.') { return }

# ---- Windows side ---------------------------------------------------------------------------------------------------------------------------

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class CycleMouse {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  public static void Click(int x, int y, bool right) { SetCursorPos(x, y); mouse_event(right ? 0x8u : 0x2u, 0, 0, 0, UIntPtr.Zero); mouse_event(right ? 0x10u : 0x4u, 0, 0, 0, UIntPtr.Zero); }
}
"@
[void][CycleMouse]::SetProcessDPIAware()
$A = [Windows.Automation.AutomationElement]
$root = $A::RootElement
$all = [Windows.Automation.Condition]::TrueCondition
$alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\timetug-dev.exe'
$package = Get-AppxPackage -Name $script:PackageName
if (-not $package) { throw "the package $($script:PackageName) is not installed for this user" }
if (-not (Test-Path -LiteralPath $alias)) { throw "execution alias not found: $alias" }
$log = Join-Path $env:LOCALAPPDATA ("Packages\{0}\LocalState\logs\timetug.log" -f $package.PackageFamilyName)

function Get-LogLength { if (Test-Path -LiteralPath $log) { return (Get-Item -LiteralPath $log).Length } else { return 0 } }

function Find-Dialog {
    foreach ($window in $root.FindAll('Children', $all)) { if (Test-DialogTitle $window.Current.Name) { return $window } }
    foreach ($process in Get-Process -ErrorAction SilentlyContinue) { if (Test-DialogTitle $process.MainWindowTitle) { return $process } }
    return $null
}

function Stop-OnDialog($Dialog, [int]$Number, [int64]$From) {
    Write-Host "[$Tag $Number] DIALOG FOUND (left open: nothing was dismissed or killed)"
    if ($Dialog -is [Windows.Automation.AutomationElement]) {
        Write-Host ("dialog title: [{0}]" -f $Dialog.Current.Name)
        foreach ($element in $Dialog.FindAll('Descendants', $all)) { if ($element.Current.Name) { Write-Host ("dialog text: {0}: [{1}]" -f $element.Current.ControlType.ProgrammaticName, $element.Current.Name) } }
    }
    else { Write-Host ("dialog title (process window): [{0}]" -f $Dialog.MainWindowTitle) }
    $lines = @(Get-StartFailureLine $log $From)
    foreach ($line in $lines) { Write-Host "log: $line" }
    Write-Host ("log lines '{0}' since this start: {1}" -f $script:FailureLine, $lines.Count)
    exit 9
}

function Get-Overflow { return $root.FindAll('Children', $all) | Where-Object { $_.Current.ClassName -eq 'TopLevelWindowForOverflowXamlIsland' } | Select-Object -First 1 }

function Get-TrayIcon {
    $taskbar = $root.FindFirst('Children', (New-Object Windows.Automation.PropertyCondition($A::ClassNameProperty, 'Shell_TrayWnd')))
    if (-not $taskbar) { return $null }
    $buttons = @($taskbar.FindAll('Descendants', $all) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and $_.Current.BoundingRectangle.Width -gt 0 })
    # Pinned taskbar buttons are named "<App> pinned" / "<n> running window(s)"; the tray icon of the app is not among them.
    $direct = $buttons | Where-Object { (Test-TrayIconName $_.Current.Name) -and $_.Current.Name -notmatch 'pinned|running window' } | Select-Object -First 1
    if ($direct) { return $direct }
    $overflow = Get-Overflow
    if (-not $overflow) {
        $chevron = $buttons | Where-Object { $_.Current.Name -like 'Show Hidden Icons*' } | Select-Object -First 1
        if (-not $chevron) { return $null }
        ($chevron.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
        Start-Sleep -Milliseconds 900
        $overflow = Get-Overflow
    }
    if (-not $overflow) { return $null }
    return $overflow.FindAll('Descendants', $all) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and (Test-TrayIconName $_.Current.Name) } | Select-Object -First 1
}

# Quits TimeTug through the tray menu. Returns a status word: gone, no-icon, no-menu or still-running.
function Invoke-TrayQuit {
    $menu = $null
    for ($attempt = 1; $attempt -le 4 -and -not $menu; $attempt++) {
        $icon = Get-TrayIcon
        if (-not $icon) { return 'no-icon' }
        $r = $icon.Current.BoundingRectangle
        [CycleMouse]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2), $true)
        for ($i = 0; $i -lt 8 -and -not $menu; $i++) {
            Start-Sleep -Milliseconds 400
            $menu = $root.FindAll('Children', $all) | Where-Object { $_.Current.ClassName -eq '#32768' } | Select-Object -First 1
        }
    }
    if (-not $menu) { return 'no-menu' }
    $m = $menu.Current.BoundingRectangle
    $point = Get-QuitClickPoint $m.X $m.Y $m.Width $m.Height ([int][CycleMouse]::GetDpiForSystem())
    [CycleMouse]::Click($point.X, $point.Y, $false)
    $waited = 0
    while ((Get-Process -Name TimeTug -ErrorAction SilentlyContinue) -and $waited -lt 20) { Start-Sleep -Seconds 1; $waited++ }
    if (Get-Process -Name TimeTug -ErrorAction SilentlyContinue) { return 'still-running' }
    return 'gone'
}

for ($n = 1; $n -le $Cycles; $n++) {
    if (Get-Process -Name TimeTug -ErrorAction SilentlyContinue) { Write-Host "[$Tag $n] TimeTug is already running; not starting another"; exit 4 }
    $from = Get-LogLength
    $started = Get-Date
    Start-Process -FilePath $alias
    while (((Get-Date) - $started).TotalSeconds -lt $WatchSeconds) {
        Start-Sleep -Milliseconds 400
        $dialog = Find-Dialog
        if ($dialog) { Stop-OnDialog $dialog $n $from }
    }
    $alive = [bool](Get-Process -Name TimeTug -ErrorAction SilentlyContinue)
    $failures = @(Get-StartFailureLine $log $from)
    Write-Host ("[$Tag $n] started; process alive after {0}s: {1}; dialog: none; '{2}' log lines: {3}" -f $WatchSeconds, $alive, $script:FailureLine, $failures.Count)
    foreach ($line in $failures) { Write-Host "log: $line" }
    if (-not $alive) { Write-Host "[$Tag $n] no process and no dialog: the app exited by itself"; exit 5 }

    if ($Quit -eq 'Tray') {
        $status = Invoke-TrayQuit
        Write-Host "[$Tag $n] tray quit: $status"
        $late = Find-Dialog
        if ($late) { Stop-OnDialog $late $n $from }
        if ($status -ne 'gone') { Write-Host "[$Tag $n] the app is still running; stopping here without killing it"; exit 6 }
    }
    else {
        Write-Host "[$Tag $n] quit TimeTug from the tray yourself (waiting up to 120 s)"
        $waited = 0
        while ((Get-Process -Name TimeTug -ErrorAction SilentlyContinue) -and $waited -lt 120) { Start-Sleep -Seconds 1; $waited++ }
        if (Get-Process -Name TimeTug -ErrorAction SilentlyContinue) { Write-Host "[$Tag $n] still running"; exit 6 }
    }
    Start-Sleep -Seconds 2
}
Write-Host "[$Tag] all $Cycles cycles done: no dialog, every start alive, no '$($script:FailureLine)' line"
