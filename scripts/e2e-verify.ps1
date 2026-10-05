#Requires -Version 7.0
<#
.SYNOPSIS
    端到端验证：通过 CLI 触发真实主题切换，断言注册表值，最后恢复初始状态。
.DESCRIPTION
    步骤：记录初始主题 → --set dark/light、--toggle（含混合状态）逐一断言
    HKCU 主题键值 → finally 恢复初始主题。断言失败时以非零码退出。
.EXAMPLE
    pwsh scripts/e2e-verify.ps1
    pwsh scripts/e2e-verify.ps1 -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repoRoot "src\ThemeSwitcher\bin\$Configuration\net8.0-windows\ThemeSwitcher.exe"
if (-not (Test-Path $exe)) {
    Write-Error "ThemeSwitcher.exe not found. Run 'dotnet build -c $Configuration' first."
}

$personalize = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$script:failures = 0

function Get-ThemeValue([string]$Name) {
    return [int](Get-ItemPropertyValue -Path $personalize -Name $Name)
}

function Assert-Theme([int]$ExpectedSystem, [int]$ExpectedApps, [string]$Label) {
    $sys = Get-ThemeValue 'SystemUsesLightTheme'
    $apps = Get-ThemeValue 'AppsUseLightTheme'
    if ($sys -eq $ExpectedSystem -and $apps -eq $ExpectedApps) {
        Write-Host "PASS  $Label (System=$sys Apps=$apps)"
    }
    else {
        Write-Warning "FAIL  $Label (expected System=$ExpectedSystem Apps=$ExpectedApps, got System=$sys Apps=$apps)"
        $script:failures++
    }
}

function Invoke-App([string[]]$AppArgs) {
    $proc = Start-Process -FilePath $exe -ArgumentList $AppArgs -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        Write-Error "ThemeSwitcher $($AppArgs -join ' ') exited with code $($proc.ExitCode)"
    }
}

$originalSystem = Get-ThemeValue 'SystemUsesLightTheme'
$originalApps = Get-ThemeValue 'AppsUseLightTheme'
Write-Host "Original theme: System=$originalSystem Apps=$originalApps"

try {
    Invoke-App @('--set', 'dark')
    Assert-Theme 0 0 '--set dark'

    Invoke-App @('--set', 'light')
    Assert-Theme 1 1 '--set light'

    Invoke-App @('--toggle')
    Assert-Theme 0 0 '--toggle (light -> dark)'

    # 混合状态：直接改注册表模拟，再全切应保持混合但两键翻转
    Set-ItemProperty -Path $personalize -Name SystemUsesLightTheme -Value 0 -Type DWord
    Set-ItemProperty -Path $personalize -Name AppsUseLightTheme -Value 1 -Type DWord
    Invoke-App @('--toggle')
    Assert-Theme 1 0 '--toggle keeps mixed state (flipped)'

    Invoke-App @('--status')
}
finally {
    Set-ItemProperty -Path $personalize -Name SystemUsesLightTheme -Value $originalSystem -Type DWord
    Set-ItemProperty -Path $personalize -Name AppsUseLightTheme -Value $originalApps -Type DWord
    Write-Host "Restored original theme: System=$originalSystem Apps=$originalApps"
}

if ($script:failures -gt 0) {
    Write-Error "$($script:failures) assertion(s) failed."
}

Write-Host 'E2E verification passed.'
