<#
.SYNOPSIS
  Runs a PowerShell script in the signed-in user's interactive desktop session and brings its output back. Made for SSH: a command run over
  OpenSSH Server has no desktop, but the self-test, the tray, windows and screen captures need one.
.DESCRIPTION
  A process started by sshd runs in a non-interactive logon session: no desktop, no windows, no tray, no UI Automation tree, no screen capture.
  The scripts of this folder that need the desktop (scripts\ci\run-selftest.ps1, run-a11y.ps1, vm-measure.ps1, start-cycles.ps1,
  owner-upgrade-check.ps1's start of the app) therefore must be started by something that already lives in the interactive session. A scheduled
  task registered for the same user with the Interactive logon type does that: Task Scheduler starts it inside the user's session on the desktop
  (the user must be signed in at the console or over Remote Desktop; a locked session still works for the app, not for input or screen captures).

  This script registers a one-shot task for -UserId (default: the user running this script; no admin rights are needed for one's own task, no
  password is stored), starts it, follows a log file and prints it, waits for an exit-code file, prints the exit code and removes the task. The
  task runs at normal rights (Limited), never elevated. Nothing is installed or downloaded.

  If the task never starts (nobody is signed in as that user) it says so after -StartTimeoutSeconds and removes the task. If the script outlasts
  -TimeoutSeconds the task is left running (it is never killed) and its log and exit file stay in the log folder; read them later with -Collect.
.PARAMETER ScriptPath
  The .ps1 to run (a full path on this PC).
.PARAMETER ScriptArguments
  Arguments for it, as one string each (for example '-Cycles','10'). A string like -Cycles is passed as a parameter name, every other string as one
  quoted value (so a value that itself looks like -Name cannot be passed).
.PARAMETER UserId
  DOMAIN\user whose desktop runs it. Default: the user running this script.
.PARAMETER Name
  A short name for the job (letters, digits, - and _). Default: a time stamp. It names the task, the log and the exit file.
.PARAMETER Detach
  Start the task and return at once. Read the output later with -Collect <Name>.
.PARAMETER Collect
  Print the output of a job started earlier and say whether it has finished (exit code) or still runs.
.PARAMETER TimeoutSeconds
  How long to follow the job before leaving it running. Default 900.
.PARAMETER StartTimeoutSeconds
  How long to wait for the task to start in the session. Default 30.
.PARAMETER LogFolder
  Where logs go. Default: %LOCALAPPDATA%\TimeTug-user-session of the user running this script (must be readable by -UserId).
#>
[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(ParameterSetName = 'Run')][string]$ScriptPath,
    [Parameter(ParameterSetName = 'Run')][string[]]$ScriptArguments = @(),
    [Parameter(ParameterSetName = 'Run')][string]$Name,
    [Parameter(ParameterSetName = 'Run')][switch]$Detach,
    [Parameter(ParameterSetName = 'Collect')][string]$Collect,
    [string]$UserId,
    [int]$TimeoutSeconds = 900,
    [int]$StartTimeoutSeconds = 30,
    [string]$LogFolder
)

$ErrorActionPreference = 'Stop'

# ---- pure helpers (tested in scripts/vm/tests/run-in-user-session.tests.ps1) ------------------------------------------------------------------

# A PowerShell single-quoted literal: 'it''s'.
function ConvertTo-SingleQuoted([string]$Text) { return "'" + $Text.Replace("'", "''") + "'" }

# A job name safe for a task name and file names.
function Test-JobName([string]$Name) { return $Name -match '^[A-Za-z0-9_-]{1,60}$' }

# The wrapper that runs inside the session: runs the script with its arguments, appends everything it prints (all streams) to the log, and writes
# the exit code to <log>.exit as the last thing it does. An error that stops the script is written to the log and gives exit code 1.
function New-WrapperScript([string]$ScriptPath, [string[]]$Arguments, [string]$LogPath) {
    $argumentText = ''
    foreach ($argument in @($Arguments)) {
        # A parameter name (-Cycles, -NoStart) stays a parameter name; everything else is one quoted value.
        if ($argument -match '^-[A-Za-z][A-Za-z0-9]*$') { $argumentText += ' ' + $argument } else { $argumentText += ' ' + (ConvertTo-SingleQuoted $argument) }
    }
    $log = ConvertTo-SingleQuoted $LogPath
    $exit = ConvertTo-SingleQuoted ($LogPath + '.exit')
    $target = ConvertTo-SingleQuoted $ScriptPath
    return @"
`$log = $log
`$code = 0
try {
    & $target$argumentText *>&1 | ForEach-Object { Add-Content -LiteralPath `$log -Value ([string]`$_) }
    if (`$LASTEXITCODE -is [int] -and `$LASTEXITCODE -ne 0) { `$code = `$LASTEXITCODE }
}
catch { Add-Content -LiteralPath `$log -Value ('error: ' + `$_.Exception.Message); `$code = 1 }
Set-Content -LiteralPath $exit -Value `$code
"@
}

# True when the text of `query user` (or `quser`) lists the user. The header line is skipped; the current session is marked with '>'.
function Test-UserListed([string]$QueryUserText, [string]$UserName) {
    foreach ($line in ($QueryUserText -split "`r?`n" | Select-Object -Skip 1)) {
        $first = ($line.TrimStart('>', ' ') -split '\s+')[0]
        if ($first -and $first -ieq $UserName) { return $true }
    }
    return $false
}

# Lines of the log after the first $Skip lines.
function Get-NewLogLine([string]$LogPath, [int]$Skip) {
    if (-not (Test-Path -LiteralPath $LogPath)) { return @() }
    return @(Get-Content -LiteralPath $LogPath | Select-Object -Skip $Skip)
}

# ---- Windows side --------------------------------------------------------------------------------------------------------------------------

function Get-JobFolder {
    if ($LogFolder) { $folder = $LogFolder } else { $folder = Join-Path $env:LOCALAPPDATA 'TimeTug-user-session' }
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    return (Resolve-Path -LiteralPath $folder).Path
}

function Read-JobOutput([string]$Log, [ref]$Seen) {
    $lines = @(Get-NewLogLine $Log $Seen.Value)
    foreach ($line in $lines) { Write-Host $line }
    $Seen.Value += $lines.Count
}

function Get-ExitCode([string]$Log) {
    $file = $Log + '.exit'
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    return [int]((Get-Content -LiteralPath $file -Raw).Trim())
}

if ($MyInvocation.InvocationName -ne '.') {
    $folder = Get-JobFolder

    if ($PSCmdlet.ParameterSetName -eq 'Collect') {
        if (-not (Test-JobName $Collect)) { throw "invalid job name: $Collect" }
        $log = Join-Path $folder "$Collect.log"
        if (-not (Test-Path -LiteralPath $log) -and -not (Test-Path -LiteralPath ($log + '.exit'))) { throw "no job named $Collect under $folder" }
        $seen = 0
        Read-JobOutput $log ([ref]$seen)
        $code = Get-ExitCode $log
        if ($null -eq $code) { Write-Host "[user-session] $Collect is still running" } else { Write-Host "[user-session] $Collect finished with exit code $code" }
        if ($null -ne $code) { exit $code } else { exit 0 }
    }

    if (-not $ScriptPath) { throw 'give -ScriptPath <script.ps1> to run, or -Collect <job name> to read a job' }
    if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) { throw "script not found: $ScriptPath" }
    if (-not $UserId) { $UserId = "$env:USERDOMAIN\$env:USERNAME" }
    if (-not $Name) { $Name = 'job-' + (Get-Date).ToString('yyyyMMdd-HHmmss') }
    if (-not (Test-JobName $Name)) { throw "invalid job name '$Name': letters, digits, - and _ only, at most 60" }
    $userName = ($UserId -split '\\')[-1]
    $listing = ''
    try { $listing = (& query.exe user 2>&1 | Out-String) } catch { $listing = '' }
    if ($listing -and -not (Test-UserListed $listing $userName)) {
        throw "$UserId has no signed-in session on this PC (query user lists none): sign in as that user at the console first, then run this again"
    }

    $log = Join-Path $folder "$Name.log"
    foreach ($stale in $log, ($log + '.exit'), (Join-Path $folder "$Name.wrapper.ps1")) { if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force } }
    $wrapper = Join-Path $folder "$Name.wrapper.ps1"
    Set-Content -LiteralPath $wrapper -Value (New-WrapperScript $ScriptPath $ScriptArguments $log) -Encoding UTF8

    $taskName = "TimeTug-user-session-$Name"
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ("-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"{0}`"" -f $wrapper)
    $principal = New-ScheduledTaskPrincipal -UserId $UserId -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds ($TimeoutSeconds + 3600)) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "[user-session] job $Name for $UserId; log $log"
    Start-ScheduledTask -TaskName $taskName

    # Wait for it to start in the session.
    $started = $false
    $deadline = (Get-Date).AddSeconds($StartTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $info = Get-ScheduledTask -TaskName $taskName | Get-ScheduledTaskInfo
        $state = (Get-ScheduledTask -TaskName $taskName).State
        if ($state -eq 'Running' -or (Test-Path -LiteralPath $log) -or (Test-Path -LiteralPath ($log + '.exit')) -or $info.LastRunTime.Year -gt 2000) { $started = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $started) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        throw "the task did not start within ${StartTimeoutSeconds}s: is $UserId signed in on the desktop (not only over SSH)? Task removed."
    }
    if ($Detach) {
        Write-Host "[user-session] started; read it with: -Collect $Name (the task $taskName stays until you remove it: Unregister-ScheduledTask -TaskName $taskName -Confirm:`$false)"
        return
    }

    $seen = 0
    $end = (Get-Date).AddSeconds($TimeoutSeconds)
    $code = $null
    while ((Get-Date) -lt $end) {
        Read-JobOutput $log ([ref]$seen)
        $code = Get-ExitCode $log
        if ($null -ne $code) { break }
        Start-Sleep -Seconds 1
    }
    Read-JobOutput $log ([ref]$seen)
    if ($null -eq $code) {
        Write-Host "[user-session] job $Name is still running after ${TimeoutSeconds}s; left running. Read it with -Collect $Name, remove the task with: Unregister-ScheduledTask -TaskName $taskName -Confirm:`$false"
        exit 124
    }
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Remove-Item -LiteralPath $wrapper -Force -ErrorAction SilentlyContinue
    Write-Host "[user-session] job $Name finished with exit code $code"
    exit $code
}
