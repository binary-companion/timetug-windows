<#
.SYNOPSIS
  Helpers (dot-source this file) to register, start, query and remove a one-shot scheduled task for the current user through schtasks.exe.
.DESCRIPTION
  Over an OpenSSH session a standard user is denied CIM/WMI ("Cannot connect to CIM server. Access denied"), and the ScheduledTasks cmdlets
  (Get-ScheduledTask, Register-ScheduledTask, New-ScheduledTaskAction, ...) and Get-CimInstance all need it. schtasks.exe talks to the Task
  Scheduler service directly and works. The task is described in XML (schtasks /create /xml) so it can have the Interactive logon type with
  Limited rights, a hidden-friendly action, an execution time limit and, when asked, a one-time trigger that wakes the PC. No password is
  stored and nothing is elevated. Every function here is pure except the ones that start schtasks.exe.
#>

# XML text escaped for an element or attribute value.
function ConvertTo-XmlText([string]$Text) { return [Security.SecurityElement]::Escape($Text) }

# The Task Scheduler XML of a task that runs $Command $Arguments for $UserId in that user's interactive session, at Limited rights.
# No trigger unless -StartBoundary (a [datetime], local time) is given: the task is then started by `schtasks /run`.
function New-UserTaskXml {
    param([string]$UserId, [string]$Command, [string]$Arguments, [int]$TimeLimitSeconds = 3600, $StartBoundary = $null, [switch]$WakeToRun)
    $trigger = ''
    if ($null -ne $StartBoundary) {
        $trigger = "<Triggers><TimeTrigger><StartBoundary>$(([datetime]$StartBoundary).ToString('yyyy-MM-ddTHH:mm:ss'))</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>"
    }
    $wake = if ($WakeToRun) { 'true' } else { 'false' }
    return @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  $trigger
  <Principals>
    <Principal id="Author">
      <UserId>$(ConvertTo-XmlText $UserId)</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <WakeToRun>$wake</WakeToRun>
    <ExecutionTimeLimit>PT${TimeLimitSeconds}S</ExecutionTimeLimit>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$(ConvertTo-XmlText $Command)</Command>
      <Arguments>$(ConvertTo-XmlText $Arguments)</Arguments>
    </Exec>
  </Actions>
</Task>
"@
}

# One CSV line of `schtasks /query /fo csv /v /nh` as a record: Status and LastResult by column position (the header text is localized, the order is not).
function ConvertFrom-TaskQueryCsv([string]$Text) {
    $line = @($Text -split "`r?`n" | Where-Object { $_.Trim() })[0]
    if (-not $line) { return $null }
    $columns = @($line | ConvertFrom-Csv -Header (0..40 | ForEach-Object { "c$_" }))[0]
    if ($null -eq $columns) { return $null }
    return [pscustomobject]@{ Status = [string]$columns.c3; LastRunTime = [string]$columns.c5; LastResult = [string]$columns.c6 }
}

# True when the task has run, is running, or left its log or exit file: not merely "ready, never run" (last result 267011 = 0x41303, "not yet run").
function Test-TaskStarted($Record, [bool]$HasOutput) {
    if ($HasOutput) { return $true }
    if ($null -eq $Record) { return $false }
    if ($Record.Status -match '^(Running|Wird ausgef|En cours|En ejecuci)') { return $true }
    return ($Record.LastResult -and $Record.LastResult -notin '267011', '0x41303', 'N/A')
}

# The signed-in account as DOMAIN\user. Not $env:USERDOMAIN: over OpenSSH it says WORKGROUP, which maps to no account.
function Get-CurrentUserId { return [Security.Principal.WindowsIdentity]::GetCurrent().Name }

function Invoke-SchTasks([string[]]$Arguments) {
    # schtasks writes errors to stderr; with 'Stop' that would throw before the exit code is read.
    $ErrorActionPreference = 'Continue'
    $text = (& schtasks.exe @Arguments 2>&1 | ForEach-Object { [string]$_ } | Out-String)
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = $text.Trim() }
}

# Registers (replacing any task of that name) from XML text. UTF-16 with a byte order mark, as schtasks expects.
function Register-UserTaskXml([string]$TaskName, [string]$Xml) {
    $file = Join-Path ([IO.Path]::GetTempPath()) ('tt-task-' + [guid]::NewGuid().ToString('N') + '.xml')
    try {
        [IO.File]::WriteAllText($file, $Xml, [Text.Encoding]::Unicode)
        $result = Invoke-SchTasks @('/create', '/tn', $TaskName, '/xml', $file, '/f')
    }
    finally { Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue }
    if ($result.ExitCode -ne 0) { throw "schtasks /create failed ($($result.ExitCode)): $($result.Text)" }
}

function Start-UserTask([string]$TaskName) {
    $result = Invoke-SchTasks @('/run', '/tn', $TaskName)
    if ($result.ExitCode -ne 0) { throw "schtasks /run failed ($($result.ExitCode)): $($result.Text)" }
}

# The record of ConvertFrom-TaskQueryCsv, or $null when the task does not exist.
function Get-UserTaskRecord([string]$TaskName) {
    $result = Invoke-SchTasks @('/query', '/tn', $TaskName, '/fo', 'csv', '/v', '/nh')
    if ($result.ExitCode -ne 0) { return $null }
    return ConvertFrom-TaskQueryCsv $result.Text
}

# Removes the task; no error when it is already gone.
function Remove-UserTask([string]$TaskName) {
    [void](Invoke-SchTasks @('/delete', '/tn', $TaskName, '/f'))
}
