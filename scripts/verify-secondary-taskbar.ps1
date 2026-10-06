#Requires -Version 7.0
<#
.SYNOPSIS
    视觉验证：截屏采样主/副屏任务栏像素亮度，量化确认副屏任务栏主题滞留，并实验候选唤醒消息。
.DESCRIPTION
    任务栏深色亮度约 32、浅色约 243（阈值 128 二分判定）。
    阶段 1：切 light/dark 各一次并采样，确认主屏正常、副屏滞留；
    阶段 2：副屏处于"错色"状态时逐个发送候选消息，采样观察哪个能唤醒。
    结束后恢复原主题并广播。采样只读取像素颜色，不保存截图。
#>
param(
    [int]$SettleMs = 1500,
    [int]$Rounds = 12
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Native -Name TbExperiment -MemberDefinition @'
[DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
public static void SendSettingChange(IntPtr h, string param) {
    _ = SendMessageTimeout(h, 0x001A, IntPtr.Zero, param, 0x0002, 1000, out _);
}
public static void BroadcastSettingChange(string param) {
    _ = SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, param, 0x0002, 2000, out _);
}
[DllImport("dwmapi.dll")]
public static extern int DwmFlush();
[DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
public static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);
[DllImport("user32.dll", SetLastError = false)]
public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
[StructLayout(LayoutKind.Sequential)]
public struct RECT { public int Left, Top, Right, Bottom; }
'@

# WinForms Screen 在 DPI 虚拟化坐标下与 CopyFromScreen 一致，自洽即可
Add-Type -AssemblyName System.Windows.Forms

function Get-TrayRect([string]$className) {
    $h = [Native.TbExperiment]::FindWindowW($className, $null)
    if ($h -eq [IntPtr]::Zero) { return $null }
    $r = New-Object Native.TbExperiment+RECT
    [void][Native.TbExperiment]::GetWindowRect($h, [ref]$r)
    return $r
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
    finally {
        $g.Dispose(); $bmp.Dispose()
    }
}

$primaryRect = Get-TrayRect 'Shell_TrayWnd'
$secondaryRect = Get-TrayRect 'Shell_SecondaryTrayWnd'
if (-not $primaryRect) { Write-Error 'Shell_TrayWnd not found' }
if (-not $secondaryRect) { Write-Error 'Shell_SecondaryTrayWnd not found' }

# Win11 任务栏图标组居中：取左右两侧空白背景各一点，避开图标/托盘污染；
# 两点任一不符即判 lag（覆盖 ADM #1172 描述的 partial 现象）
function Get-SamplePoints($rect) {
    $y = [int](($rect.Top + $rect.Bottom) / 2)
    $w = $rect.Right - $rect.Left
    return @(
        @{ X = [int]($rect.Left + $w * 0.22); Y = $y; Tag = 'L' }
        @{ X = [int]($rect.Left + $w * 0.78); Y = $y; Tag = 'R' }
    )
}
$primaryPts = Get-SamplePoints $primaryRect
$secondaryPts = Get-SamplePoints $secondaryRect
Write-Host ("Primary tray points:   ({0},{1}) ({2},{3})" -f $primaryPts[0].X, $primaryPts[0].Y, $primaryPts[1].X, $primaryPts[1].Y)
Write-Host ("Secondary tray points: ({0},{1}) ({2},{3})" -f $secondaryPts[0].X, $secondaryPts[0].Y, $secondaryPts[1].X, $secondaryPts[1].Y)

function Sample([string]$Label) {
    $pL = Get-Brightness $primaryPts[0].X $primaryPts[0].Y
    $pR = Get-Brightness $primaryPts[1].X $primaryPts[1].Y
    $sL = Get-Brightness $secondaryPts[0].X $secondaryPts[0].Y
    $sR = Get-Brightness $secondaryPts[1].X $secondaryPts[1].Y
    Write-Host ("{0,-38} P:L={1,3} R={2,3} | S:L={3,3} R={4,3}" -f $Label, $pL, $pR, $sL, $sR)
    return @{ PL = $pL; PR = $pR; SL = $sL; SR = $sR }
}

function Test-Brightness([int]$value, [string]$expected) {
    $actual = if ($value -lt 128) { 'DARK' } else { 'LIGHT' }
    return $actual -eq $expected
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot "src\ThemeSwitcher\bin\Debug\net8.0-windows\ThemeSwitcher.exe"

$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$originalSystem = [int](Get-ItemPropertyValue -Path $personalize -Name SystemUsesLightTheme)
$originalApps = [int](Get-ItemPropertyValue -Path $personalize -Name AppsUseLightTheme)
Write-Host ("Original theme: System={0} Apps={1}" -f $originalSystem, $originalApps)

function Set-Theme([string]$mode) {
    $p = Start-Process -FilePath $exe -ArgumentList @('--set', $mode) -Wait -PassThru
    if ($p.ExitCode -ne 0) { Write-Error "ThemeSwitcher --set $mode exited $($p.ExitCode)" }
}

try {
    Write-Host ''
    Write-Host "=== Phase 1: stress $Rounds alternating switches, watch for secondary lag ==="
    $lagDetected = $false
    for ($round = 1; $round -le $Rounds; $round++) {
        $mode = if ($round % 2 -eq 1) { 'light' } else { 'dark' }
        $expect = if ($mode -eq 'light') { 'LIGHT' } else { 'DARK' }
        Set-Theme $mode
        Start-Sleep -Milliseconds $SettleMs
        $state = Sample ("round {0,2}: --set {1}" -f $round, $mode)
        $sBad = @()
        if (-not (Test-Brightness $state.SL $expect)) { $sBad += 'L' }
        if (-not (Test-Brightness $state.SR $expect)) { $sBad += 'R' }
        if ($sBad.Count -gt 0) {
            Write-Host ("LAG DETECTED at round {0}: secondary {1} point(s) expected {2}" -f $round, ($sBad -join '/'), $expect)
            $lagDetected = $true
            break
        }
        foreach ($pair in @(@($state.PL, 'PL'), @($state.PR, 'PR'))) {
            if (-not (Test-Brightness $pair[0] $expect)) {
                Write-Host ("WARNING: primary {0} mismatched at round {1} (expected {2}, raw {3})" -f $pair[1], $round, $expect, $pair[0])
            }
        }
    }

    if (-not $lagDetected) {
        Write-Host "Secondary taskbar followed all $Rounds switches (no lag reproduced this run). Phase 2 skipped."
        return
    }

    Write-Host ''
    Write-Host "=== Phase 2: wake-up experiments (registry expects last set, secondary stuck) ==="
    $secondaryHwnd = [Native.TbExperiment]::FindWindowW('Shell_SecondaryTrayWnd', $null)

    [Native.TbExperiment]::SendSettingChange($secondaryHwnd, 'ImmersiveColorSet')
    Start-Sleep -Milliseconds $SettleMs
    $null = Sample 'direct WM_SETTINGCHANGE(ImmersiveColorSet) -> secondary'

    [Native.TbExperiment]::BroadcastSettingChange('ImmersiveColorSet')
    Start-Sleep -Milliseconds $SettleMs
    $null = Sample 'broadcast WM_SETTINGCHANGE(ImmersiveColorSet)'

    [void][Native.TbExperiment]::DwmFlush()
    Start-Sleep -Milliseconds $SettleMs
    $null = Sample 'DwmFlush()'

    # 往返切换：完整再走一轮（观察滞后一拍现象）
    $nextMode = if ($Rounds % 2 -eq 1) { 'light' } else { 'dark' }
    Set-Theme $nextMode
    Start-Sleep -Milliseconds $SettleMs
    $null = Sample "round-trip: --set $nextMode"
}
finally {
    Set-ItemProperty -Path $personalize -Name SystemUsesLightTheme -Value $originalSystem -Type DWord
    Set-ItemProperty -Path $personalize -Name AppsUseLightTheme -Value $originalApps -Type DWord
    [Native.TbExperiment]::BroadcastSettingChange('ImmersiveColorSet')
    Write-Host ''
    Write-Host ("Restored original theme: System={0} Apps={1}" -f $originalSystem, $originalApps)
}
