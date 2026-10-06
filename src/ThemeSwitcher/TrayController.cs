using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ThemeSwitcher;

/// <summary>
/// 系统托盘（FR-4）：左键一键全切，右键菜单（显示/切换/修复主题/开机自启/退出），
/// 图标随当前主题重绘（左半圆 = 当前主题色）。
/// </summary>
internal sealed class TrayController : IDisposable
{
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
    private static readonly Color DarkFill = Color.FromArgb(31, 31, 31);
    private static readonly Color LightFill = Color.FromArgb(243, 243, 243);

    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _autoStartItem = new("开机自启");
    private readonly Func<bool> _isAutoStart;
    private readonly Action<bool> _setAutoStart;
    private IntPtr _iconHandle;

    public TrayController(
        Action showWindow,
        Action toggleTheme,
        Action repairTheme,
        Func<bool> isAutoStart,
        Action<bool> setAutoStart,
        Action exitApp)
    {
        _isAutoStart = isAutoStart;
        _setAutoStart = setAutoStart;

        _autoStartItem.Click += (_, _) => _setAutoStart(!_isAutoStart());
        _menu.Opening += (_, _) => _autoStartItem.Checked = _isAutoStart();
        _menu.Items.AddRange(
        [
            new ToolStripMenuItem("显示主窗口", null, (_, _) => showWindow()),
            new ToolStripMenuItem("切换主题", null, (_, _) => toggleTheme()),
            new ToolStripMenuItem("修复主题", null, (_, _) => repairTheme()),
            new ToolStripSeparator(),
            _autoStartItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("退出", null, (_, _) => exitApp()),
        ]);

        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                toggleTheme();
            }
        };
        _notifyIcon.Visible = true;
    }

    /// <summary>按当前明暗重绘托盘图标；左半圆颜色代表当前主题。</summary>
    public void UpdateIcon(bool dark)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var circle = new Rectangle(2, 2, size - 4, size - 4);
            using var rightBrush = new SolidBrush(dark ? LightFill : DarkFill);
            using var leftBrush = new SolidBrush(dark ? DarkFill : LightFill);
            using var ringPen = new Pen(AccentColor, 3);
            graphics.FillEllipse(rightBrush, circle);
            graphics.FillPie(leftBrush, circle, 90, 180);
            graphics.DrawEllipse(ringPen, circle);
        }

        IntPtr handle = bitmap.GetHicon();
        _notifyIcon.Icon = Icon.FromHandle(handle);
        _notifyIcon.Text = $"主题切换器 - 当前{(dark ? "深色" : "浅色")}";
        ReleaseIconHandle();
        _iconHandle = handle;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _notifyIcon.Dispose();
        _menu.Dispose();
        ReleaseIconHandle();
    }

    /// <summary>GetHicon 创建的 GDI 句柄必须用 DestroyIcon 释放，否则每次重绘都泄漏。</summary>
    private void ReleaseIconHandle()
    {
        if (_iconHandle != IntPtr.Zero)
        {
            _ = DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
