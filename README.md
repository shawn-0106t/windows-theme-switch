# windows-theme-switch

极简的 Windows 11 深色/浅色主题手动切换工具。一键（或托盘单击）切换 **Windows 模式**（任务栏、开始菜单等系统界面）与**应用模式**，无需打开 `设置 > 个性化 > 颜色`。

## 功能

- **一键全切**：大按钮同时切换 Windows 模式与应用模式
- **独立开关**：Windows 模式 / 应用模式可分别设置，支持"系统深色 + 应用浅色"等任意组合
- **托盘常驻**：点窗口 ✕ 最小化到系统托盘；托盘**左键单击 = 一键全切**；右键菜单支持显示主窗口、切换主题、修复主题、开机自启开关、退出
- **即时生效**：切换后任务栏、开始菜单与支持主题的应用立刻跟随，无需注销
- **外部感知**：在系统设置页手动改主题时，本工具的状态与托盘图标自动同步
- **单实例**：重复启动自动激活已有窗口
- **CLI 模式**：支持脚本化调用（见下文）

不做定时/日出日落/壁纸联动等自动化——那是 [Auto Dark Mode](https://github.com/AutoDarkMode/Windows-Auto-Night-Mode) 的领域，本项目专注"最快的手动切换"。

## 环境要求

- Windows 10 1809+（主要面向 Windows 11）
- 框架依赖版需要 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)；self-contained 版无需任何运行时

## 构建

```powershell
dotnet build          # Debug
dotnet test           # 单元测试
pwsh scripts/e2e-verify.ps1   # 端到端验证（会短暂真实切换主题并自动恢复）
```

## 发布

```cmd
scripts\publish.cmd
```

产物为单文件 exe（几百 KB，框架依赖），输出路径见脚本提示。需要免 .NET Runtime 的便携版时，取消脚本末尾注释行的注释执行即可。

## 命令行用法

| 命令 | 作用 |
| --- | --- |
| `ThemeSwitcher.exe` | 打开主窗口 |
| `ThemeSwitcher.exe --minimized` | 启动后直接进入托盘（开机自启即用此参数） |
| `ThemeSwitcher.exe --toggle` | Windows 模式 + 应用模式同时取反，然后退出 |
| `ThemeSwitcher.exe --set dark` | 两个模式都设为深色，然后退出 |
| `ThemeSwitcher.exe --set light` | 两个模式都设为浅色，然后退出 |
| `ThemeSwitcher.exe --set-sys dark\|light` | 仅设置 Windows 模式，然后退出 |
| `ThemeSwitcher.exe --set-apps dark\|light` | 仅设置应用模式，然后退出 |
| `ThemeSwitcher.exe --repair` | 重新应用当前主题（任务栏偶发不跟随时的一键修复，见下文"已知限制"） |
| `ThemeSwitcher.exe --status` | 输出当前状态（`System=Dark Apps=Light`） |

## 工作原理

主题状态存储于当前用户注册表（无需管理员权限）：

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
    SystemUsesLightTheme  DWORD   Windows 模式：1=浅色，0=深色
    AppsUseLightTheme     DWORD   应用模式：1=浅色，0=深色
```

写入后向系统广播 `WM_SETTINGCHANGE("ImmersiveColorSet")`，Explorer 与支持的应用即时重绘——与系统设置页自身使用的机制相同，均为 Windows 官方文档化行为。

## 已知限制（Windows 上游 bug）

多显示器下任务栏对第三方主题切换的响应由 Explorer 内部状态机决定，存在已知上游问题（[AutoDarkMode #1172](https://github.com/AutoDarkMode/Windows-Auto-Night-Mode/issues/1172) 同款）。本工具内置的缓解实测效果：

- **CLI 方式切换**（`--toggle` / `--set ...`，无 GUI 实例运行时）：双屏任务栏稳定即时跟随
- **GUI 常驻切换**（托盘左键 / 主窗口按钮）：副屏任务栏正常；主屏任务栏偶尔滞后一拍（显示上一次的颜色，再切一次即跟上）
- 任务栏完全不动时：右键托盘试一次**"修复主题"**（或命令行 `--repair`）；罕见的深层冻结态下连系统设置页切换都唤不醒，此时重启资源管理器（任务管理器结束 `explorer.exe` 后重新运行）是最后手段——这是 Windows 自身 bug 的边界，本工具无法代劳

完整诊断数据（触发规律、已证伪的 workaround、两种坏状态形态）见 `docs/HANDOFF.md`。

## CI

GitHub Actions（`.github/workflows/ci.yml`）运行三道门禁：`dotnet format --verify-no-changes`、`dotnet build`（警告视为错误）、`dotnet test`。应用本体零第三方 NuGet 依赖，故无锁文件冻结门禁。

## License

[MIT](LICENSE)
