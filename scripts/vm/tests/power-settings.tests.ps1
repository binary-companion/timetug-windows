# Tests for the pure logic of scripts/vm/power-settings.ps1 (reading powercfg output, the values Apply sets).
# Plain PowerShell, no Pester: runs on Windows PowerShell 5.1, PowerShell 7 and Linux/macOS pwsh.
#   pwsh -NoProfile -File scripts/vm/tests/power-settings.tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'power-settings.ps1')

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }
function Assert-True($Condition, [string]$Text) { if (-not $Condition) { throw $Text } }

# powercfg /query output, English and German (the numbers are all that is read).
$english = @"
  Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
  GUID Alias: SCHEME_BALANCED
  Subgroup GUID: 238c9fa8-0aad-41ed-83f4-97be242c8f20  (Sleep)
    Power Setting GUID: 29f6c1db-86da-48c5-9fdb-f2b67b1f44da  (Sleep after)
      GUID Alias: STANDBYIDLE
      Possible Setting Index: 000
      Possible Settings Friendly Name: Never
    Current AC Power Setting Index: 0x00000708
    Current DC Power Setting Index: 0x00000384
"@
$german = @"
  GUID des Energieschemas: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert)
    Aktueller Netzbetriebs-Einstellungsindex: 0x00000000
    Aktueller Akkubetriebs-Einstellungsindex: 0x0000012c
"@

Check 'the active plan GUID is read from powercfg /getactivescheme' {
    Assert-Equal (Get-SchemeGuid 'Power Scheme GUID: 381B4222-F694-41F0-9685-FF5BB260DF2E  (Balanced)') '381b4222-f694-41f0-9685-ff5bb260df2e'
    $threw = $false
    try { Get-SchemeGuid 'nothing here' | Out-Null } catch { $threw = $true }
    Assert-True $threw 'a text without a GUID must throw'
}

Check 'AC and battery indexes are the last two hex numbers, whatever the language' {
    $i = Get-PowerIndex $english
    Assert-Equal $i.Ac 1800
    Assert-Equal $i.Dc 900
    $i = Get-PowerIndex $german
    Assert-Equal $i.Ac 0
    Assert-Equal $i.Dc 300
}

Check 'a setting the plan does not have gives null' {
    Assert-Equal (Get-PowerIndex 'The power scheme, subgroup or setting specified does not exist.') $null
    Assert-Equal (Get-PowerIndex '') $null
}

Check 'Apply turns wake timers on and converts minutes to seconds only for what was asked' {
    $v = Get-ApplyValue -1 -1
    Assert-Equal $v.Keys.Count 1
    Assert-Equal $v.WakeTimers 1
    $v = Get-ApplyValue 2 5
    Assert-Equal $v.SleepAfter 120
    Assert-Equal $v.DisplayOffAfter 300
    $v = Get-ApplyValue 0 -1
    Assert-Equal $v.SleepAfter 0
    Assert-True (-not $v.Contains('DisplayOffAfter')) 'display must stay untouched'
}

Check 'every touched setting has a name, a powercfg subgroup and a setting alias' {
    foreach ($s in $script:Settings) { Assert-True ($s.Name -and $s.Sub -and $s.Setting -and $s.Unit) "incomplete: $($s.Name)" }
    Assert-Equal @($script:Settings | Where-Object { $_.Name -eq 'WakeTimers' }).Count 1
}

Check 'the script cannot touch anything but the power plan values and its own backup file' {
    $text = [regex]::Replace((Get-Content -Raw -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'power-settings.ps1')), '(?s)<#.*?#>', '')
    Assert-True ($text -notmatch 'Set-ItemProperty|New-ItemProperty|reg\.exe|Stop-Service|Restart-Computer|Stop-Computer') 'touches more than powercfg'
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) check(s) failed"; exit 1 }
Write-Host 'all checks passed'
