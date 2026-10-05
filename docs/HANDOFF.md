# HANDOFF — v1.0 交付后问题交接（#1 副屏任务栏 / #2 窗口贴靠托盘）

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v1.0 |
| 日期 | 2026-10-05 |
| 状态 | 待修复（本文档为交接基线，修复会话从这里开工） |
| 基线代码 | commit `d9214b6`（main，阶段 0–7 全部完成，CI 三门禁全绿，e2e 4/4 PASS） |
| 环境 | Windows 11（10.0.26300，25H2 内核）、双显示器、.NET 8 Desktop Runtime 已装 |

## 背景

v1.0 已交付：主窗口一键全切 + Windows/应用模式独立开关、托盘常驻（左键全切）、
开机自启、单实例、CLI（`--toggle/--set/--status`）。用户实测**大部分正常**，
以下两个问题为实测反馈，本文档固化复现信息、根因分析与候选方案，供下一轮实施。

---

## 问题 1：切换主题后副屏任务栏不跟随（仅任务栏，其余 UI 正常）

### 现象与复现

- 主屏任务栏、开始菜单、窗口标题栏、应用全部正常切换
- **副屏任务栏**保持切换前的明暗外观，不随 `WM_SETTINGCHANGE` 更新
- 后续任意一次新的主题切换，副屏任务栏更新为**上一次**的目标值（滞后一拍）或仍不动（待确认具体表现，修复时留意）
- 环境：Win11 26300 + 双屏

### 根因分析（已调研，可信度较高）

这是 **Windows 自身的已知 bug**，非本项目实现错误：

- AutoDarkMode 同款问题：[Windows-Auto-Night-Mode #1172](https://github.com/AutoDarkMode/Windows-Auto-Night-Mode/issues/1172)（"Auto changed theme is typically partial in multi monitor"——主屏正常、副屏任务栏滞留，重启 explorer 才恢复）
- WinDynamicDesktop [#656](https://github.com/t1m0thyj/WinDynamicDesktop/issues/656)、Reddit r/WindowsHelp 均有相同症状报告
- 维护者结论：`HWND_BROADCAST` 广播后，副屏任务栏（Explorer 的副任务栏实例）经常不处理该次 `ImmersiveColorSet` 变更；AutoDarkMode 的解法是 **"aggressive DWM refresh"**（设置项"More aggressive theme switch"）：切换后重新应用一次主题并强制完整 DWM 刷新；托盘另有手动入口 "Try to fix the theme"

### 候选方案（按侵入性排序，建议按序试验）

**方案 A：双写 + 二次广播（首选，零副作用）**
`ThemeHelper.SetTheme`（src/ThemeSwitcher/ThemeHelper.cs:48）写注册表并广播一次后，
延迟 ~100–150ms 将两个 DWORD **重写一次相同值**并再广播一次。
原理：副屏任务栏对"第二次"变更的响应路径不同（AutoDarkMode "re-apply theme" 的轻量等价物）。
实现要点：`BroadcastSettingChange()`（ThemeHelper.cs:110）已有重试，此处是**写+播**整体重复，
需要小 心与 `MainForm.WndProc` 自刷新（MainForm.cs:243 附近）的交互——重入刷新无害但应确认。

**方案 B：显式向任务栏窗口补发消息**
`FindWindow("Shell_TrayWnd", null)` + 枚举副屏任务栏窗口
（Win10 为 `Shell_SecondaryTrayWnd`；**Win11 26300 的窗口类需 Spy++ 实测确认**，
也可 `EnumWindows` 过滤 Explorer 进程的顶层窗口），对每个句柄
`SendMessageTimeout(WM_SETTINGCHANGE, 0, "ImmersiveColorSet", ...)`。
用 SendMessageTimeout 而非 PostMessage（lParam 字符串生命周期安全）。

**方案 C（AutoDarkMode 同款兜底，重）：**
- 设置项"激进刷新"：每次切换后执行 A（或 A+B）双保险
- 托盘菜单加"修复主题"手动项：重写注册表 + 广播，供偶发残留时一键修复
- 明确**不做**自动重启 explorer（破坏性，AutoDarkMode 也不默认做）

### 验证方法

- 双屏真机：主窗口全切 / 托盘左键全切 / CLI `--toggle` 三条路径 × 深→浅、浅→深双向，逐次检查副屏任务栏
- 单屏回归：确认 A/B 不引入闪烁、重复刷新或性能回退
- 已知 Windows 行为：仅**任务栏**滞后，开始菜单/Explorer 窗口不受此 bug 影响，可作检查排除项

### 验收标准

- 双屏下全部三条切换路径，副屏任务栏与主屏同步即时变色（允许 ≤200ms 的 A 方案二次广播延迟）
- 单屏行为与 v1.0 完全一致；CI 三门禁 + e2e 仍全绿

---

## 问题 2：主窗口启动时紧靠托盘图标

### 需求

主窗口显示位置从"屏幕居中"改为**紧靠系统托盘图标**（右下角任务栏托盘附近），
符合"托盘工具点击即出、视线不跳"的直觉（EarTrumpet / PowerToys 弹窗同款行为）。

### 候选方案

**方案 1（推荐）：Shell_TrayWnd 矩形推算右下角**
Win11 托盘永远位于任务栏右端，"紧靠托盘"≈"任务栏右端 + 工作区右下角对齐"：

1. `FindWindow("Shell_TrayWnd", null)` 拿任务栏句柄
2. `Screen.FromHandle(trayHwnd)` 得到任务栏所在屏幕（通常主屏）
3. `Location = workingArea.BottomRight - windowSize - margin(≈8px)`（任务栏在底部时；
   左/右侧任务栏做同侧对齐兜底即可，Win11 官方仅支持底部）
4. `StartPosition = Manual`（MainForm.cs:63 当前为 `CenterScreen`，需改）

优点：零 P/Invoke 复杂度（FindWindow 一条）、不依赖 .NET NotifyIcon 内部实现、跨 DPI 稳健
（PerMonitorV2 下直接设像素 Location，WinForms 自动处理跨屏缩放，允许 ±几 px 偏差）。

**方案 2（精确到图标，仅当方案 1 视觉不够贴）：`Shell_NotifyIcon(NIM_GETRECT)`**
需要 NOTIFYICONIDENTIFIER 的 hWnd+uID 与**注册图标时完全一致**，而 .NET `NotifyIcon`
（TrayController.cs）的 id/窗口是内部私有字段——需反射读取（脆弱，随 .NET 更新可能碎）
或自建"影子图标"注册→测量→删除（会闪一个图标位）。代价大，默认不选。

### 决策点（实施时定，默认取括号内）

- 何时贴靠：仅启动首次（默认）vs 每次 `ShowAndActivate`（MainForm.cs:127，第二实例激活）
  也重新贴靠 → **建议每次显示都贴靠**（用户不会拖动这个小窗，行为可预测最重要）
- 用户手动拖走后：本次会话内尊重用户位置（需要加"已拖动"标志）vs 依旧回贴
  → **v1.1 先不做**，保持每次贴靠，有反馈再加

### 验收标准

- 启动（含 `--minimized` 后托盘"显示主窗口"）与第二实例激活，窗口均出现在任务栏托盘
  正上方（底部任务栏情形），完全可见、不出屏、不遮任务栏
- 150% DPI 与双屏（任务栏在主屏）情形位置正确；CI 三门禁全绿

---

## 相关代码索引（基线 commit `d9214b6`）

| 位置 | 说明 |
| --- | --- |
| src/ThemeSwitcher/ThemeHelper.cs:48 `SetTheme` | 写注册表 + 广播入口（问题 1 改造点） |
| src/ThemeSwitcher/ThemeHelper.cs:110 `BroadcastSettingChange` | 广播 + 失败重试一次 |
| src/ThemeSwitcher/MainForm.cs:63 `StartPosition` | 居中改贴靠（问题 2 改造点） |
| src/ThemeSwitcher/MainForm.cs:127 `ShowAndActivate` | 第二实例激活/托盘"显示主窗口"路径（问题 2 贴靠调用点） |
| src/ThemeSwitcher/MainForm.cs:164 `RefreshAll` | 广播重入的自刷新（问题 1 方案 A 需确认交互） |
| src/ThemeSwitcher/TrayController.cs:46 | 托盘左键全切（问题 1 三条验证路径之一） |
| scripts/e2e-verify.ps1 | 端到端回归（改动后必须复跑；副屏视觉需真机人工确认） |
| docs/SPEC.md | 修复后按需 bump 版本并补变更记录；FR/验收标准相应增补 |

## 实施注意

- 问题 1 属上游 bug 的**缓解**而非根除：HANDOFF 验收以"三路径即时同步"为准；
  若 A+B 组合仍偶发残留，落地方案 C 的手动"修复主题"托盘项即可收敛
- 两个问题互相独立，可拆两个 commit（`fix: secondary taskbar sync` / `feat: dock window near tray`）
- 修复完成后本 HANDOFF 归档：问题节标记 resolved + commit 号，或移入 `docs/archive/`
