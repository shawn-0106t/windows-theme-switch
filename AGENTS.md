# AGENTS.md — windows-theme-switch

给在此仓库工作的 AI 编码代理的指导。

## 项目是什么

极简的 Windows 11 深色/浅色主题手动切换器：C# WinForms / `net8.0-windows`，主窗口一键全切 + Windows 模式/应用模式独立开关 + 托盘常驻。

核心机制（全部为 Windows 官方文档化行为）：写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` 下 `SystemUsesLightTheme` / `AppsUseLightTheme` 两个 DWORD，然后 `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet")` 广播。

## 必读文档（冲突时以 SPEC 为准）

| 文档 | 用途 |
| --- | --- |
| `docs/SPEC.md` | 需求基线、FR/NFR 编号、验收标准、变更记录 |
| `docs/PLAN.md` | 分阶段路线图与项目结构 |
| `docs/HANDOFF.md` | 当前待办问题（复现、根因、候选方案、验收标准） |
| `README.md` | 用户视角的用法与构建说明 |

## 硬性约束

1. `src/` **零第三方 NuGet 依赖**（`tests/` 仅 xunit 工具链）；引入新依赖前必须先说明理由并获得确认
2. **禁止复制 GPL 项目代码**：AutoDarkMode（GPL-3.0）等同类项目只可参考公开机制思路，不得搬运实现
3. 全部操作限 `HKCU`，不得要求管理员权限
4. csproj 已启用 `TreatWarningsAsErrors`：交付代码必须 build 零警告
5. **单元测试禁止触碰真实主题键**：`ThemeHelper` 构造参数可注入沙盒注册表键（现有测试用 `Software\ThemeSwitcher.Tests`）与 `broadcast: false`
6. `scripts/e2e-verify.ps1` 会**真实切换系统主题**：修改切换逻辑后必须复跑并确认 finally 恢复 + 广播路径未被破坏；新增切换路径时同步更新断言
7. 本机为中文 Windows（代码页 936/GBK）：源码/脚本一律 UTF-8 显式编码，脚本输出避免 emoji，PowerShell 用 pwsh 7+（5.1 需显式 `-Encoding UTF8`）

## 常用命令（Git Bash / pwsh）

```bash
dotnet build                        # 必须零警告
dotnet test                         # 11+ 单测
dotnet format --verify-no-changes   # CI 门禁
pwsh scripts/e2e-verify.ps1         # 端到端（切主题后自动恢复；需先 Debug build）
cmd //c scripts\\publish.cmd        # 发布单文件 exe（bin/Release/net8.0-windows/win-x64/publish/）
PYTHONUTF8=1 python scripts/make-icon.py   # 修改图标设计后重生成 src/ThemeSwitcher/app.ico
```

## 代码与提交约定

- 风格以 `.editorconfig` 为准（format 门禁强制；命名规则为 suggestion 级）
- commit：conventional commits 前缀（feat/fix/test/docs/chore/build）+ 中文描述，一个逻辑单元一个 commit
- main 已开 branch protection（required check = `build-test`）：改动建议走 PR，CI 在 `windows-latest` 跑三门禁（format / build / test；零外部依赖故无锁文件门禁）
- 提交前本地至少跑 `build + test + format`；涉及切换逻辑的改动必须加跑 e2e；副屏任务栏等视觉行为需真机双屏人工确认（e2e 只能断言注册表）

## 已知坑（改动前先读）

- **任务栏对第三方主题切换的响应是 Explorer 上游 bug**（HANDOFF 问题 1，2026-10-06 三轮诊断收尾）：影响超出副屏——GUI 常驻会话内主屏任务栏也会滞后一拍；罕见深冻态下连设置页官方路径都无效（仅重启 explorer 恢复）。应用侧已累计证伪十一种 workaround（双写/二次广播/定向补发/后台线程/BeginInvoke/子进程隔离/Shell 启动/窗口激活/启动预热/PostMessage/修复主题对 partial 态无效），**决策为接受现状等待上游修复，不要再为它追加新 workaround**；实验数据与两种坏状态形态全部在 `docs/HANDOFF.md`
- `HWND_BROADCAST` 会同步回到自己的 `WndProc`：自刷新已 BeginInvoke 化（避免广播内嵌套 UI 工作），无害，不要"优化"它而引入递归/重入死锁
- 托盘图标句柄来自 `GetHicon`：必须与 `DestroyIcon` 配对释放（`TrayController` 已处理，改动时保持配对）
- 单实例 = 命名 Mutex + 激活事件：二次启动路径改动需保证 `AllowSetForegroundWindow` 在 `Set()` 之前调用、激活事件先于 `MainForm` 构造创建（两个历史 P2，勿回退）
