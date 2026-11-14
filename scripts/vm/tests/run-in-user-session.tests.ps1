# Tests for the pure logic of scripts/vm/run-in-user-session.ps1 (quoting, job names, the wrapper that runs inside the session, `query user` parsing).
# Plain PowerShell, no Pester: runs on Windows PowerShell 5.1, PowerShell 7 and Linux/macOS pwsh.
#   pwsh -NoProfile -File scripts/vm/tests/run-in-user-session.tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'run-in-user-session.ps1')

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }
function Assert-True($Condition, [string]$Text) { if (-not $Condition) { throw $Text } }
function Count-Of($Items) { if ($null -eq $Items) { return 0 } return @($Items).Count }
function New-TempFolder { $f = Join-Path ([IO.Path]::GetTempPath()) ('riu-test-' + [guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Force -Path $f | Out-Null; return $f }
# The PowerShell that runs the wrapper in these tests: the one running the tests.
function Get-Shell { if ($PSVersionTable.PSEdition -eq 'Core') { return (Get-Process -Id $PID).Path } else { return 'powershell.exe' } }

Check 'single quotes in a literal are doubled' {
    Assert-Equal (ConvertTo-SingleQuoted "it's") "'it''s'"
    Assert-Equal (ConvertTo-SingleQuoted 'C:\a b\c.ps1') "'C:\a b\c.ps1'"
}

Check 'a comma list that powershell -File passed as one string is split' {
    Assert-Equal ((Expand-ScriptArgument @('-Cycles,1')) -join '|') '-Cycles|1'
    Assert-Equal ((Expand-ScriptArgument @('-Cycles', '1')) -join '|') '-Cycles|1'
    Assert-Equal ((Expand-ScriptArgument @('a,b')) -join '|') 'a,b'
    Assert-Equal (Count-Of (Expand-ScriptArgument @())) 0
}

Check 'job names are limited to safe characters' {
    Assert-True (Test-JobName 'selftest-1_a') 'a normal name'
    Assert-True (-not (Test-JobName 'a b')) 'space'
    Assert-True (-not (Test-JobName '..\x')) 'path'
    Assert-True (-not (Test-JobName '')) 'empty'
    Assert-True (-not (Test-JobName ('a' * 61))) 'too long'
}

Check 'query user text is read for the user, case-insensitively, header skipped' {
    $text = " USERNAME              SESSIONNAME        ID  STATE   IDLE TIME  LOGON TIME`r`n>ttagent               console             1  Active      none   11/14/2026 3:02 AM`r`n owner                 rdp-tcp#2           2  Disc        1:04   11/14/2026 1:00 AM"
    Assert-True (Test-UserListed $text 'ttagent') 'the console user'
    Assert-True (Test-UserListed $text 'OWNER') 'case'
    Assert-True (-not (Test-UserListed $text 'USERNAME')) 'the header is not a user'
    Assert-True (-not (Test-UserListed $text 'nobody')) 'a user that is not there'
    Assert-True (-not (Test-UserListed '' 'ttagent')) 'empty text'
}

Check 'the log tail skips what was already read' {
    $f = New-TempFolder
    try {
        $log = Join-Path $f 'a.log'
        Assert-Equal (Count-Of (Get-NewLogLine $log 0)) 0
        Set-Content -LiteralPath $log -Value @('one', 'two', 'three')
        Assert-Equal ((Get-NewLogLine $log 0) -join ',') 'one,two,three'
        Assert-Equal ((Get-NewLogLine $log 2) -join ',') 'three'
        Assert-Equal (Count-Of (Get-NewLogLine $log 3)) 0
    }
    finally { Remove-Item -Recurse -Force $f }
}

Check 'the wrapper runs a script with awkward arguments, logs every stream and writes the exit code last' {
    $f = New-TempFolder
    try {
        $target = Join-Path $f "t est's.ps1"
        Set-Content -LiteralPath $target -Value 'param([string]$A, [string]$B) ; "A=$A"; "B=$B"; Write-Warning "careful"; Write-Error "bad" -ErrorAction Continue; exit 3'
        $log = Join-Path $f 'job.log'
        $wrapper = Join-Path $f 'wrapper.ps1'
        Set-Content -LiteralPath $wrapper -Value (New-WrapperScript $target @('-A', "it's here") $log) -Encoding UTF8
        # -A is bound by name: the wrapper passes '-A' and "it's here" as two arguments.
        & (Get-Shell) -NoProfile -ExecutionPolicy Bypass -File $wrapper | Out-Null
        $lines = @(Get-Content -LiteralPath $log)
        Assert-True ($lines -contains "A=it's here") "argument lost: $($lines -join ' / ')"
        Assert-True (($lines -join ' ') -match 'careful') 'the warning stream is not logged'
        Assert-True (($lines -join ' ') -match 'bad') 'the error stream is not logged'
        Assert-Equal (Get-ExitCode $log) 3
    }
    finally { Remove-Item -Recurse -Force $f }
}

Check 'a script that throws gives exit code 1 and the message in the log' {
    $f = New-TempFolder
    try {
        $target = Join-Path $f 'boom.ps1'
        Set-Content -LiteralPath $target -Value 'throw "kaboom"'
        $log = Join-Path $f 'job.log'
        $wrapper = Join-Path $f 'wrapper.ps1'
        Set-Content -LiteralPath $wrapper -Value (New-WrapperScript $target @() $log) -Encoding UTF8
        & (Get-Shell) -NoProfile -ExecutionPolicy Bypass -File $wrapper | Out-Null
        Assert-Equal (Get-ExitCode $log) 1
        Assert-True ((Get-Content -LiteralPath $log -Raw) -match 'kaboom') 'the message is not in the log'
    }
    finally { Remove-Item -Recurse -Force $f }
}

Check 'a job that has not finished has no exit code yet' {
    $f = New-TempFolder
    try { Assert-Equal (Get-ExitCode (Join-Path $f 'none.log')) $null } finally { Remove-Item -Recurse -Force $f }
}

Check 'the task XML is interactive, limited, has no password and no trigger unless asked' {
    $xml = New-UserTaskXml -UserId 'PC\me & you' -Command 'powershell.exe' -Arguments '-File "C:\a b\w.ps1"' -TimeLimitSeconds 4500
    $doc = [xml]$xml
    $ns = New-Object Xml.XmlNamespaceManager $doc.NameTable
    $ns.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    Assert-Equal $doc.SelectSingleNode('//t:LogonType', $ns).InnerText 'InteractiveToken'
    Assert-Equal $doc.SelectSingleNode('//t:RunLevel', $ns).InnerText 'LeastPrivilege'
    Assert-Equal $doc.SelectSingleNode('//t:UserId', $ns).InnerText 'PC\me & you'
    Assert-Equal $doc.SelectSingleNode('//t:ExecutionTimeLimit', $ns).InnerText 'PT4500S'
    Assert-Equal $doc.SelectSingleNode('//t:Arguments', $ns).InnerText '-File "C:\a b\w.ps1"'
    Assert-Equal $doc.SelectSingleNode('//t:WakeToRun', $ns).InnerText 'false'
    Assert-True ($null -eq $doc.SelectSingleNode('//t:Triggers', $ns)) 'a trigger without being asked'
    Assert-True ($xml -notmatch 'Password|Highest') 'password or elevation'
}

Check 'a one-time wake trigger is written when asked' {
    $xml = New-UserTaskXml -UserId 'PC\me' -Command 'x.exe' -Arguments '' -StartBoundary ([datetime]'2026-11-14T10:05:09') -WakeToRun
    Assert-True ($xml -match '<StartBoundary>2026-11-14T10:05:09</StartBoundary>') 'start boundary'
    Assert-True ($xml -match '<WakeToRun>true</WakeToRun>') 'wake'
}

Check 'schtasks query CSV is read by column position' {
    $line = '"PC","\TimeTug-user-session-x","N/A","Running","Interactive only","11/14/2026 3:02:00 AM","267009","me","","","","","","","","","","","","","","","","","","","","","","","",""'
    $r = ConvertFrom-TaskQueryCsv $line
    Assert-Equal $r.Status 'Running'
    Assert-Equal $r.LastResult '267009'
    Assert-True (Test-TaskStarted $r $false) 'a running task has started'
    $never = ConvertFrom-TaskQueryCsv ($line -replace '"Running"', '"Ready"' -replace '"267009"', '"267011"')
    Assert-True (-not (Test-TaskStarted $never $false)) 'a task that never ran has not started'
    Assert-True (Test-TaskStarted $never $true) 'output means it started'
    Assert-True (-not (Test-TaskStarted $null $false)) 'no record'
    $done = ConvertFrom-TaskQueryCsv ($line -replace '"Running"', '"Ready"' -replace '"267009"', '"0"')
    Assert-True (Test-TaskStarted $done $false) 'it ran and finished'
}

Check 'the script registers only an interactive, limited task through schtasks and never kills or elevates anything' {
    $folder = Split-Path -Parent $PSScriptRoot
    $text = [regex]::Replace((Get-Content -Raw -LiteralPath (Join-Path $folder 'run-in-user-session.ps1')), '(?s)<#.*?#>', '')
    $lib = [regex]::Replace((Get-Content -Raw -LiteralPath (Join-Path $folder 'user-task.ps1')), '(?s)<#.*?#>', '')
    Assert-True ($lib -match 'InteractiveToken' -and $lib -match 'LeastPrivilege') 'not interactive and limited'
    Assert-True (($text + $lib) -notmatch 'RunLevel Highest|-Password|Stop-Process|/end|Stop-ScheduledTask') 'elevates, stores a password or kills'
    foreach ($name in 'run-in-user-session.ps1', 'user-task.ps1', 'session-check.ps1', 'vm-measure.ps1', 'start-cycles.ps1', 'power-settings.ps1', 'owner-upgrade-check.ps1') {
        $code = [regex]::Replace((Get-Content -Raw -LiteralPath (Join-Path $folder $name)), '(?s)<#.*?#>', '')
        Assert-True ($code -notmatch '(?m)^[^#\r\n]*(ScheduledTask|Get-CimInstance|Get-WmiObject)') "$name uses CIM or the ScheduledTasks cmdlets, which a standard user is denied over SSH"
    }
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) check(s) failed"; exit 1 }
Write-Host 'all checks passed'
