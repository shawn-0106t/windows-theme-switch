#Requires -Version 7.0
<#
.SYNOPSIS
    GUI 路径验证 v2：自动点击 GUI 实例"一键全切"按钮，双时刻采样主/副屏任务栏；
    出现滞留时立即做唤醒实验（定向消息 / 广播 / DwmFlush / 再点击）。
.DESCRIPTION
    上一版在 round 1 观察到主副屏任务栏全部不跟随（注册表已变）。此版不提前 break，
    每轮采 1.5s 与 3.5s 两个时刻，滞留现场自动尝试唤醒手段并采样其效果。
#>
param(
    [int]$SettleMs = 1500,
    [int]$Rounds = 6
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Native
{
public static class Gui
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string cls, string title);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr h);

    public struct RECT { public int Left, Top, Right, Bottom; }

    public static IntPtr FindWindowByTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            var t = new StringBuilder(256);
            _ = GetWindowText(h, t, 256);
            if (t.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public sealed class ChildWin { public IntPtr Hwnd; public string Class = ""; public int W, H; }

    public static List<ChildWin> EnumChildren(IntPtr parent)
    {
        var list = new List<ChildWin>();
        EnumChildWindows(parent, (h, _) =>
        {
            var c = new StringBuilder(256);
            _ = GetClassName(h, c, 256);
            GetWindowRect(h, out RECT r);
            list.Add(new ChildWin { Hwnd = h, Class = c.ToString(), W = r.Right - r.Left, H = r.Bottom - r.Top });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static void ClickButton(IntPtr h) { _ = PostMessage(h, 0x00F5, IntPtr.Zero, IntPtr.Zero); }

    public static bool GetRect(IntPtr h, out RECT r) { return GetWindowRect(h, out r); }

    public static void SendSettingChange(IntPtr h, string param)
    {
        _ = SendMessageTimeout(h, 0x001A, IntPtr.Zero, param, 0x0002, 2000, out _);
    }

    public static void BroadcastSettingChange(string param)
    {
        _ = SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, param, 0x0002, 3000, out _);
    }
}
}
'@

function Get-TrayPoint([string]$className, [double]$ratio) {
    $h = [Native.Gui]::FindWindowW($className, $null)
    if ($h -eq [IntPtr]::Zero) { Write-Error "$className not found" }
    $r = New-Object Native.Gui+RECT
    [void][Native.Gui]::GetRect($h, [ref]$r)
    return @{ X = [int]($r.Left + ($r.Right - $r.Left) * $ratio); Y = [int](($r.Top + $r.Bottom) / 2) }
}

function Get-Brightness([int]$x, [int]$y) {
    $bmp = [System.Drawing.Bitmap]::new(6, 6)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.CopyFromScreen($x - 3, $y - 3, 0, 0, [System.Drawing.Size]::new(6, 6))
        $sum = 0
        for ($i = 0; $i -lt 6; $i++) {
            for ($j = 0; $j -lt 6; $j++) {
                $p = $bmp.GetPixel($i, $j)
                $sum += [int](($p.R + $p.G + $p.B) / 3)
            }
        }
        return [int]($sum / 36)
    }
    finally { $g.Dispose(); $bmp.Dispose() }
}

$pPts = @(0.15, 0.35, 0.65) | ForEach-Object { Get-TrayPoint 'Shell_TrayWnd' $_ }
$sPts = @(0.15, 0.35, 0.65) | ForEach-Object { Get-TrayPoint 'Shell_SecondaryTrayWnd' $_ }

function Judge([int]$v) { if ($v -lt 128) { 'DARK' } else { 'LIGHT' } }

function Median([int[]]$a) { ($a | Sort-Object)[[int](($a.Count - 1) / 2)] }

function Sample6([string]$Label) {
    $pv = @(); foreach ($pt in $pPts) { $pv += (Get-Brightness $pt.X $pt.Y) }
    $sv = @(); foreach ($pt in $sPts) { $sv += (Get-Brightness $pt.X $pt.Y) }
    $pM = Median $pv; $sM = Median $sv
    Write-Host ("{0,-40} P=[{1}] med={2,3}({3}) | S=[{4}] med={5,3}({6})" -f `
        $Label, ($pv -join ','), $pM, (Judge $pM), ($sv -join ','), $sM, (Judge $sM))
    return @{ P = $pM; S = $sM }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot "src\ThemeSwitcher\bin\Debug\net8.0-windows\ThemeSwitcher.exe"
$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
function Get-SystemFlag { [int](Get-ItemPropertyValue -Path $personalize -Name SystemUsesLightTheme) }

$existing = Get-Process ThemeSwitcher -ErrorAction SilentlyContinue
if ($existing) { Write-Error 'another ThemeSwitcher instance is running; close it first' }

$originalSystem = Get-SystemFlag
$originalApps = [int](Get-ItemPropertyValue -Path $personalize -Name AppsUseLightTheme)
Write-Host ("Original theme: System={0} Apps={1}" -f $originalSystem, $originalApps)

$gui = Start-Process -FilePath $exe -PassThru
try {
    Start-Sleep -Seconds 2
    $mainHwnd = [Native.Gui]::FindWindowByTitle('主题切换器')
    if ($mainHwnd -eq [IntPtr]::Zero) { Write-Error 'main window not found' }
    $toggleButton = [Native.Gui]::EnumChildren($mainHwnd) |
        Where-Object { $_.Class -like 'WindowsForms10.BUTTON*' } |
        Sort-Object W -Descending | Select-Object -First 1
    if (-not $toggleButton) { Write-Error 'toggle button not found' }
    Write-Host ("toggle button hwnd=0x{0:X} size={1}x{2}" -f $toggleButton.Hwnd.ToInt64(), $toggleButton.W, $toggleButton.H)

    Write-Host ''
    Write-Host "=== GUI path: $Rounds button clicks (activate first, then click) ==="
    $lagRound = -1
    for ($round = 1; $round -le $Rounds; $round++) {
        [void][Native.Gui]::SetForegroundWindow($mainHwnd)
        Start-Sleep -Milliseconds 400
        [Native.Gui]::ClickButton($toggleButton.Hwnd)
        Start-Sleep -Milliseconds $SettleMs
        $sys = Get-SystemFlag
        $expect = if ($sys -eq 1) { 'LIGHT' } else { 'DARK' }
        $state = Sample6 ("round {0} @+{1}ms (sys={2} want {3})" -f $round, $SettleMs, $sys, $expect)
        $badEarly = (Judge $state.P) -ne $expect -or (Judge $state.S) -ne $expect
        if ($badEarly) {
            Start-Sleep -Milliseconds 3000
            $state2 = Sample6 ("round {0} settle +3s" -f $round)
            $stillBad = (Judge $state2.P) -ne $expect -or (Judge $state2.S) -ne $expect
            if ($stillBad) {
                $lagRound = $round
                Write-Host ("STUCK at round {0}: taskbar(s) not following after settle" -f $round)
                break
            }
            Write-Host ("round {0}: settled late - continuing" -f $round)
        }
    }

    if ($lagRound -ge 0) {
        Write-Host ''
        Write-Host '=== wake-up experiments on the stuck state ==='
        $trayH = [Native.Gui]::FindWindowW('Shell_TrayWnd', $null)
        $secH = [Native.Gui]::FindWindowW('Shell_SecondaryTrayWnd', $null)

        Start-Sleep -Seconds 6  # 等 GUI 实例的完整 SetTheme（含 reapply）彻底结束
        $null = Sample6 'after settle (idle 6s)'

        [Native.Gui]::SendSettingChange($trayH, 'ImmersiveColorSet')
        [Native.Gui]::SendSettingChange($secH, 'ImmersiveColorSet')
        Start-Sleep -Milliseconds 2000
        $null = Sample6 'direct WM_SETTINGCHANGE -> both trays'

        [Native.Gui]::BroadcastSettingChange('ImmersiveColorSet')
        Start-Sleep -Milliseconds 2000
        $null = Sample6 'broadcast WM_SETTINGCHANGE'
    }
    else {
        Write-Host "no stuck state observed in $Rounds GUI rounds"
    }
}
finally {
    if ($gui -and -not $gui.HasExited) { Stop-Process -Id $gui.Id -Force }
    Set-ItemProperty -Path $personalize -Name SystemUsesLightTheme -Value $originalSystem -Type DWord
    Set-ItemProperty -Path $personalize -Name AppsUseLightTheme -Value $originalApps -Type DWord
    [Native.Gui]::BroadcastSettingChange('ImmersiveColorSet')
    Write-Host ''
    Write-Host ("Restored original theme: System={0} Apps={1}" -f $originalSystem, $originalApps)
}
