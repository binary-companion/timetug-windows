# Tests for scripts/release/write-oauth-ids.ps1. Plain PowerShell, no Pester: Windows PowerShell 5.1, PowerShell 7 and pwsh on Linux/macOS.
#   pwsh -NoProfile -File scripts/release/tests/write-oauth-ids.tests.ps1
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'write-oauth-ids.ps1'
. $scriptPath

$script:failures = 0
function Check([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { Write-Host "FAIL $Name : $($_.Exception.Message)"; $script:failures++ }
}
function Assert-Equal($Actual, $Expected) { if ($Actual -ne $Expected) { throw "expected '$Expected' but got '$Actual'" } }
function Assert-True($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Body, [string]$Pattern) {
    $message = $null
    try { & $Body } catch { $message = $_.Exception.Message }
    if ($null -eq $message) { throw 'expected an error but there was none' }
    if ($message -notmatch $Pattern) { throw "error '$message' does not match '$Pattern'" }
}
# Runs the script in a child PowerShell with the given environment; returns @{ Output; Exit }.
function Invoke-Script([hashtable]$Environment, [string[]]$Arguments) {
    $shell = (Get-Process -Id $PID).Path
    $saved = @{}
    foreach ($name in 'GOOGLE_OAUTH_CLIENT_ID', 'GOOGLE_OAUTH_CLIENT_SECRET', 'MICROSOFT_OAUTH_CLIENT_ID') {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $Environment[$name])
    }
    try {
        $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $scriptPath @Arguments 2>&1 | Out-String
        return @{ Output = $output; Exit = $LASTEXITCODE }
    }
    finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) } }
}

$sentinelGoogle = 'sentinel-google.apps.example'
$sentinelSecret = 'sentinel-secret-value'
$sentinelMicrosoft = '00000000-0000-0000-0000-0000000005e7'

Check 'values must be plain tokens' {
    Assert-Equal (Test-OAuthValue 'abc-123.apps.example') $true
    Assert-Equal (Test-OAuthValue '00000000-0000-0000-0000-0000000005e7') $true
    Assert-Equal (Test-OAuthValue 'a"b') $false
    Assert-Equal (Test-OAuthValue 'a b') $false
    Assert-Equal (Test-OAuthValue 'a;b') $false
    Assert-Equal (Test-OAuthValue '') $false
}

Check 'the generated source carries the values and is a partial implementation' {
    $source = New-OAuthIdsSource $sentinelGoogle $sentinelSecret $sentinelMicrosoft
    Assert-True ($source -match 'static partial void Provide') 'no Provide implementation'
    Assert-True ($source.Contains("googleClientId = `"$sentinelGoogle`";")) 'google id missing'
    Assert-True ($source.Contains("googleClientSecret = `"$sentinelSecret`";")) 'google secret missing'
    Assert-True ($source.Contains("microsoftClientId = `"$sentinelMicrosoft`";")) 'microsoft id missing'
}

Check 'nothing set writes nothing' {
    Assert-Equal ($null -eq (New-OAuthIdsSource '' '' '')) $true
}

Check 'google needs its id and its secret together' {
    Assert-Throws { New-OAuthIdsSource 'only-id' '' '' } 'both GOOGLE_OAUTH_CLIENT_ID and GOOGLE_OAUTH_CLIENT_SECRET'
    Assert-Throws { New-OAuthIdsSource '' 'only-secret' '' } 'both GOOGLE_OAUTH_CLIENT_ID and GOOGLE_OAUTH_CLIENT_SECRET'
}

Check 'microsoft alone is fine' {
    $source = New-OAuthIdsSource '' '' $sentinelMicrosoft
    Assert-True ($source.Contains($sentinelMicrosoft)) 'microsoft id missing'
    Assert-True (-not $source.Contains('googleClientId =')) 'google must not be set'
}

Check 'a value that could break out of the string is refused without being printed' {
    $thrown = $null
    try { New-OAuthIdsSource 'x";evil();//' $sentinelSecret '' } catch { $thrown = $_.Exception.Message }
    Assert-True ($null -ne $thrown) 'expected an error'
    Assert-True (-not $thrown.Contains('evil')) 'the error repeated the value'
}

Check 'the script writes the file, names only the families and never prints a value' {
    $target = Join-Path ([IO.Path]::GetTempPath()) ('oauth-ids-' + [guid]::NewGuid().ToString('N') + '.g.cs')
    $run = Invoke-Script @{ GOOGLE_OAUTH_CLIENT_ID = $sentinelGoogle; GOOGLE_OAUTH_CLIENT_SECRET = $sentinelSecret; MICROSOFT_OAUTH_CLIENT_ID = $sentinelMicrosoft } @('-OutputPath', $target, '-Require')
    Assert-Equal $run.Exit 0
    Assert-True (Test-Path $target) 'file not written'
    Assert-True ($run.Output -match 'google, microsoft') 'families not named'
    foreach ($value in $sentinelGoogle, $sentinelSecret, $sentinelMicrosoft) { Assert-True (-not $run.Output.Contains($value)) 'a value was printed' }
    Remove-Item $target
}

Check '-Require fails when an id is missing, and says which variable, not its value' {
    $target = Join-Path ([IO.Path]::GetTempPath()) ('oauth-ids-' + [guid]::NewGuid().ToString('N') + '.g.cs')
    $run = Invoke-Script @{ GOOGLE_OAUTH_CLIENT_ID = $sentinelGoogle; GOOGLE_OAUTH_CLIENT_SECRET = $sentinelSecret } @('-OutputPath', $target, '-Require')
    Assert-True ($run.Exit -ne 0) 'expected a failure'
    Assert-True ($run.Output -match 'MICROSOFT_OAUTH_CLIENT_ID') 'the missing variable is not named'
    Assert-True (-not (Test-Path $target)) 'a file was written anyway'
    Assert-True (-not $run.Output.Contains($sentinelGoogle)) 'a value was printed'
}

Check 'without -Require an empty environment writes nothing and removes an old file' {
    $target = Join-Path ([IO.Path]::GetTempPath()) ('oauth-ids-' + [guid]::NewGuid().ToString('N') + '.g.cs')
    Set-Content $target 'stale'
    $run = Invoke-Script @{} @('-OutputPath', $target)
    Assert-Equal $run.Exit 0
    Assert-True (-not (Test-Path $target)) 'the stale file was left'
}

Check '-Remove deletes the generated file' {
    $target = Join-Path ([IO.Path]::GetTempPath()) ('oauth-ids-' + [guid]::NewGuid().ToString('N') + '.g.cs')
    Set-Content $target 'x'
    $run = Invoke-Script @{} @('-OutputPath', $target, '-Remove')
    Assert-Equal $run.Exit 0
    Assert-True (-not (Test-Path $target)) 'the file is still there'
}

if ($script:failures -gt 0) { Write-Host "$($script:failures) failed"; exit 1 }
Write-Host 'all passed'
