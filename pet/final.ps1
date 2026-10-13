$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$root = $PSScriptRoot
$exe  = Join-Path $root 'dist\Pet.exe'
$out  = Join-Path $root 'build'
$log  = Join-Path $root 'dist\pet-debug.log'
if (Test-Path $log) { Remove-Item $log -Force }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class W {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr e);
  public const uint DOWN = 0x0002, UP = 0x0004, RDOWN = 0x0008, RUP = 0x0010, WHEEL = 0x0800;
  public static int PidAt(int x, int y) {
    POINT p; p.X = x; p.Y = y;
    IntPtr h = WindowFromPoint(p);
    if (h == IntPtr.Zero) return 0;
    uint pid; GetWindowThreadProcessId(h, out pid);
    return (int)pid;
  }
}
'@

function Bbox([int]$procId) {
    $a = 99999; $b = -1; $c = 99999; $d = -1
    for ($y = 880; $y -le 1400; $y += 10) {
        for ($x = 2080; $x -le 2556; $x += 10) {
            if ([W]::PidAt($x, $y) -eq $procId) {
                if ($x -lt $a) { $a = $x }; if ($x -gt $b) { $b = $x }
                if ($y -lt $c) { $c = $y }; if ($y -gt $d) { $d = $y }
            }
        }
    }
    return @($a, $b, $c, $d)
}

function Shot([string]$name) {
    $bmp = New-Object System.Drawing.Bitmap(480, 520)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(2110, 885, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function LClick([int]$x, [int]$y) {
    [void][W]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 150
    [W]::mouse_event([W]::DOWN, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 50
    [W]::mouse_event([W]::UP, 0, 0, 0, [IntPtr]::Zero)
}

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Milliseconds 2500
$bb = Bbox $p.Id
$cx = [int](($bb[0] + $bb[1]) / 2); $cy = [int](($bb[2] + $bb[3]) / 2)
Write-Host ("character bbox x {0}..{1} y {2}..{3} -> target {4},{5}" -f $bb[0], $bb[1], $bb[2], $bb[3], $cx, $cy)

Write-Host '[1] jump'
LClick $cx $cy; Start-Sleep -Milliseconds 230; Shot 'f1_jump'; Start-Sleep -Milliseconds 1400

Write-Host '[2] squash'
LClick $cx $cy; Start-Sleep -Milliseconds 190; Shot 'f2_squash'; Start-Sleep -Milliseconds 1400

Write-Host '[3] shake'
LClick $cx $cy; Start-Sleep -Milliseconds 190; Shot 'f3_shake'; Start-Sleep -Milliseconds 2800

Write-Host '[4] right-click menu'
[void][W]::SetCursorPos($cx, $cy); Start-Sleep -Milliseconds 200
[W]::mouse_event([W]::RDOWN, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 60
[W]::mouse_event([W]::RUP, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 900
Shot 'f4_menu'
[W]::keybd_event(0x1B, 0, 0, [IntPtr]::Zero); [W]::keybd_event(0x1B, 0, 2, [IntPtr]::Zero)
Start-Sleep -Milliseconds 600

Write-Host '[5] wheel resize'
$b0 = Bbox $p.Id
[void][W]::SetCursorPos($cx, $cy); Start-Sleep -Milliseconds 250
for ($i = 0; $i -lt 3; $i++) { [W]::mouse_event([W]::WHEEL, 0, 0, 120, [IntPtr]::Zero); Start-Sleep -Milliseconds 300 }
Start-Sleep -Milliseconds 300
$b1 = Bbox $p.Id
Write-Host ("  before {0}x{1}   after {2}x{3}" -f ($b0[1] - $b0[0]), ($b0[3] - $b0[2]), ($b1[1] - $b1[0]), ($b1[3] - $b1[2]))
Shot 'f5_scaled'

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300
Write-Host ''
Write-Host '===== log ====='
if (Test-Path $log) { Get-Content $log }
