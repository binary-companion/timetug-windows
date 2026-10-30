# usage: spike-pack.ps1 -Name v1 -Config Release -Props "-p:A=b -p:C=d" -DropAlias
param([string]$Name, [string]$Config = 'Release', [string]$Props = '', [switch]$DropAlias, [string]$Platform = 'ARM64', [string]$Source = 'C:\src\timetug-windows', [string]$Work = 'C:\spike', [string]$AppProps = '')
$ErrorActionPreference = 'Stop'
$root = Join-Path $Work $Name
if (Test-Path $root) { Remove-Item -Recurse -Force $root }
New-Item -ItemType Directory -Force $root | Out-Null
robocopy $Source "$root\repo" /E /XD bin obj build .git .vs /NFL /NDL /NJH /NJS /NP | Out-Null
$m = "$root\repo\src\TimeTug.App\Package.appxmanifest"
$x = Get-Content $m -Raw
$x = $x.Replace('BinaryCompanion.TimeTug.Dev', 'BinaryCompanion.TimeTug.Spike')
if ($DropAlias) { $x = [regex]::Replace($x, '(?s)\s*<!-- Development builds only.*?</uap5:Extension>', '') } else { $x = $x.Replace('timetug-dev.exe', 'timetug-spike.exe') }
Set-Content $m $x -Encoding UTF8
if ($AppProps) {
  $csproj = "$root\repo\src\TimeTug.App\TimeTug.App.csproj"
  $c = Get-Content $csproj -Raw
  $ins = ($AppProps -split ';' | % { $kv = $_ -split '=', 2; "    <$($kv[0])>$($kv[1])</$($kv[0])>" }) -join "`n"
  $c = $c.Replace('<WindowsAppSDKSelfContained>false</WindowsAppSDKSelfContained>', $ins)
  Set-Content $csproj $c -Encoding UTF8
}
$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=TimeTug Development' -KeyUsage DigitalSignature -FriendlyName "spike $Name" -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddDays(3) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
Export-Certificate -Cert $cert -FilePath "$root\spike.cer" | Out-Null
$layout = "$root\layout"
$sw = [Diagnostics.Stopwatch]::StartNew()
Push-Location "$root\repo"
$cmd = "dotnet publish src/TimeTug.App/TimeTug.App.csproj -c $Config -p:Platform=$Platform -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true -p:PackageCertificateThumbprint=$($cert.Thumbprint) -p:AppxPackageDir=$layout\ -p:AppxBundle=Never --nologo $Props"
Write-Host $cmd.Replace($cert.Thumbprint, '<thumb>')
Invoke-Expression "$cmd 2>&1" | Where-Object { $_ -match 'error|warn|->' } | Select-Object -First 30
Pop-Location
Write-Host ("build {0:n0}s" -f $sw.Elapsed.TotalSeconds)
Import-Certificate -FilePath "$root\spike.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue
$msix = Get-ChildItem $layout -Recurse -Filter *.msix | Where-Object { $_.FullName -notmatch 'Dependencies' } | Select-Object -First 1
Write-Host ("msix: {0} {1:n1} MB" -f $msix.Name, ($msix.Length / 1MB))
$deps = @(Get-ChildItem $layout -Recurse -Filter *.msix | Where-Object { $_.FullName -match 'Dependencies' })
Write-Host ("dependency packages: {0} ({1:n1} MB)" -f $deps.Count, (($deps | Measure-Object Length -Sum).Sum / 1MB))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($msix.FullName)
$e = $zip.Entries
Write-Host ("entries: {0}; uncompressed {1:n1} MB" -f $e.Count, (($e | Measure-Object Length -Sum).Sum / 1MB))
Write-Host ("has coreclr.dll: {0}; hostfxr.dll: {1}; Microsoft.UI.Xaml.dll: {2}; Microsoft.WindowsAppRuntime.dll: {3}" -f ($e.Name -contains 'coreclr.dll'), ($e.Name -contains 'hostfxr.dll'), ($e.Name -contains 'Microsoft.UI.Xaml.dll'), ($e.Name -contains 'Microsoft.WindowsAppRuntime.dll'))
$zip.Dispose()
