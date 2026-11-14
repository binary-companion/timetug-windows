# Tests for the pure logic of scripts/vm/start-cycles.ps1 (dialog title, the log scan, the Quit click point, the tray icon name).
# Plain PowerShell, no Pester: runs on Windows PowerShell 5.1, PowerShell 7 and Linux/macOS pwsh.
#   pwsh -NoProfile -File scripts/vm/tests/start-cycles.tests.ps1
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'start-cycles.ps1')

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }
function Assert-True($Condition, [string]$Text) { if (-not $Condition) { throw $Text } }
function Count-Of($Items) { if ($null -eq $Items) { return 0 } return @($Items).Count }

Check 'the dialog title matches with a straight or a curly apostrophe and nothing else' {
    Assert-True (Test-DialogTitle "TimeTug couldn't start") 'straight'
    Assert-True (Test-DialogTitle ("TimeTug couldn" + [char]0x2019 + "t start")) 'curly'
    Assert-True (-not (Test-DialogTitle 'TimeTug')) 'the app window'
    Assert-True (-not (Test-DialogTitle 'Settings')) 'settings'
    Assert-True (-not (Test-DialogTitle '')) 'empty'
}

Check 'the log scan returns only could-not-start lines from the given byte on' {
    $log = Join-Path ([IO.Path]::GetTempPath()) ('cycles-' + [guid]::NewGuid().ToString('N') + '.log')
    try {
        [IO.File]::WriteAllText($log, "start ok`nTimeTug could not start. OldReason: x`n")
        $from = (Get-Item $log).Length
        [IO.File]::AppendAllText($log, "a meeting titled Private was refreshed`nTimeTug could not start. System.IO.IOException: in use`nlater`n")
        $lines = @(Get-StartFailureLine $log $from)
        Assert-Equal $lines.Count 1
        Assert-True ($lines[0] -match 'IOException') 'reason missing'
        Assert-True (($lines -join '') -notmatch 'Private') 'another line was printed'
        Assert-Equal (Count-Of (Get-StartFailureLine $log 0)) 2
        Assert-Equal (Count-Of (Get-StartFailureLine ($log + '.missing') 0)) 0
    }
    finally { Remove-Item -Force $log -ErrorAction SilentlyContinue }
}

Check 'the Quit click point lies inside the last item at 100%, 150% and 200% scaling' {
    # A menu 214 px tall at 150% (the VM): the click is 19.5 px above the bottom edge, 105 px in.
    $p = Get-QuitClickPoint 3945 2111 320 214 144
    Assert-Equal $p.X 4050
    Assert-Equal $p.Y 2305
    $p = Get-QuitClickPoint 100 200 160 120 96
    Assert-Equal $p.X 170
    Assert-Equal $p.Y 307
    $p = Get-QuitClickPoint 100 200 300 300 192
    Assert-Equal $p.X 240
    Assert-Equal $p.Y 474
}

Check 'a very narrow menu is never clicked outside it, and a scale below 100% counts as 100%' {
    $p = Get-QuitClickPoint 100 200 50 120 96
    Assert-True ($p.X -lt 150) 'outside the menu'
    $p = Get-QuitClickPoint 100 200 160 120 48
    Assert-Equal $p.Y 307
}

Check 'tray icon names: the state tooltip or the app name, not other apps' {
    Assert-True (Test-TrayIconName 'No more meetings today') 'idle tooltip'
    Assert-True (Test-TrayIconName 'TimeTug Dev') 'app name'
    Assert-True (-not (Test-TrayIconName 'iCloud')) 'iCloud'
    Assert-True (-not (Test-TrayIconName 'Microsoft Teams')) 'Teams'
    Assert-True (-not (Test-TrayIconName '')) 'empty'
}

Check 'the script never kills a process or clicks anything except the tray icon and its Quit item' {
    $text = [regex]::Replace((Get-Content -Raw -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'start-cycles.ps1')), '(?s)<#.*?#>', '')
    Assert-True ($text -notmatch 'Stop-Process|taskkill|\.Kill\(|CloseMainWindow|SendKeys') 'kills, closes or types'
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) check(s) failed"; exit 1 }
Write-Host 'all checks passed'
