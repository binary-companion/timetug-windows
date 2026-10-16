$ErrorActionPreference='Stop'
cd C:\src\timetug-windows
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=TimeTug Development" -KeyUsage DigitalSignature -FriendlyName "TimeTug dev (throwaway)" -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddDays(2) -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
$cert.Thumbprint
dotnet publish src\TimeTug.App\TimeTug.App.csproj -c CI -p:Platform=ARM64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true "-p:PackageCertificateThumbprint=$($cert.Thumbprint)" -p:AppxPackageDir=C:\src\out\ -p:AppxBundle=Never --nologo 2>&1 | Select-Object -Last 25
Get-ChildItem C:\src\out -Recurse | select -First 20 FullName
