#Requires -Version 7.0
<#
.SYNOPSIS
    验证"修复主题"（--repair / 托盘菜单）：制造 GUI 首次切换的任务栏冻结，验证一键修复。
.DESCRIPTION
    已知规律：GUI 实例第一次切换 8/8 触发任务栏 partial/冻结。本脚本复现冻结后执行
    CLI --repair（等值重写+UpdatePerUserSystemParameters+广播），采样验证解冻。
    前置条件：双屏真机（依赖 Shell_SecondaryTrayWnd）。
#>
param([int]$SettleMs = 6000)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Native
{
public static class Rp
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
    [DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string cls, string title);

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

    public static RECT GetRect(IntPtr h) { GetWindowRect(h, out RECT r); return r; }

    public static void BroadcastSettingChange(string param)
    {
        _ = SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, param, 0x0002, 3000, out _);
    }
}
}
'@

function Get-TrayPoint([string]$className, [double]$ratio) {
    $h = [Native.Rp]::FindWindowW($className, $null)
    if ($h -eq [IntPtr]::Zero) { Write-Error "$className not found" }
    $r = [Native.Rp]::GetRect($h)
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

function Sample([string]$Label) {
    $pv = @(); foreach ($pt in $pPts) { $pv += (Get-Brightness $pt.X $pt.Y) }
    $sv = @(); foreach ($pt in $sPts) { $sv += (Get-Brightness $pt.X $pt.Y) }
    $pM = Median $pv; $sM = Median $sv
    Write-Host ("{0,-44} P=[{1}] med={2,3}({3}) | S=[{4}] med={5,3}({6})" -f `
        $Label, ($pv -join ','), $pM, (Judge $pM), ($sv -join ','), $sM, (Judge $sM))
    return @{ P = (Judge $pM); S = (Judge $sM) }
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
    $mainHwnd = [Native.Rp]::FindWindowByTitle('主题切换器')
    if ($mainHwnd -eq [IntPtr]::Zero) { Write-Error 'main window not found' }
    $toggleButton = [Native.Rp]::EnumChildren($mainHwnd) |
        Where-Object { $_.Class -like 'WindowsForms10.BUTTON*' } |
        Sort-Object W -Descending | Select-Object -First 1

    Write-Host ''
    Write-Host '=== Step 1: GUI first click (expected: taskbar lag/frozen) ==='
    [Native.Rp]::ClickButton($toggleButton.Hwnd)
    Start-Sleep -Milliseconds $SettleMs
    $sys = Get-SystemFlag
    $expect = if ($sys -eq 1) { 'LIGHT' } else { 'DARK' }
    $stuck = Sample ("GUI click #1 (sys={0} want {1})" -f $sys, $expect)

    Write-Host ''
    Write-Host '=== Step 2: CLI --repair (expect: taskbar matches registry) ==='
    $p = Start-Process -FilePath $exe -ArgumentList @('--repair') -Wait -PassThru
    Start-Sleep -Milliseconds 3000
    $repaired = Sample ("after --repair (sys={0} want {1})" -f $sys, $expect)

    $repairedOk = $repaired.P -eq $expect -and $repaired.S -eq $expect
    Write-Host ''
    if ($repairedOk) {
        Write-Host 'REPAIR RESULT: PASS (taskbar woke up and matches registry)'
    }
    elseif ($stuck.P -eq $expect -and $stuck.S -eq $expect) {
        Write-Host 'NOTE: no freeze reproduced this run (first click already followed); repair untested'
    }
    else {
        Write-Host 'REPAIR RESULT: FAIL (taskbar still stuck after repair)'
    }

    Write-Host ''
    Write-Host '=== Step 3: one more GUI click (post-repair health) ==='
    [Native.Rp]::ClickButton($toggleButton.Hwnd)
    Start-Sleep -Milliseconds $SettleMs
    $sys3 = Get-SystemFlag
    $expect3 = if ($sys3 -eq 1) { 'LIGHT' } else { 'DARK' }
    $null = Sample ("GUI click #2 after repair (sys={0} want {1})" -f $sys3, $expect3)
}
finally {
    if ($gui -and -not $gui.HasExited) { Stop-Process -Id $gui.Id -Force }
    Set-ItemProperty -Path $personalize -Name SystemUsesLightTheme -Value $originalSystem -Type DWord
    Set-ItemProperty -Path $personalize -Name AppsUseLightTheme -Value $originalApps -Type DWord
    [Native.Rp]::BroadcastSettingChange('ImmersiveColorSet')
    Write-Host ''
    Write-Host ("Restored original theme: System={0} Apps={1}" -f $originalSystem, $originalApps)
}
