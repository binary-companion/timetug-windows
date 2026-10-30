<#
.SYNOPSIS
  Installs the Dev-signed Store dry-run package (build-release-msix.ps1 -DryRun), starts it, checks it runs and runs the Windows App Certification Kit.
.DESCRIPTION
  For the pull-request dry run and for the VM. Needs an elevated session or SYSTEM to trust the throwaway certificate (a CI runner is elevated).
  1. Trusts dryrun.cer for the machine, installs the package with its framework packages for the current user.
  2. Starts it (the release manifest has no execution alias: shell:AppsFolder), waits, requires a running TimeTug process and a log file
     with entries (no calendar or account data is involved), then stops it.
  3. With -Wack and appcert.exe present (the x64 hosted runner; not on ARM64, see docs/spikes/2026-10-store-packaging.md) runs the Kit on
     the package: the overall result must be PASS; optional findings are listed.
  4. Removes the package and the trusted certificate again.
.PARAMETER Folder
  The dry run's output folder (the .msix, dryrun.cer and layout\Dependencies\<arch>).
.PARAMETER Platform
  x64 or ARM64.
.PARAMETER Wack
  Run the Windows App Certification Kit when it is installed.
.PARAMETER WackReport
  Where to copy the Kit's XML report. Default: wack.xml in -Folder.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Folder,
    [Parameter(Mandatory)][ValidateSet('x64', 'ARM64')][string]$Platform,
    [switch]$Wack,
    [string]$WackReport
)

$ErrorActionPreference = 'Stop'
$identityName = 'BinaryCompanion.TimeTug.ReleaseDryRun'
$publisher = 'CN=TimeTug Release Dry Run'

function Get-WackFailures($Report) {
    $failures = @()
    foreach ($test in $Report.SelectNodes('//TEST')) {
        $result = $test.SelectSingleNode('RESULT')
        if ($result -and $result.InnerText -ne 'PASS') {
            $messages = @($test.SelectNodes('.//MESSAGE') | ForEach-Object { $_.GetAttribute('TEXT') })
            $failures += [pscustomobject]@{ Name = $test.GetAttribute('NAME'); Optional = ($test.GetAttribute('OPTIONAL') -eq 'TRUE'); Result = $result.InnerText; Text = ($messages -join ' | ') }
        }
    }
    return $failures
}

if ($MyInvocation.InvocationName -ne '.') {
    $Folder = (Resolve-Path -LiteralPath $Folder).Path
    $arch = $Platform.ToLowerInvariant()
    $msix = @(Get-ChildItem -LiteralPath $Folder -Filter "TimeTug_*_$arch.msix" -File | Select-Object -First 1)[0]
    if (-not $msix) { throw "no TimeTug_*_$arch.msix in $Folder" }
    $cer = Join-Path $Folder 'dryrun.cer'
    if (-not (Test-Path -LiteralPath $cer)) { throw "dryrun.cer is missing from $Folder" }
    $dependencies = @(Get-ChildItem -LiteralPath (Join-Path $Folder 'layout') -Recurse -Filter '*.msix' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "Dependencies[\\/]$arch[\\/]" } | ForEach-Object { $_.FullName })

    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $cer
    if ($certificate.HasPrivateKey) { throw 'dryrun.cer holds a private key; only the public certificate belongs here' }
    if ($certificate.Subject -ne $publisher) { throw "unexpected certificate subject '$($certificate.Subject)'" }

    $thumbprint = $certificate.Thumbprint
    # Trusting is machine-wide and needs an elevated session; on the VM that is done as SYSTEM first, and the install here runs as the user.
    $alreadyTrusted = @(Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $thumbprint }).Count -gt 0
    if (-not $alreadyTrusted) { Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null }
    try {
        Get-AppxPackage -Name $identityName | Remove-AppxPackage
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            try {
                if ($dependencies.Count -gt 0) { Add-AppxPackage -Path $msix.FullName -DependencyPath $dependencies } else { Add-AppxPackage -Path $msix.FullName }
                break
            }
            catch {
                # 0x80073D02: a shell host that uses the Windows App Runtime is running; stop it (it restarts by itself) and try once more.
                if ($attempt -eq 1 -and $_.Exception.Message -match '0x80073D02') {
                    Write-Host 'Install blocked by running apps; stopping them and retrying'
                    Get-Process -ErrorAction SilentlyContinue | Where-Object {
                        $_.Path -like '*WebExperience*' -or $_.Path -like '*StartExperiencesApp*' -or $_.Name -in 'StartMenuExperienceHost', 'Widgets', 'WidgetService'
                    } | Stop-Process -Force -ErrorAction SilentlyContinue
                    Start-Sleep -Seconds 3
                    continue
                }
                throw
            }
        }
        $package = Get-AppxPackage -Name $identityName
        if (-not $package) { throw 'the package did not install' }
        Write-Host "Installed $($package.PackageFullName); framework dependencies: $(($package.Dependencies | ForEach-Object { $_.Name }) -join ', ')"

        Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"
        $running = $null
        for ($waited = 0; $waited -lt 30 -and -not $running; $waited += 2) { Start-Sleep -Seconds 2; $running = Get-Process -Name TimeTug -ErrorAction SilentlyContinue }
        if (-not $running) { throw 'TimeTug did not start' }
        Start-Sleep -Seconds 8
        if (-not (Get-Process -Name TimeTug -ErrorAction SilentlyContinue)) { throw 'TimeTug started and then exited' }
        $log = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState\logs\timetug.log"
        if (-not (Test-Path -LiteralPath $log) -or (Get-Content -LiteralPath $log).Count -lt 1) { throw 'the app wrote no log entries' }
        Write-Host "The app is running and has written $((Get-Content -LiteralPath $log).Count) log line(s)."
        Get-Process -Name TimeTug -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2

        if ($Wack) {
            $kit = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
            if (-not (Test-Path -LiteralPath $kit)) { Write-Host "The Windows App Certification Kit is not installed on this machine ($Platform): its report is skipped." }
            else {
                $report = Join-Path $Folder 'wack.xml'
                & $kit reset | Out-Null
                & $kit test -appxpackagepath $msix.FullName -reportoutputpath $report | Select-Object -Last 3
                if (-not (Test-Path -LiteralPath $report)) { throw 'the Certification Kit wrote no report' }
                $xml = [xml](Get-Content -LiteralPath $report -Raw)
                $failures = @(Get-WackFailures $xml)
                foreach ($failure in $failures) {
                    $text = $failure.Text; if ($text.Length -gt 300) { $text = $text.Substring(0, 300) + '...' }
                    Write-Host ("WACK {0}: {1} ({2}) {3}" -f $failure.Result, $failure.Name, $(if ($failure.Optional) { 'optional' } else { 'REQUIRED' }), $text)
                }
                if ($WackReport) { Copy-Item -LiteralPath $report -Destination $WackReport -Force }
                $overall = $xml.DocumentElement.GetAttribute('OVERALL_RESULT'); if (-not $overall) { $overall = $xml.SelectSingleNode('//OVERALL_RESULT').InnerText }
                if ($overall -ne 'PASS') { throw "the Windows App Certification Kit result is $overall" }
                if (@($failures | Where-Object { -not $_.Optional }).Count -gt 0) { throw 'the Certification Kit reports a required test that did not pass' }
                Write-Host 'WACK: overall PASS (optional findings are listed above, not failures).'
            }
        }
    }
    finally {
        Get-Process -Name TimeTug -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Get-AppxPackage -Name $identityName | Remove-AppxPackage -ErrorAction SilentlyContinue
        Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $thumbprint } | Remove-Item -ErrorAction SilentlyContinue
    }
}
