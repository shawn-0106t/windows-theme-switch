using System.Runtime.InteropServices;

namespace ThemeSwitcher;

/// <summary>
/// 主窗口：一键全切大按钮 + Windows/应用模式独立开关（FR-1/2/3），
/// 关闭到托盘常驻（FR-4），界面与标题栏明暗跟随系统模式（FR-9）。
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
    private static readonly Color DarkSurface = Color.FromArgb(32, 32, 32);
    private static readonly Color DarkButtonFace = Color.FromArgb(58, 58, 58);
    private static readonly Color DarkText = Color.FromArgb(241, 241, 241);
    private static readonly Color HintText = Color.FromArgb(130, 130, 130);

    private const string ImmersiveColorSet = "ImmersiveColorSet";
    private const int WmSettingChange = 0x001A;

    private readonly ThemeHelper _theme = new();
    private readonly TrayController _tray;
    private readonly Label _lblStatus = new();
    private readonly Button _btnToggleAll = new();
    private readonly Label _lblSystem = new();
    private readonly Button _btnSystemLight = new();
    private readonly Button _btnSystemDark = new();
    private readonly Label _lblApps = new();
    private readonly Button _btnAppsLight = new();
    private readonly Button _btnAppsDark = new();
    private readonly Label _lblHint = new();
    private bool _startMinimized;
    private bool _uiDark;
    private bool _exitRequested;
    private bool _hintShown;

    public MainForm(bool startMinimized)
    {
        _startMinimized = startMinimized;
        InitializeComponent();

        _tray = new TrayController(
            showWindow: ShowAndActivate,
            toggleTheme: ToggleAllThemes,
            isAutoStart: () => _theme.IsAutoStartEnabled(),
            setAutoStart: _theme.SetAutoStart,
            exitApp: RequestExit);

        _ = Handle; // 提前创建句柄：WndProc 才能收到主题广播，DWM 标题栏才可设置
        DockNearTray();
        RefreshAll();
    }

    /// <summary>按任务栏位置把窗口对齐到托盘附近（FR-11）。每次显示都重新贴靠，行为可预测。</summary>
    private void DockNearTray()
    {
        Location = TrayDocking.GetDockedLocation(Size);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // 构造期取到的 Size 可能尚未完成 DPI/字体缩放（右下锚点会溢出屏幕）；
        // Load 时缩放已结束且窗口仍不可见，此处校正无闪烁，兜住首次显示路径
        DockNearTray();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(7F, 17F);
        Text = "主题切换器";
        ClientSize = new Size(336, 244);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual; // 位置由贴靠托盘逻辑接管（FR-11）
        Font = new Font("Microsoft YaHei UI", 9F);

        _lblStatus.SetBounds(16, 12, 304, 24);
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);

        _btnToggleAll.SetBounds(16, 44, 304, 48);
        _btnToggleAll.FlatStyle = FlatStyle.Flat;
        _btnToggleAll.FlatAppearance.BorderSize = 0;
        _btnToggleAll.Cursor = Cursors.Hand;
        _btnToggleAll.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        _btnToggleAll.Click += (_, _) => ToggleAllThemes();

        _lblSystem.Text = "Windows 模式";
        _lblSystem.SetBounds(16, 106, 120, 30);
        _lblSystem.TextAlign = ContentAlignment.MiddleLeft;
        _btnSystemLight.SetBounds(140, 106, 88, 30);
        _btnSystemLight.Text = "浅色";
        _btnSystemDark.SetBounds(232, 106, 88, 30);
        _btnSystemDark.Text = "深色";

        _lblApps.Text = "应用模式";
        _lblApps.SetBounds(16, 146, 120, 30);
        _lblApps.TextAlign = ContentAlignment.MiddleLeft;
        _btnAppsLight.SetBounds(140, 146, 88, 30);
        _btnAppsLight.Text = "浅色";
        _btnAppsDark.SetBounds(232, 146, 88, 30);
        _btnAppsDark.Text = "深色";

        foreach (Button button in new[] { _btnSystemLight, _btnSystemDark, _btnAppsLight, _btnAppsDark })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
        }

        _btnSystemLight.Click += (_, _) => ApplyChange(systemLight: true, appsLight: null);
        _btnSystemDark.Click += (_, _) => ApplyChange(systemLight: false, appsLight: null);
        _btnAppsLight.Click += (_, _) => ApplyChange(systemLight: null, appsLight: true);
        _btnAppsDark.Click += (_, _) => ApplyChange(systemLight: null, appsLight: false);

        _lblHint.SetBounds(16, 188, 304, 36);
        _lblHint.ForeColor = HintText;
        _lblHint.Text = "提示：点击窗口关闭按钮会最小化到系统托盘；在托盘图标上右键可选择退出。";
        _lblHint.Visible = false;

        Controls.AddRange(
        [
            _lblStatus,
            _btnToggleAll,
            _lblSystem,
            _btnSystemLight,
            _btnSystemDark,
            _lblApps,
            _btnAppsLight,
            _btnAppsDark,
            _lblHint,
        ]);

        ResumeLayout();
    }

    /// <summary>由激活监听线程（第二实例启动时）调用，把窗口带到前台。</summary>
    public void ShowAndActivate()
    {
        _startMinimized = false;
        DockNearTray();
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
    }

    // ---- 主题操作（FR-1/FR-2）----

    private void ToggleAllThemes()
    {
        _theme.ToggleAll();
        OnUserThemeApplied();
    }

    private void ApplyChange(bool? systemLight, bool? appsLight)
    {
        _theme.SetTheme(systemLight, appsLight);
        OnUserThemeApplied();
    }

    private void OnUserThemeApplied()
    {
        RefreshAll();
        if (!_hintShown)
        {
            _hintShown = true;
            _lblHint.Visible = true;
        }
    }

    /// <summary>从注册表重读状态并刷新界面、标题栏与托盘；自身切换与外部修改共用此路径。</summary>
    private void RefreshAll()
    {
        ThemeState state = _theme.ReadState();
        bool dark = !state.SystemUsesLightTheme;

        ApplyUiTheme(dark);
        ThemeHelper.ApplyTitleBarTheme(this, dark);
        _tray.UpdateIcon(dark);

        _lblStatus.Text =
            $"Windows 模式：{ModeName(state.SystemUsesLightTheme)}　｜　应用模式：{ModeName(state.AppsUseLightTheme)}";

        _btnToggleAll.Text = state.SystemUsesLightTheme == state.AppsUseLightTheme
            ? (state.SystemUsesLightTheme ? "切换到深色模式" : "切换到浅色模式")
            : "反转全部主题";

        MarkSelected(_btnSystemLight, state.SystemUsesLightTheme);
        MarkSelected(_btnSystemDark, !state.SystemUsesLightTheme);
        MarkSelected(_btnAppsLight, state.AppsUseLightTheme);
        MarkSelected(_btnAppsDark, !state.AppsUseLightTheme);
    }

    private static string ModeName(bool light) => light ? "浅色" : "深色";

    private void ApplyUiTheme(bool dark)
    {
        _uiDark = dark;
        BackColor = dark ? DarkSurface : SystemColors.Control;
        ForeColor = dark ? DarkText : SystemColors.ControlText;
        _lblStatus.ForeColor = ForeColor;
        _lblSystem.ForeColor = ForeColor;
        _lblApps.ForeColor = ForeColor;
        _lblHint.ForeColor = HintText;
        _btnToggleAll.BackColor = AccentColor;
        _btnToggleAll.ForeColor = Color.White;
    }

    private void MarkSelected(Button button, bool selected)
    {
        if (selected)
        {
            button.BackColor = AccentColor;
            button.ForeColor = Color.White;
        }
        else
        {
            button.BackColor = _uiDark ? DarkButtonFace : SystemColors.Control;
            button.ForeColor = _uiDark ? DarkText : SystemColors.ControlText;
        }
    }

    // ---- 托盘与退出（FR-4）----

    private void RequestExit()
    {
        _exitRequested = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tray.Dispose();
        base.OnFormClosed(e);
    }

    // ---- 外部主题变更感知：系统设置页修改后自动刷新（阶段 4）----

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmSettingChange && m.LParam != IntPtr.Zero
            && Marshal.PtrToStringAuto(m.LParam) == ImmersiveColorSet)
        {
            RefreshAll();
        }

        base.WndProc(ref m);
    }

    protected override void SetVisibleCore(bool value)
    {
        // --minimized 启动：吞掉首次显示请求，仅保持句柄（托盘/广播可用）
        if (_startMinimized && value)
        {
            value = false;
            if (!IsHandleCreated)
            {
                _ = Handle;
            }
        }

        base.SetVisibleCore(value);
    }
}
