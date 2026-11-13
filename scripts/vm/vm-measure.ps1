<#
.SYNOPSIS
  Measures the installed TimeTug package: installed size, memory, idle CPU and the time from process start to the tray icon.
.DESCRIPTION
  Run it in the signed-in user's session of the machine that has the package installed (Windows PowerShell 5.1 or 7). It
  measures whatever build of the package is installed, so install the build first (vm-install.ps1), then run this once
  per build with a different -Label, and compare the two JSON files. Same package name for both builds is fine.

  For each run it stops the app, starts it fresh (no account is touched: the account-free state of a fresh install),
  and records:
    startToTrayMs   process start until the tray icon exists. The tray library creates a window of class H.NotifyIcon_<guid>
                    in the app's process right before it adds the icon, so the first time that window is seen is the time.
                    Polled every 20 ms; the figure is good to about 30 ms.
    settled*        working set and private bytes of the app's processes SettleSeconds after process start (default 300 = 5 minutes).
    idleCpuSeconds  CPU time (user + kernel) the app's processes used during the next IdleSeconds (default 600 = 10 minutes).
  and, once per package, the installed size (the InstallLocation folder, summed file lengths) and the native engine files.

  The script holds the machine awake (SetThreadExecutionState) while it runs, so the screen does not lock or sleep during
  the idle window. That only lasts as long as the script. It never reads app data, logs or account settings, and writes no
  account or calendar text: the JSON holds numbers, file names of the package and the OS version.

  A 15-minute run is needed per repeat: with -Runs 3 expect about 46 minutes. Start it detached (a long prlctl exec drops):
    scripts\vm\vm-run.sh --user --ps "Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','C:\src\timetug-windows\scripts\vm\vm-measure.ps1','-Label','rust','-OutFile','C:\Users\Public\timetug-vm\measure-rust.json'"
  and read the JSON when it exists.
.PARAMETER PackageName
  The installed package name. Default BinaryCompanion.TimeTug.Dev (the development package).
.PARAMETER OutFile
  Where the JSON goes.
.PARAMETER Label
  A word for this build in the JSON (for example swift or rust).
.PARAMETER Runs
  How many fresh starts to measure. Default 3.
.PARAMETER SettleSeconds
  Seconds from process start to the memory reading. Default 300.
.PARAMETER IdleSeconds
  Seconds of idle CPU measured after the memory reading. Default 600.
.PARAMETER SampleSeconds
  Seconds between samples inside the idle window. Default 15.
#>
[CmdletBinding()]
param(
    [string]$PackageName = 'BinaryCompanion.TimeTug.Dev',
    [Parameter(Mandatory)][string]$OutFile,
    [string]$Label = '',
    [ValidateRange(1, 20)][int]$Runs = 3,
    [ValidateRange(10, 3600)][int]$SettleSeconds = 300,
    [ValidateRange(10, 7200)][int]$IdleSeconds = 600,
    [ValidateRange(1, 300)][int]$SampleSeconds = 15,
    [ValidateRange(5, 300)][int]$TrayTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class MeasureNative
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);

    // True when a top-level window of the process has a class that starts with the prefix.
    public static bool HasWindowClass(uint pid, string prefix)
    {
        bool found = false;
        EnumWindows((hwnd, l) =>
        {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner != pid) { return true; }
            var text = new StringBuilder(256);
            GetClassName(hwnd, text, 256);
            if (text.ToString().StartsWith(prefix, StringComparison.Ordinal)) { found = true; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $mid = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$mid] }
    return ($sorted[$mid - 1] + $sorted[$mid]) / 2
}

$package = Get-AppxPackage -Name $PackageName
if (-not $package) { throw "package $PackageName is not installed for this user" }
$install = $package.InstallLocation.TrimEnd('\')

# The package's executable and its application id, for the launch.
$manifest = Get-AppxPackageManifest -Package $package
$application = @($manifest.Package.Applications.Application)[0]
$executable = Split-Path -Leaf $application.Executable
$processName = [IO.Path]::GetFileNameWithoutExtension($executable)
$appUserModelId = "$($package.PackageFamilyName)!$($application.Id)"

function Get-PackageProcesses {
    # Every process whose image is inside the package folder (the app is one process; a helper would be counted too).
    @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $path = $null
            try { $path = $_.Path } catch { $path = $null }
            $path -and $path.StartsWith($install, [StringComparison]::OrdinalIgnoreCase)
        })
}

function Stop-PackageProcesses {
    foreach ($p in Get-PackageProcesses) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-PackageProcesses).Count -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
}

# Installed size.
$files = @(Get-ChildItem -LiteralPath $install -Recurse -File -Force -ErrorAction SilentlyContinue)
$installedBytes = ($files | Measure-Object -Property Length -Sum).Sum
$nativeNames = '^(TimeTugEngine|swift|Foundation|_FoundationICU|dispatch|BlocksRuntime|libswift|msalruntime)'
$engineFiles = @($files | Where-Object { $_.Name -match $nativeNames } | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($install.Length + 1); bytes = $_.Length }
    })

$os = Get-CimInstance Win32_OperatingSystem
$result = [ordered]@{
    label            = $Label
    package          = $package.Name
    version          = $package.Version.ToString()
    architecture     = $package.Architecture.ToString()
    installLocation  = $install
    installedBytes   = [long]$installedBytes
    fileCount        = $files.Count
    nativeEngineFiles = $engineFiles
    os               = "$($os.Caption) build $($os.BuildNumber)"
    measuredAt       = (Get-Date).ToString('s')
    settleSeconds    = $SettleSeconds
    idleSeconds      = $IdleSeconds
    sampleSeconds    = $SampleSeconds
    runs             = @()
}

# ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED: no sleep, no screen blanking while this script runs.
[void][MeasureNative]::SetThreadExecutionState([uint32]2147483651)

try {
    for ($run = 1; $run -le $Runs; $run++) {
        Write-Host "run $run of ${Runs}: stopping the app"
        Stop-PackageProcesses
        Start-Sleep -Seconds 10

        Write-Host "run ${run}: starting"
        Start-Process "shell:AppsFolder\$appUserModelId"

        $proc = $null
        $deadline = (Get-Date).AddSeconds(30)
        while (-not $proc -and (Get-Date) -lt $deadline) {
            $proc = @(Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($install, [StringComparison]::OrdinalIgnoreCase) }) | Select-Object -First 1
            if (-not $proc) { Start-Sleep -Milliseconds 20 }
        }
        if (-not $proc) { throw 'the app did not start within 30 seconds' }
        $startedAt = $proc.StartTime
        $mainId = [uint32]$proc.Id

        $trayMs = $null
        $deadline = (Get-Date).AddSeconds($TrayTimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            if ([MeasureNative]::HasWindowClass($mainId, 'H.NotifyIcon_')) { $trayMs = [int](((Get-Date) - $startedAt).TotalMilliseconds); break }
            if (-not (Get-Process -Id $mainId -ErrorAction SilentlyContinue)) { throw 'the app exited while starting' }
            Start-Sleep -Milliseconds 20
        }
        Write-Host "run ${run}: tray icon after $trayMs ms"

        function Read-Sample {
            $list = Get-PackageProcesses
            [ordered]@{
                workingSet   = [long](($list | Measure-Object -Property WorkingSet64 -Sum).Sum)
                privateBytes = [long](($list | Measure-Object -Property PrivateMemorySize64 -Sum).Sum)
                cpuSeconds   = [math]::Round((($list | ForEach-Object { $_.TotalProcessorTime.TotalSeconds } | Measure-Object -Sum).Sum), 3)
                processes    = $list.Count
                threads      = [int](($list | ForEach-Object { $_.Threads.Count } | Measure-Object -Sum).Sum)
                handles      = [int](($list | Measure-Object -Property HandleCount -Sum).Sum)
            }
        }

        $waitSettle = $SettleSeconds - ((Get-Date) - $startedAt).TotalSeconds
        if ($waitSettle -gt 0) { Start-Sleep -Seconds ([int][math]::Ceiling($waitSettle)) }
        if (-not (Get-Process -Id $mainId -ErrorAction SilentlyContinue)) { throw 'the app exited during the settle time' }
        $settled = Read-Sample
        $settleAt = Get-Date
        Write-Host ("run ${run}: settled working set {0:N1} MB, private {1:N1} MB" -f ($settled.workingSet / 1MB), ($settled.privateBytes / 1MB))

        $samples = @()
        $last = $settled
        while (((Get-Date) - $settleAt).TotalSeconds -lt $IdleSeconds) {
            Start-Sleep -Seconds $SampleSeconds
            if (-not (Get-Process -Id $mainId -ErrorAction SilentlyContinue)) { throw 'the app exited during the idle window' }
            $last = Read-Sample
            $samples += , @([int]((Get-Date) - $settleAt).TotalSeconds, $last.workingSet, $last.privateBytes, $last.cpuSeconds)
        }
        $idleCpu = [math]::Round($last.cpuSeconds - $settled.cpuSeconds, 3)
        Write-Host ("run ${run}: idle CPU {0} s over {1} s" -f $idleCpu, $IdleSeconds)

        $result.runs += [ordered]@{
            run                    = $run
            startToTrayMs          = $trayMs
            settledWorkingSetBytes = $settled.workingSet
            settledPrivateBytes    = $settled.privateBytes
            settledProcesses       = $settled.processes
            settledThreads         = $settled.threads
            settledHandles         = $settled.handles
            idleCpuSeconds         = $idleCpu
            endWorkingSetBytes     = $last.workingSet
            endPrivateBytes        = $last.privateBytes
            samples                = $samples
        }
    }

    $medians = [ordered]@{}
    foreach ($name in 'startToTrayMs', 'settledWorkingSetBytes', 'settledPrivateBytes', 'idleCpuSeconds') {
        $medians[$name] = Get-Median ([double[]]@($result.runs | ForEach-Object { $_[$name] }))
    }
    $result.medians = $medians
}
finally {
    [void][MeasureNative]::SetThreadExecutionState([uint32]2147483648)
    Stop-PackageProcesses
}

$folder = Split-Path -Parent $OutFile
if ($folder -and -not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Force $folder | Out-Null }
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutFile -Encoding UTF8
Write-Host "wrote $OutFile"
