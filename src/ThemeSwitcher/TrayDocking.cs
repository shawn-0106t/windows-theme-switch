using System.Runtime.InteropServices;

namespace ThemeSwitcher;

/// <summary>
/// 计算主窗口"紧靠任务栏托盘"的显示位置（FR-11）。
/// Win11 托盘永远位于任务栏右端：取任务栏所在屏幕的工作区，
/// 对齐到工作区靠任务栏一侧的角（底部任务栏 = 工作区右下角，即托盘正上方）。
/// 像素位置直接生效：PerMonitorV2 下 WinForms 自动处理跨屏缩放，允许 ±几 px 偏差。
/// </summary>
public static class TrayDocking
{
    private const int DefaultMargin = 8;

    /// <summary>返回窗口应出现的屏幕坐标；找不到任务栏窗口时按主屏右下角兜底。</summary>
    public static Point GetDockedLocation(Size windowSize, int margin = DefaultMargin)
    {
        IntPtr trayHwnd = FindWindow(ThemeHelper.TrayWndClass, null);
        Screen? screen = trayHwnd != IntPtr.Zero ? Screen.FromHandle(trayHwnd) : Screen.PrimaryScreen;
        if (screen is null)
        {
            return Point.Empty; // 连屏幕信息都拿不到（无桌面会话）：窗口落在主屏原点
        }

        return GetDockedLocation(screen.Bounds, screen.WorkingArea, windowSize, margin);
    }

    /// <summary>
    /// 纯计算：任务栏停靠边 = 工作区相对屏幕边界缺失的那一侧，窗口对齐该侧的角。
    /// 底部（Win11 唯一官方支持位）与无任务栏时均取右下角；左/右/顶部停靠仅做同侧兜底。
    /// </summary>
    public static Point GetDockedLocation(Rectangle screenBounds, Rectangle workingArea, Size windowSize, int margin)
    {
        bool leftDocked = workingArea.Left > screenBounds.Left;
        bool topDocked = workingArea.Top > screenBounds.Top;

        int x = leftDocked
            ? workingArea.Left + margin
            : workingArea.Right - windowSize.Width - margin;
        int y = topDocked
            ? workingArea.Top + margin
            : workingArea.Bottom - windowSize.Height - margin;

        return new Point(x, y);
    }

    [DllImport("user32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
}
