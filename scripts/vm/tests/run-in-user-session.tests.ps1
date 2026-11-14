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

Check 'the script registers only an interactive, limited task and never kills or elevates anything' {
    $text = [regex]::Replace((Get-Content -Raw -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'run-in-user-session.ps1')), '(?s)<#.*?#>', '')
    Assert-True ($text -match '-LogonType Interactive') 'not interactive'
    Assert-True ($text -match '-RunLevel Limited') 'not limited'
    Assert-True ($text -notmatch 'RunLevel Highest|-Password|Stop-Process|Stop-ScheduledTask') 'elevates, stores a password or kills'
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) check(s) failed"; exit 1 }
Write-Host 'all checks passed'
