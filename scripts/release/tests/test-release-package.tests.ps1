# Tests for the pure logic in scripts/release/test-release-package.ps1 (reading a Certification Kit report). Plain PowerShell.
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'test-release-package.ps1') -Folder . -Platform x64

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }

Check 'a Certification Kit report lists what did not pass and whether it is optional' {
    [xml]$report = '<REPORT OVERALL_RESULT="PASS"><TESTS>' +
        '<TEST INDEX="1" NAME="Fine" OPTIONAL="FALSE"><RESULT><![CDATA[PASS]]></RESULT></TEST>' +
        '<TEST INDEX="88" NAME="Blocked executables" OPTIONAL="TRUE"><RESULT><![CDATA[FAIL]]></RESULT><MESSAGES><MESSAGE TEXT="File a.dll uses CreateProcessW" /></MESSAGES></TEST>' +
        '<TEST INDEX="2" NAME="Needed" OPTIONAL="FALSE"><RESULT><![CDATA[FAIL]]></RESULT></TEST></TESTS></REPORT>'
    $failures = @(Get-WackFailures $report)
    Assert-Equal $failures.Count 2
    Assert-Equal $failures[0].Name 'Blocked executables'
    Assert-Equal $failures[0].Optional $true
    Assert-Equal ($failures[0].Text -like '*CreateProcessW*') $true
    Assert-Equal $failures[1].Optional $false
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) failed"; exit 1 }
Write-Host 'all passed'
