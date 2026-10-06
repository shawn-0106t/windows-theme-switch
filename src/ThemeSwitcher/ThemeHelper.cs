using System.Runtime.InteropServices;
using System.Text;
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

    /// <summary>主任务栏窗口类名（TrayDocking 定位托盘屏幕时复用）。</summary>
    public const string TrayWndClass = "Shell_TrayWnd";

    private const string BroadcastParam = "ImmersiveColorSet";
    private const string SecondaryTrayWndClass = "Shell_SecondaryTrayWnd";
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint BroadcastTimeoutMs = 1000;

    // 任务栏窗口正常毫秒级响应，无需长超时。SetTheme 全程同步，最坏阻塞预算：
    // 2 轮 × (广播重试 2×1000ms + N 个任务栏窗口 × 200ms) + 120ms 延迟，正常情况 <150ms
    private const uint TaskbarNotifyTimeoutMs = 200;
    private const int DefaultReapplyDelayMs = 120;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    private readonly string _personalizePath;
    private readonly string _runKeyPath;
    private readonly bool _broadcast;
    private readonly int _reapplyDelayMs;

    public ThemeHelper(
        string? personalizePath = null,
        string? runKeyPath = null,
        bool broadcast = true,
        int reapplyDelayMs = DefaultReapplyDelayMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reapplyDelayMs);
        _personalizePath = personalizePath ?? DefaultPersonalizePath;
        _runKeyPath = runKeyPath ?? DefaultRunKeyPath;
        _broadcast = broadcast;
        _reapplyDelayMs = reapplyDelayMs;
    }

    public ThemeState ReadState()
    {
        return new ThemeState(
            ReadLightFlag("SystemUsesLightTheme"),
            ReadLightFlag("AppsUseLightTheme"));
    }

    /// <summary>
    /// 写入主题值并广播。传 null 表示对应模式保持不变。
    /// 整个过程同步完成（含延迟补发），保证 CLI 进程退出前所有变更通知已发出。
    /// </summary>
    public void SetTheme(bool? systemLight, bool? appsLight)
    {
        if (systemLight is null && appsLight is null)
        {
            return;
        }

        WriteThemeValues(systemLight, appsLight);
        BroadcastSettingChange();
        NotifyTaskbarWindows();

        // 多屏下副屏任务栏经常不处理首次 ImmersiveColorSet 广播（Windows 上游 bug，
        // AutoDarkMode #1172 同款）：延迟重写相同值并二次广播，等价于"重新应用一次主题"。
        // 必须同步执行：SetTheme 的调用方（含 CLI）返回后进程可能随即退出。
        if (_reapplyDelayMs > 0)
        {
            Thread.Sleep(_reapplyDelayMs);
            // 延迟窗口内外部可能已写入新值（设置页/另一 CLI 实例）：让位新切换，不再覆盖
            if (!IsCurrentStateUnchanged(systemLight, appsLight))
            {
                return;
            }

            WriteThemeValues(systemLight, appsLight);
            BroadcastSettingChange();
            NotifyTaskbarWindows();
        }
    }

    /// <summary>本次 SetTheme 涉及的键在延迟后是否仍保持首次写入的值（未涉及的键忽略）。</summary>
    private bool IsCurrentStateUnchanged(bool? systemLight, bool? appsLight)
    {
        ThemeState current = ReadState();
        return (systemLight is not { } system || current.SystemUsesLightTheme == system)
            && (appsLight is not { } apps || current.AppsUseLightTheme == apps);
    }

    private void WriteThemeValues(bool? systemLight, bool? appsLight)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_personalizePath, writable: true);
        if (systemLight is { } system)
        {
            key.SetValue("SystemUsesLightTheme", system ? 1 : 0, RegistryValueKind.DWord);
        }

        if (appsLight is { } apps)
        {
            key.SetValue("AppsUseLightTheme", apps ? 1 : 0, RegistryValueKind.DWord);
        }
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

    /// <summary>
    /// 向主/副任务栏窗口定向补发一次 WM_SETTINGCHANGE：HWND_BROADCAST 之外再显式送达，
    /// 提高副屏任务栏（Shell_SecondaryTrayWnd）错过广播时的命中率。尽力而为，失败不影响切换结果。
    /// </summary>
    private void NotifyTaskbarWindows()
    {
        if (!_broadcast)
        {
            return;
        }

        foreach (IntPtr hwnd in EnumerateTaskbarWindows())
        {
            _ = SendMessageTimeout(
                hwnd, WmSettingChange, IntPtr.Zero, BroadcastParam,
                SmtoAbortIfHung, TaskbarNotifyTimeoutMs, out _);
        }
    }

    /// <summary>
    /// 枚举所有任务栏顶层窗口。EnumWindows 而非 FindWindow：多显示器时
    /// Shell_SecondaryTrayWnd 可能有多个实例（每块副屏一个）。
    /// </summary>
    private static List<IntPtr> EnumerateTaskbarWindows()
    {
        var handles = new List<IntPtr>();
        _ = EnumWindows((hwnd, _) =>
        {
            var className = new StringBuilder(256);
            if (GetClassName(hwnd, className, className.Capacity) > 0
                && className.ToString() is TrayWndClass or SecondaryTrayWndClass)
            {
                handles.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);
        return handles;
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

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int sizeOfValue);

    private const int DwmwaUseImmersiveDarkMode = 20;
}
