# usage: spike-install.ps1 -Name v4 [-Work C:\spike] [-Wacks]  (run as the user who installs)
param([string]$Name, [string]$Work = 'C:\spike', [switch]$Wack)
$ErrorActionPreference = 'Stop'
$root = Join-Path $Work $Name
$msix = Get-ChildItem "$root\layout" -Recurse -Filter *.msix | Where-Object { $_.FullName -notmatch 'Dependencies' } | Select-Object -First 1
$arch = if ($msix.Name -match '_(x64|arm64)_') { $Matches[1] } else { 'x64' }
$deps = @(Get-ChildItem "$root\layout" -Recurse -Filter *.msix | Where-Object { $_.FullName -match "Dependencies.$arch" } | % FullName)
Get-AppxPackage -Name BinaryCompanion.TimeTug.Spike | Remove-AppxPackage
for ($i = 1; $i -le 2; $i++) {
  try { if ($deps.Count) { Add-AppxPackage -Path $msix.FullName -DependencyPath $deps } else { Add-AppxPackage -Path $msix.FullName }; break }
  catch { if ($i -eq 1 -and $_.Exception.Message -match '0x80073D02') { Write-Host 'retry 0x80073D02'; Get-Process | ? { $_.Path -like '*WebExperience*' -or $_.Path -like '*StartExperiencesApp*' -or $_.Name -in 'StartMenuExperienceHost','Widgets','WidgetService' } | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep 3 } else { throw } }
}
$pkg = Get-AppxPackage -Name BinaryCompanion.TimeTug.Spike
Write-Host "installed: $($pkg.PackageFullName); dependencies: $(($pkg.Dependencies | % Name) -join ', ')"
Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!App"
Start-Sleep 12
$p = Get-Process TimeTug -ErrorAction SilentlyContinue
Write-Host ("running after 12s: {0}" -f [bool]$p)
$log = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)\LocalState\logs\timetug.log"
if (Test-Path $log) { Write-Host "log lines: $((Get-Content $log).Count)"; Get-Content $log -Tail 4 | % { Write-Host "  $($_ -replace '\d{4}-\d\d-\d\dT[^ ]+','<t>')" } } else { Write-Host 'no log' }
if ($p) { $p | Stop-Process -Force }
Start-Sleep 2
if ($Wack) {
  $kit = 'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe'
  if (Test-Path $kit) {
    & $kit reset | Out-Null
    & $kit test -appxpackagepath $msix.FullName -reportoutputpath "$root\wack.xml" | Select-Object -Last 5
    $rep = [xml](Get-Content "$root\wack.xml" -Raw)
    Write-Host ("WACK overall: {0}" -f $rep.REPORT.OVERALL_RESULT)
    $rep.SelectNodes('//TEST[RESULT="FAIL"]') | % { Write-Host ("  FAIL: " + $_.NAME + " / " + (($_.SelectNodes('.//MESSAGE') | % TEXT) -join ' | ')) }
  } else { Write-Host 'WACK: appcert.exe not installed' }
}
if (-not $env:KEEP_SPIKE) { Get-AppxPackage -Name BinaryCompanion.TimeTug.Spike | Remove-AppxPackage }
