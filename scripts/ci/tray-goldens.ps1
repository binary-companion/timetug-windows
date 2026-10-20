<#
.SYNOPSIS
  Checks, or with -Update rewrites, the golden pictures of the tray digit icons.
.DESCRIPTION
  The goldens are tests/TimeTug.Platform.Tests/data/tray-digit-<digit>-<size>-<light|dark>.png (digits 1 to 9 at 16 and 32 px).
  Tests compare the renderer's output with them (per channel within 2 of 255, see TrayGoldenTests) and never write them.
  Pass -Update only after a deliberate change of the digit shapes (src/TimeTug.App/Assets/Tray/Glyphs/digits.txt), the icon
  files or the renderer: it redraws every golden and the contact sheet in docs/screenshots/tray-digits-contact-sheet.png from the
  current renderer. Look at the pictures before committing them.
.PARAMETER Update
  Rewrite the goldens and the contact sheet, then run the comparison.
#>
[CmdletBinding()]
param([switch]$Update)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repo
try {
    $project = 'tests/TimeTug.Platform.Tests'
    if ($Update) {
        $env:TIMETUG_UPDATE_TRAY_GOLDENS = '1'
        try {
            dotnet test $project --nologo --filter 'FullyQualifiedName~TrayGoldenTests.Update_the_goldens_when_asked'
            if ($LASTEXITCODE -ne 0) { throw "rewriting the goldens failed ($LASTEXITCODE)" }
        }
        finally { Remove-Item Env:\TIMETUG_UPDATE_TRAY_GOLDENS -ErrorAction SilentlyContinue }
        Write-Host 'Goldens rewritten: review tests/TimeTug.Platform.Tests/data/*.png and docs/screenshots/tray-digits-contact-sheet.png'
    }

    dotnet test $project --nologo --filter 'FullyQualifiedName~TrayGoldenTests|FullyQualifiedName~TrayContactSheetTests'
    if ($LASTEXITCODE -ne 0) { throw "the golden comparison failed ($LASTEXITCODE)" }
}
finally { Pop-Location }
