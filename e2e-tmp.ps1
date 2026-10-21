Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk); [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);' -Name HK -Namespace N
function Probe($label) { $ok = [N.HK]::RegisterHotKey([IntPtr]::Zero, 77, 7, 0x86); if ($ok) { [void][N.HK]::UnregisterHotKey([IntPtr]::Zero, 77) }; "$label : another program can register Ctrl+Alt+Shift+F23 = $ok" }
$dir = "$env:LOCALAPPDATA\TimeTug"; New-Item -ItemType Directory -Force $dir | Out-Null
Get-Process TimeTug -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item "$dir\dev-hotkey.txt" -ErrorAction SilentlyContinue
Start-Process "shell:AppsFolder\BinaryCompanion.TimeTug.Dev_68z4tsj3tx7ew!App"; Start-Sleep 14
Probe "no dev-hotkey.txt"
Get-Process TimeTug | Stop-Process -Force; Start-Sleep 2
Set-Content "$dir\dev-hotkey.txt" "Ctrl+Alt+Shift+F23"
Start-Process "shell:AppsFolder\BinaryCompanion.TimeTug.Dev_68z4tsj3tx7ew!App"; Start-Sleep 14
Probe "dev-hotkey.txt = Ctrl+Alt+Shift+F23"
Get-Process TimeTug | Stop-Process -Force; Start-Sleep 2
Remove-Item "$dir\dev-hotkey.txt"
Get-AppxPackage BinaryCompanion.TimeTug.Dev | Reset-AppxPackage
"cleaned"
