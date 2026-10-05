using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ThemeSwitcher;

/// <summary>个人主题状态。true = 浅色，false = 深色。</summary>
public readonly record struct ThemeState(bool SystemUsesLightTheme, bool AppsUseLightTheme);

/// <summary>
/// 读写 HKCU 主题注册表并广播系统设置变更（Windows 模式与应用模式）。
/// 机制均为官方文档化行为：Themes\Personalize 键 + WM_SETTINGCHANGE("ImmersiveColorSet")。
/// 注册表路径可注入（单元测试用沙盒键），广播可关闭（避免测试干扰桌面）。
/// </summary>
public sealed class ThemeHelper
{
    public const string DefaultPersonalizePath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public const string DefaultRunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string AutoStartValueName = "ThemeSwitcher";

    private const string BroadcastParam = "ImmersiveColorSet";
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint BroadcastTimeoutMs = 1000;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    private readonly string _personalizePath;
    private readonly string _runKeyPath;
    private readonly bool _broadcast;

    public ThemeHelper(string? personalizePath = null, string? runKeyPath = null, bool broadcast = true)
    {
        _personalizePath = personalizePath ?? DefaultPersonalizePath;
        _runKeyPath = runKeyPath ?? DefaultRunKeyPath;
        _broadcast = broadcast;
    }

    public ThemeState ReadState()
    {
        return new ThemeState(
            ReadLightFlag("SystemUsesLightTheme"),
            ReadLightFlag("AppsUseLightTheme"));
    }

    /// <summary>写入主题值并广播。传 null 表示对应模式保持不变。</summary>
    public void SetTheme(bool? systemLight, bool? appsLight)
    {
        if (systemLight is null && appsLight is null)
        {
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_personalizePath, writable: true);
        if (systemLight is { } system)
        {
            key.SetValue("SystemUsesLightTheme", system ? 1 : 0, RegistryValueKind.DWord);
        }

        if (appsLight is { } apps)
        {
            key.SetValue("AppsUseLightTheme", apps ? 1 : 0, RegistryValueKind.DWord);
        }

        BroadcastSettingChange();
    }

    /// <summary>Windows 模式与应用模式同时取反（一键全切）。</summary>
    public void ToggleAll()
    {
        ThemeState state = ReadState();
        SetTheme(!state.SystemUsesLightTheme, !state.AppsUseLightTheme);
    }

    // ---- 开机自启（HKCU Run，无需管理员权限）----

    public bool IsAutoStartEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
        return key?.GetValue(AutoStartValueName) is string;
    }

    public void SetAutoStart(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
        if (enabled)
        {
            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法获取当前可执行文件路径");
            key.SetValue(AutoStartValueName, $"\"{exePath}\" --minimized", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        }
    }

    // ---- 内部实现 ----

    /// <summary>缺键、缺值或类型异常一律按浅色处理（与 Windows 默认一致）。</summary>
    private bool ReadLightFlag(string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_personalizePath);
        object? raw = key?.GetValue(valueName);
        return raw is not int flag || flag != 0;
    }

    private void BroadcastSettingChange()
    {
        if (!_broadcast)
        {
            return;
        }

        // 系统忙时广播可能超时，重试一次（尽力而为）
        if (!BroadcastOnce())
        {
            _ = BroadcastOnce();
        }
    }

    private bool BroadcastOnce()
    {
        return SendMessageTimeout(
            HwndBroadcast, WmSettingChange, IntPtr.Zero, BroadcastParam,
            SmtoAbortIfHung, BroadcastTimeoutMs, out _) != IntPtr.Zero;
    }

    /// <summary>让窗口标题栏跟随深/浅色（DWM）。</summary>
    public static void ApplyTitleBarTheme(Form form, bool darkTitleBar)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }

        int value = darkTitleBar ? 1 : 0;
        _ = DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    [DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int sizeOfValue);

    private const int DwmwaUseImmersiveDarkMode = 20;
}
