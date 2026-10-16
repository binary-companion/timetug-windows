<#
.SYNOPSIS
  Builds and tests TimeTug for one architecture.
.PARAMETER Platform
  x64 or ARM64. Defaults to the architecture of this machine.
.PARAMETER Configuration
  Debug (default), CI or Release.
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'ARM64' } else { 'x64' }),
    [ValidateSet('Debug', 'CI', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    dotnet build TimeTug.sln -c $Configuration "-p:Platform=$Platform" --nologo
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
    dotnet test TimeTug.sln -c $Configuration "-p:Platform=$Platform" --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "tests failed ($LASTEXITCODE)" }
}
finally {
    Pop-Location
}
