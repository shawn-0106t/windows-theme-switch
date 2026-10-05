# SPEC — Windows 主题极简切换器（windows-theme-switch）

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v1.1 |
| 日期 | 2026-10-05 |
| 状态 | 已确认（需求冻结基线） |
| 适用平台 | Windows 11（Windows 10 1809+ 原理兼容，非测试目标） |

## 1. 背景与目标

Windows 11 切换深色/浅色主题需要打开 `设置 > 个性化 > 颜色` 多级页面，操作繁琐。本项目提供一个**极简的手动切换 GUI**：一键（或托盘单击）完成主题切换，无需打开系统设置。

项目定位为**极简工具**，与社区成熟项目 [AutoDarkMode/Windows-Auto-Night-Mode](https://github.com/AutoDarkMode/Windows-Auto-Night-Mode)（9.7k+ stars，GPL-3.0，定时调度型）形成差异化：不做定时/自动化，专注"最快的手动切换"体验。

## 2. 已确认决策

以下决策已在需求阶段确认，未经讨论不得变更：

| 决策项 | 结论 | 备注 |
| --- | --- | --- |
| 技术栈 | C# WinForms，`net8.0-windows` | 与 PowerToys 同属 .NET 生态；WinForms 是唯一内置托盘（`NotifyIcon`）的 .NET GUI 框架 |
| 应用形态 | 主窗口 + 系统托盘常驻 | 点窗口关闭按钮 = 最小化到托盘；托盘菜单"退出"才真正结束进程 |
| 切换粒度 | 一键全切大按钮 + 两组独立开关 | 独立开关支持"系统深色 + 应用浅色"等组合 |
| 发布方式 | 框架依赖单文件 exe（几百 KB） | 运行依赖 .NET 8 Desktop Runtime；README 提供 self-contained 便携版命令 |
| 第三方依赖 | **零 NuGet 依赖** | 注册表/托盘/广播均为 .NET BCL 与 Win32 内置能力 |

调研结论备查：Windows 11 系统设置应用为 C++/WinRT + UWP XAML（闭源，系统专用路线）；微软官方开源对应物是 PowerToys **Light Switch** 模块（C#/.NET）；两者均不改变本项目的选型结论。

## 3. 功能需求（FR）

| 编号 | 需求 | 说明 |
| --- | --- | --- |
| FR-1 | 一键全切 | 主窗口大按钮，一次点击将 Windows 模式与应用模式**同时**切到深色或浅色（两者取反）。按钮文案随当前状态：同为浅色显示"切换到深色模式"，同为深色显示"切换到浅色模式"，混合状态显示"反转全部主题" |
| FR-2 | 独立开关 × 2 | 「Windows 模式」（任务栏/开始菜单/系统 UI）与「应用模式」各一组浅色/深色互斥按钮，可独立设置任意组合 |
| FR-3 | 状态显示 | 主窗口实时显示两者当前状态（如 `Windows 模式：深色 ｜ 应用模式：浅色`） |
| FR-4 | 托盘常驻 | ① 关闭按钮 → 隐藏到托盘不退出；② 托盘图标**左键单击 = 一键全切**（最快操作路径）；③ 右键菜单：显示主窗口 / 切换主题 / 开机自启（toggle） / 退出；④ 托盘图标颜色随当前主题明暗变化（GDI+ 动态绘制，无二进制资源） |
| FR-5 | 开机自启 | 托盘菜单内 toggle 控制，写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，默认关闭 |
| FR-6 | 单实例 | 命名 Mutex 防双开；重复启动时激活已有实例窗口后自行退出 |
| FR-7 | `--minimized` 启动参数 | 启动直接进托盘不显示窗口（供开机自启使用） |
| FR-8 | 即时生效 | 切换后任务栏、开始菜单及支持主题的应用无需注销/重启即时生效 |
| FR-9 | 标题栏跟随 | 本应用自身标题栏明暗随系统主题（`DWMWA_USE_IMMERSIVE_DARK_MODE`） |
| FR-10 | CLI 模式 | 无窗口执行后退出，输出 ASCII（如 `System=Dark Apps=Light`）：`--toggle` 一键全切 / `--set dark\|light` 双模式同设 / `--status` 查询状态。供脚本自动化与端到端验证使用；不受单实例限制（与已运行 GUI 实例通过广播自然同步） |

## 4. 非功能需求（NFR）

| 编号 | 需求 |
| --- | --- |
| NFR-1 | 应用本体（`src/`）零第三方 NuGet 依赖；零 license 合规风险（不复制 AutoDarkMode 等 GPL 项目代码）。单元测试工具链（xunit）仅存在于 `tests/`，不影响发布产物 |
| NFR-2 | 无需管理员权限（全部操作在 HKCU 下） |
| NFR-3 | `dotnet build` 零警告（`TreatWarningsAsErrors=true`），CI 四门禁可通过 |
| NFR-4 | 冷启动到可操作 < 1 秒（框架依赖发布） |
| NFR-5 | 单实例进程常驻内存 < 30 MB（Private Bytes 口径；WorkingSet 受共享运行时页影响仅作参考） |

## 5. 非目标（Non-goals）

以下功能**明确不做**（避免范围蔓延；有需求直接用 AutoDarkMode / PowerToys Light Switch）：

- 定时切换、日出日落、环境光传感器等自动化调度
- 壁纸 / 鼠标指针 / accent color / `.theme` 文件联动
- 多语言界面、自动更新、MSIX 打包与商店分发
- Windows 10 专门适配（原理 1809+ 通用，但不做回归测试）

## 6. 技术原理

### 6.1 主题存储与切换

系统主题状态存储于当前用户注册表：

```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
    SystemUsesLightTheme  (REG_DWORD)   Windows 模式：1=浅色，0=深色
    AppsUseLightTheme     (REG_DWORD)   应用模式：1=浅色，0=深色
```

写入后需广播系统设置变更通知，使 Explorer / 任务栏 / 支持的应用即时重绘：

```
SendMessageTimeout(
    HWND_BROADCAST,          // 0xFFFF
    WM_SETTINGCHANGE,        // 0x001A
    0,
    "ImmersiveColorSet",     // lParam 字符串
    SMTO_ABORTIFHUNG, 1000, &result)
```

以上即系统设置页自身的公开机制（注册表路径与广播参数均为 Windows 官方文档化行为），无未公开 API。

### 6.2 其他机制

- **单实例**：命名 `Mutex`（`Local\` 前缀，按用户会话隔离）
- **开机自启**：`HKCU\...\Run` 写入 `"ThemeSwitcher" = "<exe路径>" --minimized`
- **暗色标题栏**：`DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE=20, ...)`，随 FR-1/FR-2 切换同步刷新
- **托盘图标**：`NotifyIcon` + GDI+ 运行时绘制（半明半暗圆点，明暗随主题反转），避免在 repo 维护二进制图标资源

## 7. 验收标准

1. 主窗口一键全切后 1 秒内，任务栏与开始菜单完成深浅色切换；`reg query` 断言两个 DWORD 值正确
2. 独立开关可设置 4 种组合（系统×应用），`reg query` 逐一断言
3. 关闭窗口后托盘图标存在，左键单击完成全切，右键菜单四项功能全部可用
4. 开机自启 toggle 开/关后，`HKCU\...\Run` 键正确写入/删除
5. 二次启动不出现第二个窗口/托盘图标，已有窗口被前置
6. `--minimized` 启动无窗口，仅托盘
7. `dotnet build` / `dotnet test` / `dotnet format --verify-no-changes` 全部零错误零警告
8. 全部源码通过 code-reviewer 子代理独立审查（证伪导向）无 P0/P1 问题

## 8. 变更记录

- v1.1（2026-10-05）：FR-1 补充混合状态按钮文案"反转全部主题"；新增 FR-10 CLI 模式（端到端验证的"命令行触发"路径落地为正式功能）；NFR-1 措辞明确 xunit 仅限测试工程
- v1.0（2026-10-05）：需求冻结基线
