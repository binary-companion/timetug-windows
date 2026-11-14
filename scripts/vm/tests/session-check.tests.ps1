# Tests for the pure logic of scripts/vm/session-check.ps1 (the before/after verdict and the wake gap).
# Plain PowerShell, no Pester: runs on Windows PowerShell 5.1, PowerShell 7 and Linux/macOS pwsh.
#   pwsh -NoProfile -File scripts/vm/tests/session-check.tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'session-check.ps1') -Mode Sleep

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }
function Assert-True($Condition, [string]$Text) { if (-not $Condition) { throw $Text } }
function Count-Of($Items) { if ($null -eq $Items) { return 0 } return @($Items).Count }
function New-State([int]$ProcessId, [bool]$Responding = $true) { return [pscustomobject]@{ ProcessId = $ProcessId; Responding = $Responding; CpuSeconds = 1.0; WorkingSetMB = 130 } }

Check 'the same responding process with no dialog, no failure line and little CPU passes' {
    Assert-Equal (Count-Of (Test-StateTransition (New-State 100) (New-State 100) 1 0 0 5)) 0
}

Check 'a restarted app is a problem' {
    $p = @(Test-StateTransition (New-State 100) (New-State 200) 0 0 0 5)
    Assert-Equal $p.Count 1
    Assert-True ($p[0] -match 'restarted') $p[0]
}

Check 'a gone app, a hung app, a dialog, a failure line and a busy CPU are each reported' {
    Assert-True ((@(Test-StateTransition (New-State 1) $null 0 0 0 5) -join ';') -match 'not running after') 'gone'
    Assert-True ((@(Test-StateTransition (New-State 1) (New-State 1 $false) 0 0 0 5) -join ';') -match 'does not respond') 'hung'
    Assert-True ((@(Test-StateTransition (New-State 1) (New-State 1) 0 1 0 5) -join ';') -match "couldn't start") 'dialog'
    Assert-True ((@(Test-StateTransition (New-State 1) (New-State 1) 0 0 2 5) -join ';') -match '2 ') 'failure lines'
    Assert-True ((@(Test-StateTransition (New-State 1) (New-State 1) 9 0 0 5) -join ';') -match 'busy') 'busy'
}

Check 'CPU exactly at the budget passes, one over does not' {
    Assert-Equal (Count-Of (Test-StateTransition (New-State 1) (New-State 1) 5 0 0 5)) 0
    Assert-Equal (Count-Of (Test-StateTransition (New-State 1) (New-State 1) 6 0 0 5)) 1
}

Check 'an app that was not running before is reported, once' {
    Assert-Equal (Count-Of (Test-StateTransition $null $null 0 0 0 5)) 2
}

Check 'the wake gap is near the plan from a minute early to ten minutes late' {
    $t = Get-Date '2026-11-14T03:00:00'
    $g = Get-WakeGap $t $t.AddSeconds(180) 3
    Assert-True $g.NearPlanned 'on time'
    $g = Get-WakeGap $t $t.AddSeconds(100) 3
    Assert-True (-not $g.NearPlanned) 'much too early'
    Assert-Equal $g.GapSeconds 100
    $late = Get-WakeGap $t $t.AddSeconds(900) 3
    Assert-True (-not $late.NearPlanned) 'much too late (woken by hand)'
}

Check 'the script puts the PC to sleep, never hibernates, and only registers its own wake task' {
    $text = [regex]::Replace((Get-Content -Raw -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'session-check.ps1')), '(?s)<#.*?#>', '')
    Assert-True ($text -match 'SetSuspendState\(\$false, \$false, \$false\)') 'must call SetSuspendState with hibernate false'
    Assert-True ($text -notmatch 'powercfg|Restart-Computer|Stop-Computer|Stop-Process|shutdown') 'changes power settings or ends something'
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) check(s) failed"; exit 1 }
Write-Host 'all checks passed'
