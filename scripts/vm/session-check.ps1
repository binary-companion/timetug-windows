<#
.SYNOPSIS
  Checks that a running TimeTug survives a real sleep and resume (with a scheduled wake) or a real lock and unlock, in the signed-in user's
  desktop session. Prints numbers and verdicts; reads no account data, no calendar content and no log line except the app's own start failures.
.DESCRIPTION
  Needs the signed-in user's desktop session. Over SSH run it through run-in-user-session.ps1 (use -Detach: the SSH connection may drop while
  the PC sleeps, the job in the session keeps going; read it afterwards with -Collect).

  Mode Sleep (item 8.7 without a meeting): starts TimeTug if it is not running (and leaves it running), records its state, registers a one-shot
  scheduled task that wakes the PC after -WakeAfterMinutes (needs "Allow wake timers" in the power plan: scripts\vm\power-settings.ps1 -Action Apply,
  run elevated by the owner), puts the PC to sleep (SetSuspendState, sleep not hibernate), and after the wake waits -SettleSeconds and records the
  state again. A PC that never wakes by itself needs a person to press a key; a task that never fired is reported.
  Mode Lock (item 8.6 without a meeting): the same with a lock (LockWorkStation) instead of a sleep; a PERSON unlocks the PC with their password,
  the script waits (up to -UnlockTimeoutMinutes) until the lock screen is gone, waits -SettleSeconds and records the state again.

  The state: is the process the same one as before (not restarted), does it respond, is a window titled "TimeTug couldn't start" up, does the app's log
  say "TimeTug could not start." since the start of this check, CPU seconds used while settling and working set. Verdict: PASS only when the same
  process is alive and responding, no dialog, no failure line, CPU used while settling within -CpuBudgetSeconds.
  -DryRun does everything except the sleep or the lock (waits -WakeAfterMinutes minutes instead, or 20 s for Lock): it tests the script itself.
.PARAMETER Mode
  Sleep or Lock.
.PARAMETER WakeAfterMinutes
  Sleep: minutes until the scheduled wake. Default 3 (the task needs at least a minute of margin to be registered).
.PARAMETER SettleSeconds
  Seconds to wait after the wake or unlock before the second state. Default 60.
.PARAMETER CpuBudgetSeconds
  CPU seconds the app may use during the settle time. Default 5.
.PARAMETER UnlockTimeoutMinutes
  Lock: how long to wait for the person to unlock. Default 10.
.PARAMETER OutFile
  Where the JSON result goes. Default: %LOCALAPPDATA%\TimeTug-session-check\<mode>-<time>.json
.PARAMETER DryRun
  Do not sleep or lock.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Sleep', 'Lock')][string]$Mode,
    [ValidateRange(1, 60)][int]$WakeAfterMinutes = 3,
    [ValidateRange(5, 600)][int]$SettleSeconds = 60,
    [ValidateRange(0, 600)][int]$CpuBudgetSeconds = 5,
    [ValidateRange(1, 120)][int]$UnlockTimeoutMinutes = 10,
    [string]$OutFile,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$script:PackageName = 'BinaryCompanion.TimeTug.Dev'
$script:FailureLine = 'TimeTug could not start.'

# ---- pure helpers (tested in scripts/vm/tests/session-check.tests.ps1) ------------------------------------------------------------------------

# The problems between the state before and after (empty = pass). States are objects with ProcessId, Responding, CpuSeconds, WorkingSetMB.
function Test-StateTransition($Before, $After, [int]$CpuSecondsWhileSettling, [int]$DialogWindows, [int]$FailureLines, [int]$CpuBudgetSeconds) {
    $problems = New-Object System.Collections.Generic.List[string]
    if (-not $Before) { $problems.Add('TimeTug was not running before') }
    if (-not $After) { $problems.Add('TimeTug is not running after') }
    elseif ($Before -and $After.ProcessId -ne $Before.ProcessId) { $problems.Add("TimeTug was restarted (process $($Before.ProcessId) before, $($After.ProcessId) after)") }
    if ($After -and -not $After.Responding) { $problems.Add('TimeTug does not respond') }
    if ($DialogWindows -gt 0) { $problems.Add("a window titled TimeTug couldn't start is up ($DialogWindows)") }
    if ($FailureLines -gt 0) { $problems.Add("the log has $FailureLines '$($script:FailureLine)' line(s)") }
    if ($CpuSecondsWhileSettling -gt $CpuBudgetSeconds) { $problems.Add("busy after the event: $CpuSecondsWhileSettling CPU s while settling (budget $CpuBudgetSeconds)") }
    return @($problems)
}

# How the wake gap looks: the PC slept at $SleepAt and was running again at $ResumedAt; the wake was due $WakeAfterMinutes after the sleep.
# Returns the gap in seconds and whether the resume came near the planned wake (within a minute early or 10 minutes late).
function Get-WakeGap([datetime]$SleepAt, [datetime]$ResumedAt, [int]$WakeAfterMinutes) {
    $gap = [int]($ResumedAt - $SleepAt).TotalSeconds
    $planned = $WakeAfterMinutes * 60
    return [pscustomobject]@{ GapSeconds = $gap; PlannedSeconds = $planned; NearPlanned = ($gap -ge ($planned - 60) -and $gap -le ($planned + 600)) }
}

if ($MyInvocation.InvocationName -eq '.') { return }

# ---- Windows side ---------------------------------------------------------------------------------------------------------------------------

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SessionNative {
  [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);
  [DllImport("user32.dll")] public static extern bool LockWorkStation();
}
"@
$root = [Windows.Automation.AutomationElement]::RootElement
$all = [Windows.Automation.Condition]::TrueCondition
$package = Get-AppxPackage -Name $script:PackageName
if (-not $package) { throw "the package $($script:PackageName) is not installed for this user" }
$alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\timetug-dev.exe'
$log = Join-Path $env:LOCALAPPDATA ("Packages\{0}\LocalState\logs\timetug.log" -f $package.PackageFamilyName)
if (-not $OutFile) {
    $folder = Join-Path $env:LOCALAPPDATA 'TimeTug-session-check'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $OutFile = Join-Path $folder ('{0}-{1}.json' -f $Mode.ToLowerInvariant(), (Get-Date).ToString('yyyyMMdd-HHmmss'))
}

function Say([string]$Text) { Write-Host ("[session-check] {0}  {1}" -f (Get-Date).ToString('HH:mm:ss'), $Text) }
function Get-LogLength { if (Test-Path -LiteralPath $log) { return (Get-Item -LiteralPath $log).Length } else { return 0 } }

function Get-FailureLineCount([int64]$From) {
    if (-not (Test-Path -LiteralPath $log)) { return 0 }
    $stream = [IO.File]::Open($log, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        if ($From -gt 0 -and $From -le $stream.Length) { $null = $stream.Seek($From, [IO.SeekOrigin]::Begin) }
        $reader = New-Object IO.StreamReader($stream)
        $count = 0
        while (($line = $reader.ReadLine()) -ne $null) { if ($line.Contains($script:FailureLine)) { $count++ } }
        return $count
    }
    finally { $stream.Dispose() }
}

function Get-DialogCount {
    $count = 0
    foreach ($window in $root.FindAll('Children', $all)) { if ($window.Current.Name -match 'couldn.t start') { $count++ } }
    foreach ($process in Get-Process -ErrorAction SilentlyContinue) { if ($process.MainWindowTitle -match 'couldn.t start') { $count++ } }
    return $count
}

function Get-AppState {
    $process = Get-Process -Name TimeTug -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $process) { return $null }
    return [pscustomobject]@{ ProcessId = $process.Id; Responding = [bool]$process.Responding; CpuSeconds = [double]$process.CPU; WorkingSetMB = [int]($process.WorkingSet64 / 1MB) }
}

function Test-Locked { return [bool](Get-Process -Name LogonUI -ErrorAction SilentlyContinue) }

$logFrom = Get-LogLength
if (-not (Get-AppState)) {
    Say 'TimeTug is not running: starting it (it stays running)'
    Start-Process -FilePath $alias
    Start-Sleep -Seconds 15
}
$before = Get-AppState
if (-not $before) { throw 'TimeTug did not start' }
Say ("before: process {0}, responding {1}, CPU {2:N1} s, {3} MB" -f $before.ProcessId, $before.Responding, $before.CpuSeconds, $before.WorkingSetMB)

$startedAt = Get-Date
$wake = $null
$taskName = 'TimeTug-session-check-wake'
if ($Mode -eq 'Sleep') {
    $stamp = Join-Path $env:LOCALAPPDATA 'TimeTug-session-check\wake-stamp.txt'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $stamp) | Out-Null
    if (Test-Path -LiteralPath $stamp) { Remove-Item -LiteralPath $stamp -Force }
    if (-not $DryRun) {
        $at = (Get-Date).AddMinutes($WakeAfterMinutes)
        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ("-NoProfile -WindowStyle Hidden -Command `"Set-Content -LiteralPath '{0}' -Value (Get-Date -Format o)`"" -f $stamp)
        $trigger = New-ScheduledTaskTrigger -Once -At $at
        $settings = New-ScheduledTaskSettingsSet -WakeToRun -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
        Say ("wake task registered for {0}; sleeping now (sleep, not hibernate)" -f $at.ToString('HH:mm:ss'))
        Start-Sleep -Seconds 3
        $sleepAt = Get-Date
        [void][SessionNative]::SetSuspendState($false, $false, $false)
        # The next statements run after the PC is awake again.
        $resumedAt = Get-Date
        $wake = Get-WakeGap $sleepAt $resumedAt $WakeAfterMinutes
        Say ("awake again after {0} s (planned {1} s, near the plan: {2})" -f $wake.GapSeconds, $wake.PlannedSeconds, $wake.NearPlanned)
    }
    else { Say ("dry run: no sleep; waiting {0} minute(s) instead" -f $WakeAfterMinutes); Start-Sleep -Seconds ($WakeAfterMinutes * 60) }
}
else {
    if (-not $DryRun) {
        Say 'locking the PC now; unlock it with your password'
        [void][SessionNative]::LockWorkStation()
        Start-Sleep -Seconds 5
        $deadline = (Get-Date).AddMinutes($UnlockTimeoutMinutes)
        while ((Test-Locked) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
        if (Test-Locked) { throw "still locked after $UnlockTimeoutMinutes minutes" }
        Say 'unlocked'
    }
    else { Say 'dry run: no lock; waiting 20 s instead'; Start-Sleep -Seconds 20 }
}

Say ("settling for {0} s" -f $SettleSeconds)
$cpuStart = (Get-AppState).CpuSeconds
Start-Sleep -Seconds $SettleSeconds
$after = Get-AppState
$cpuWhile = 0
if ($after -and $cpuStart -ne $null) { $cpuWhile = [int][Math]::Max(0, $after.CpuSeconds - $cpuStart) }
$dialogs = Get-DialogCount
$failures = Get-FailureLineCount $logFrom
$wakeFired = $null
if ($Mode -eq 'Sleep' -and -not $DryRun) {
    $wakeFired = Test-Path -LiteralPath $stamp
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
}
$problems = @(Test-StateTransition $before $after $cpuWhile $dialogs $failures $CpuBudgetSeconds)
if ($Mode -eq 'Sleep' -and -not $DryRun) {
    if ($wakeFired -eq $false) { $problems += 'the wake task did not run (wake timers off in the power plan, or the PC was woken by hand)' }
    if ($wake -and -not $wake.NearPlanned) { $problems += "the PC woke after $($wake.GapSeconds) s, not near the planned $($wake.PlannedSeconds) s" }
}
if ($after) { Say ("after: process {0}, responding {1}, CPU while settling {2} s, {3} MB, dialog windows {4}, failure lines {5}" -f $after.ProcessId, $after.Responding, $cpuWhile, $after.WorkingSetMB, $dialogs, $failures) }
$result = [ordered]@{
    mode = $Mode; dryRun = [bool]$DryRun; startedAt = $startedAt.ToString('s'); before = $before; after = $after; cpuSecondsWhileSettling = $cpuWhile
    dialogWindows = $dialogs; failureLines = $failures; wake = $wake; wakeTaskFired = $wakeFired; problems = $problems; passed = ($problems.Count -eq 0)
}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutFile -Encoding UTF8
Say "result: $OutFile"
foreach ($problem in $problems) { Say "PROBLEM: $problem" }
if ($problems.Count -eq 0) { Say "PASS ($Mode)"; exit 0 }
Say "FAIL ($Mode)"
exit 1
