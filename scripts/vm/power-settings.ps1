<#
.SYNOPSIS
  Backs up, changes for a sleep test, and restores the power settings a sleep or resume check needs (wake timers, sleep, hibernate and display
  time-outs of the ACTIVE power plan). Run it elevated; Status and Backup also work without.
.DESCRIPTION
  A scheduled wake (a task with "wake the computer to run it") only works when the plan allows wake timers, and a sleep test is easier with
  a short sleep time-out. Changing them needs an elevated PowerShell, so the person at the PC runs this script (the remote agent never has
  administrator rights on the test PC). The three actions:
    Status   prints the current values (read only).
    Backup   saves the current values of the active plan to -BackupFile (refuses to overwrite an earlier backup: that would lose the original
             values; use -Force only if you are sure the earlier backup is stale).
    Apply    makes a Backup first when there is none, then enables wake timers (AC and battery) and, when given, sets the sleep and display-off
             time-outs.
    Restore  puts every value back from the backup, then deletes the backup file.
  Values are read from `powercfg /query` as the last two index numbers (AC then DC), which does not depend on the language of Windows. Nothing
  here reads personal data. Always finish a test with Restore.
.PARAMETER Action
  Status, Backup, Apply or Restore.
.PARAMETER SleepAfterMinutes
  Apply: sleep after this many minutes of inactivity (AC and battery). Default: leave as it is. 0 means never.
.PARAMETER DisplayOffMinutes
  Apply: turn the display off after this many minutes. Default: leave as it is.
.PARAMETER BackupFile
  Where the backup goes. Default: %ProgramData%\TimeTug-power-backup.json.
.PARAMETER Force
  Backup: overwrite an existing backup file.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Status', 'Backup', 'Apply', 'Restore')][string]$Action = 'Status',
    [int]$SleepAfterMinutes = -1,
    [int]$DisplayOffMinutes = -1,
    [string]$BackupFile,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# The settings this script touches: a name, the powercfg subgroup and setting aliases, and how to show the number.
$script:Settings = @(
    @{ Name = 'WakeTimers'; Sub = 'SUB_SLEEP'; Setting = 'RTCWAKE'; Unit = 'switch (0 off, 1 on, 2 important only)' },
    @{ Name = 'SleepAfter'; Sub = 'SUB_SLEEP'; Setting = 'STANDBYIDLE'; Unit = 'seconds (0 never)' },
    @{ Name = 'HibernateAfter'; Sub = 'SUB_SLEEP'; Setting = 'HIBERNATEIDLE'; Unit = 'seconds (0 never)' },
    @{ Name = 'DisplayOffAfter'; Sub = 'SUB_VIDEO'; Setting = 'VIDEOIDLE'; Unit = 'seconds (0 never)' }
)

# ---- pure helpers (tested in scripts/vm/tests/power-settings.tests.ps1) ----------------------------------------------------------------------

# The GUID in the text of `powercfg /getactivescheme`.
function Get-SchemeGuid([string]$Text) {
    $match = [regex]::Match($Text, '[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}')
    if (-not $match.Success) { throw "no power plan GUID in: $Text" }
    return $match.Value.ToLowerInvariant()
}

# The last two hex numbers of `powercfg /query <scheme> <sub> <setting>`: the current AC and DC index. Null when the setting is not there.
function Get-PowerIndex([string]$QueryText) {
    $numbers = @([regex]::Matches($QueryText, '0x[0-9a-fA-F]{8}') | ForEach-Object { $_.Value })
    if ($numbers.Count -lt 2) { return $null }
    return [pscustomobject]@{ Ac = [Convert]::ToInt64($numbers[$numbers.Count - 2].Substring(2), 16); Dc = [Convert]::ToInt64($numbers[$numbers.Count - 1].Substring(2), 16) }
}

# The target values of Apply from the settings a backup holds: wake timers on, plus the time-outs that were asked for (in minutes, 0 = never).
function Get-ApplyValue([int]$SleepAfterMinutes, [int]$DisplayOffMinutes) {
    $values = [ordered]@{ WakeTimers = 1 }
    if ($SleepAfterMinutes -ge 0) { $values.SleepAfter = $SleepAfterMinutes * 60 }
    if ($DisplayOffMinutes -ge 0) { $values.DisplayOffAfter = $DisplayOffMinutes * 60 }
    return $values
}

if ($MyInvocation.InvocationName -eq '.') { return }

# ---- Windows side ---------------------------------------------------------------------------------------------------------------------------

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not $BackupFile) { $BackupFile = Join-Path $env:ProgramData 'TimeTug-power-backup.json' }
$scheme = Get-SchemeGuid ((& powercfg /getactivescheme) | Out-String)

function Read-Current {
    $current = [ordered]@{}
    foreach ($setting in $script:Settings) {
        $index = Get-PowerIndex ((& powercfg /query $scheme $setting.Sub $setting.Setting) | Out-String)
        if ($index) { $current[$setting.Name] = [ordered]@{ Ac = $index.Ac; Dc = $index.Dc } }
    }
    return $current
}

function Set-One([string]$Name, [long]$Ac, [long]$Dc) {
    $setting = $script:Settings | Where-Object { $_.Name -eq $Name }
    if ($PSCmdlet.ShouldProcess("$Name (plan $scheme)", "set AC $Ac, battery $Dc")) {
        & powercfg /setacvalueindex $scheme $setting.Sub $setting.Setting $Ac
        & powercfg /setdcvalueindex $scheme $setting.Sub $setting.Setting $Dc
    }
}

function Write-Current($Values, [string]$Title) {
    Write-Host "[power] $Title (plan $scheme)"
    foreach ($setting in $script:Settings) {
        if ($Values.Contains($setting.Name)) { Write-Host ("  {0,-16} AC {1,6}  battery {2,6}   {3}" -f $setting.Name, $Values[$setting.Name].Ac, $Values[$setting.Name].Dc, $setting.Unit) }
    }
}

function Save-Backup {
    $current = Read-Current
    $payload = [ordered]@{ scheme = $scheme; savedAt = (Get-Date).ToString('s'); values = $current }
    if ($PSCmdlet.ShouldProcess($BackupFile, 'save the current power settings')) { $payload | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $BackupFile -Encoding UTF8 }
    Write-Host "[power] backup saved in $BackupFile"
}

switch ($Action) {
    'Status' { Write-Current (Read-Current) 'current values' }
    'Backup' {
        if ((Test-Path -LiteralPath $BackupFile) -and -not $Force) { throw "a backup already exists at ${BackupFile}: Restore from it first (or -Force if it is stale)" }
        Save-Backup
    }
    'Apply' {
        if (-not (Test-Elevated) -and -not $WhatIfPreference) { throw 'Apply needs an elevated PowerShell (Run as administrator)' }
        if (-not (Test-Path -LiteralPath $BackupFile)) { Save-Backup } else { Write-Host "[power] keeping the earlier backup $BackupFile" }
        $current = Read-Current
        $target = Get-ApplyValue $SleepAfterMinutes $DisplayOffMinutes
        foreach ($name in $target.Keys) {
            if ($current.Contains($name)) { Set-One $name $target[$name] $target[$name] }
            else { Write-Host "[power] this PC's plan has no $name setting; skipped" }
        }
        if ($PSCmdlet.ShouldProcess("plan $scheme", 'activate the changes')) { & powercfg /setactive $scheme }
        Write-Current (Read-Current) 'values now (run -Action Restore when the test is over)'
    }
    'Restore' {
        if (-not (Test-Elevated) -and -not $WhatIfPreference) { throw 'Restore needs an elevated PowerShell (Run as administrator)' }
        if (-not (Test-Path -LiteralPath $BackupFile)) { throw "no backup at ${BackupFile}: nothing to restore" }
        $backup = Get-Content -Raw -LiteralPath $BackupFile | ConvertFrom-Json
        if ($backup.scheme -ne $scheme) { Write-Host "[power] note: the active plan changed since the backup ($($backup.scheme) then, $scheme now); restoring into the plan of the backup" }
        $scheme = [string]$backup.scheme
        foreach ($property in $backup.values.PSObject.Properties) { Set-One $property.Name ([long]$property.Value.Ac) ([long]$property.Value.Dc) }
        if ($PSCmdlet.ShouldProcess("plan $scheme", 'activate the restored values')) { & powercfg /setactive $scheme }
        if ($PSCmdlet.ShouldProcess($BackupFile, 'delete the backup')) { Remove-Item -LiteralPath $BackupFile -Force }
        Write-Current (Read-Current) 'values restored'
    }
}
