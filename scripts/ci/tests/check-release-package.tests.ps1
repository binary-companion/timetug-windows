# Tests for scripts/ci/check-release-package.ps1: a clean fixture package passes, and each deliberately bad fixture fails for its own reason.
# Plain PowerShell, no Pester: Windows PowerShell 5.1, PowerShell 7 and pwsh on Linux/macOS.
#   pwsh -NoProfile -File scripts/ci/tests/check-release-package.tests.ps1
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'check-release-package.ps1'
. $scriptPath

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-True($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-HasProblem($Problems, [string]$Pattern) {
    if (-not (@($Problems) | Where-Object { $_ -match $Pattern })) { throw "no problem matching '$Pattern' in: $(@($Problems) -join ' | ')" }
}

$manifestTemplate = @'
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
  <Identity Name="__NAME__" Publisher="__PUBLISHER__" Version="__VERSION__" />
  <Properties><DisplayName>TimeTug</DisplayName><PublisherDisplayName>Binary Companion LLC</PublisherDisplayName><Logo>Assets\StoreLogo.png</Logo></Properties>
  <Applications><Application Id="App" Executable="TimeTug.exe" EntryPoint="Windows.FullTrustApplication">
    <uap:VisualElements DisplayName="TimeTug" Description="TimeTug" BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
    __ALIAS__
  </Application></Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /><Capability Name="internetClient" />__CAPABILITY__</Capabilities>
</Package>
'@
$aliasXml = '<Extensions><uap5:Extension Category="windows.appExecutionAlias" Executable="TimeTug.exe" EntryPoint="Windows.FullTrustApplication"><uap5:AppExecutionAlias><uap5:ExecutionAlias Alias="timetug-dev.exe" /></uap5:AppExecutionAlias></uap5:Extension></Extensions>'

function New-Fixture([hashtable]$Options) {
    $values = @{ Name = 'BinaryCompanion.TimeTug'; Publisher = 'CN=Binary Companion'; Version = '1.2.3.0'; Alias = ''; Capability = ''; Assemblies = @{ 'TimeTug.dll' = 'plain'; 'TimeTug.Presentation.dll' = 'plain' }; Logos = $true }
    foreach ($key in $Options.Keys) { $values[$key] = $Options[$key] }
    $root = Join-Path ([IO.Path]::GetTempPath()) ('release-check-fixture-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'Assets') | Out-Null
    $xml = $manifestTemplate.Replace('__NAME__', $values.Name).Replace('__PUBLISHER__', $values.Publisher).Replace('__VERSION__', $values.Version).Replace('__ALIAS__', $values.Alias).Replace('__CAPABILITY__', $values.Capability)
    Set-Content -LiteralPath (Join-Path $root 'AppxManifest.xml') -Value $xml -Encoding UTF8
    if ($values.Logos) {
        foreach ($logo in 'StoreLogo.png', 'Square150x150Logo.scale-200.png', 'Square44x44Logo.png') { Set-Content -LiteralPath (Join-Path $root "Assets/$logo") -Value 'png' }
    }
    foreach ($name in $values.Assemblies.Keys) {
        [System.IO.File]::WriteAllBytes((Join-Path $root $name), [System.Text.Encoding]::UTF8.GetBytes('MZ-----' + $values.Assemblies[$name] + '-----'))
    }
    return $root
}
function Problems([string]$Root, [string]$ExpectedVersion = '', [string[]]$Sentinels = @()) {
    $all = @(Get-ManifestProblems $Root $ExpectedVersion) + @(Get-AssemblyProblems $Root)
    if ($Sentinels.Count -gt 0) { $all += @(Get-SentinelProblems $Root $Sentinels 'TimeTug.Presentation.dll') }
    return $all
}

Check 'a clean release package passes' {
    $root = New-Fixture @{}
    $problems = @(Problems $root '1.2.3.0')
    Assert-True ($problems.Count -eq 0) ("unexpected problems: " + ($problems -join ' | '))
    Remove-Item -Recurse -Force $root
}

Check 'an execution alias is refused' {
    $root = New-Fixture @{ Alias = $aliasXml }
    $problems = @(Problems $root)
    Assert-HasProblem $problems 'execution alias'
    Assert-HasProblem $problems 'timetug-dev'
    Remove-Item -Recurse -Force $root
}

Check 'the throwaway publisher and the .Dev identity are refused' {
    $root = New-Fixture @{ Publisher = 'CN=TimeTug Development'; Name = 'BinaryCompanion.TimeTug.Dev' }
    $problems = @(Problems $root)
    Assert-HasProblem $problems 'throwaway development publisher'
    Assert-HasProblem $problems 'development identity'
    Remove-Item -Recurse -Force $root
}

Check 'a placeholder left in the manifest is refused' {
    $root = New-Fixture @{ Name = '__PARTNER_CENTER_PACKAGE_NAME__' }
    Assert-HasProblem (Problems $root) 'placeholder'
    Remove-Item -Recurse -Force $root
}

Check 'only X.Y.Z.0 versions pass, and the expected one must match' {
    foreach ($bad in '1.2.3.1', '1.2.3', '1.2.0.4') {
        $root = New-Fixture @{ Version = $bad }
        Assert-HasProblem (Problems $root) 'Major.Minor.Build.0'
        Remove-Item -Recurse -Force $root
    }
    $root = New-Fixture @{ Version = '1.2.4.0' }
    Assert-HasProblem (Problems $root '1.2.3.0') "expected '1.2.3.0'"
    Remove-Item -Recurse -Force $root
}

Check 'a capability the app does not need is refused' {
    $root = New-Fixture @{ Capability = '<Capability Name="picturesLibrary" />' }
    Assert-HasProblem (Problems $root) "picturesLibrary"
    Remove-Item -Recurse -Force $root
}

Check 'a logo the manifest names must be in the package' {
    $root = New-Fixture @{ Logos = $false }
    Assert-HasProblem (Problems $root) 'logo'
    Remove-Item -Recurse -Force $root
}

Check 'the self-test runner, its switch and the developer files in an assembly are refused' {
    foreach ($leak in 'TimeTug.App.SelfTest.Scenarios.Hello', 'SelfTestRunner', 'dev-oauth.json', 'dev-hotkey.txt', 'timetug-dev.exe') {
        $root = New-Fixture @{ Assemblies = @{ 'TimeTug.dll' = $leak; 'TimeTug.Presentation.dll' = 'plain' } }
        Assert-HasProblem (Problems $root) 'TimeTug.dll contains'
        Remove-Item -Recurse -Force $root
    }
}

Check 'the self-test switch is found as a UTF-16 string literal too' {
    $root = New-Fixture @{}
    $bytes = [System.Text.Encoding]::Unicode.GetBytes('--selftest')
    [System.IO.File]::WriteAllBytes((Join-Path $root 'TimeTug.dll'), ([byte[]](77, 90) + $bytes))
    Assert-HasProblem (Problems $root) 'self-test switch'
    Remove-Item -Recurse -Force $root
}

Check 'a sentinel may be in the one assembly that embeds it and nowhere else' {
    $sentinel = 'sentinel-id.example'
    $root = New-Fixture @{ Assemblies = @{ 'TimeTug.dll' = 'plain'; 'TimeTug.Presentation.dll' = $sentinel } }
    $problems = @(Problems $root '' @($sentinel))
    Assert-True ($problems.Count -eq 0) ("unexpected problems: " + ($problems -join ' | '))
    Remove-Item -Recurse -Force $root
}

Check 'a sentinel that leaked into another file, in either encoding, is refused' {
    $sentinel = 'sentinel-id.example'
    $root = New-Fixture @{ Assemblies = @{ 'TimeTug.dll' = 'plain'; 'TimeTug.Presentation.dll' = $sentinel } }
    Set-Content -LiteralPath (Join-Path $root 'Assets/notes.txt') -Value "id=$sentinel"
    [System.IO.File]::WriteAllBytes((Join-Path $root 'resources.pri'), [System.Text.Encoding]::Unicode.GetBytes($sentinel))
    $problems = @(Problems $root '' @($sentinel))
    Assert-HasProblem $problems 'notes.txt'
    Assert-HasProblem $problems 'resources.pri'
    Remove-Item -Recurse -Force $root
}

Check 'a sentinel that is not in its home assembly fails the check (a blind check proves nothing)' {
    $root = New-Fixture @{}
    Assert-HasProblem (Problems $root '' @('sentinel-id.example')) 'proves nothing'
    Remove-Item -Recurse -Force $root
}

Check 'the script exits 1 on a bad package and 0 on a clean one' {
    $shell = (Get-Process -Id $PID).Path
    $bad = New-Fixture @{ Alias = $aliasXml }
    $null = & $shell -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Folder $bad 2>&1
    Assert-True ($LASTEXITCODE -eq 1) "exit code was $LASTEXITCODE"
    $good = New-Fixture @{}
    $null = & $shell -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Folder $good 2>&1
    Assert-True ($LASTEXITCODE -eq 0) "exit code was $LASTEXITCODE"
    Remove-Item -Recurse -Force $bad, $good
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) failed"; exit 1 }
Write-Host 'all passed'
