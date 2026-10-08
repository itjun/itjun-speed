Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class M2 {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
}
"@
$proc = Get-Process LanSpeed.App -ErrorAction Stop
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$rect = $root.Current.BoundingRectangle
Write-Host ("窗口边界(UIA): L={0} T={1} W={2} H={3}" -f [int]$rect.X, [int]$rect.Y, [int]$rect.Width, [int]$rect.Height)

# 窗格右缘 ≈ 窗口左 + OpenPaneLength(默认190)。DPI：UIA 坐标为物理像素，鼠标同物理像素 ✓
$param = $args[0]
if (-not $param) { $param = 150 }
$startX = [int]$rect.X + 186
$startY = [int]$rect.Y + 260
Write-Host ("拖拽起点: $startX,$startY  位移: +$param")
[M2]::SetCursorPos($startX, $startY) | Out-Null
Start-Sleep -Milliseconds 400
[M2]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 150
for ($i = 1; $i -le 12; $i++) {
  [M2]::SetCursorPos($startX + [int]($param * $i / 12), $startY) | Out-Null
  Start-Sleep -Milliseconds 45
}
Start-Sleep -Milliseconds 150
[M2]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Write-Host "拖拽完成"
