# PLAN — 实施计划（windows-theme-switch）

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v1.0 |
| 日期 | 2026-10-05 |
| 关联文档 | [SPEC.md](SPEC.md)（需求基线，冲突时以 SPEC 为准） |
| 总体策略 | 分阶段交付，每阶段独立可验证；阶段完成即 git commit，可随时暂停/回滚 |

## 0. 目标项目结构（最终形态）

```
windows-theme-switch/
├── .gitignore
├── .editorconfig              # 代码风格基线（阶段 1）
├── README.md                  # 用法与发布说明（阶段 6）
├── LICENSE                    # MIT（阶段 6）
├── docs/
│   ├── SPEC.md
│   └── PLAN.md
├── src/ThemeSwitcher/
│   ├── ThemeSwitcher.csproj
│   ├── Program.cs             # 入口 + 单实例 Mutex（FR-6）
│   ├── ThemeHelper.cs         # 主题读写/广播/自启注册（核心逻辑）
│   ├── MainForm.cs            # 主窗口 UI + 事件绑定
│   └── TrayController.cs      # NotifyIcon 托盘行为与动态图标
├── tests/ThemeSwitcher.Tests/
│   └── ThemeHelperTests.cs    # xunit 单元测试
├── scripts/
│   ├── publish.cmd            # 一键发布
│   └── make-icon.py           # .ico 生成（一次性，产物入库后脚本保留备追溯）
└── .github/workflows/ci.yml   # CI 四门禁（阶段 6）
```

## 阶段 0：仓库与文档（本次已完成）

**任务**
- [x] `git init -b main`
- [x] `.gitignore`（.NET 产物 + 敏感信息兜底 + `.zcode/`）
- [x] `docs/SPEC.md`、`docs/PLAN.md`
- [x] 初始提交

**验收**：`git log` 有初始提交；`git status` 干净。

## 阶段 1：项目骨架

**任务**
1. `src/ThemeSwitcher/ThemeSwitcher.csproj`：`net8.0-windows`，`UseWindowsForms`，`Nullable enable`，`TreatWarningsAsErrors true`，`ApplicationHighDpiMode=PerMonitorV2`；`ApplicationIcon` 预留
2. `Program.cs`：命名 Mutex（`Local\ThemeSwitcher.SingleInstance`）单实例；二次启动向已有实例发送激活消息（注册窗口消息 + `PostMessage`，或轮询窗口标题 `SetForegroundWindow`）后退出；解析 `--minimized`（FR-7）
3. `.editorconfig`：4 空格缩进、UTF-8、C# 命名风格基线
4. 解决方案文件 `ThemeSwitcher.sln`（src + tests）

**验收**：`dotnet build` 零警告；连续启动两个实例，第二个自动退出且第一个窗口被前置；`--minimized` 不显示窗口。

## 阶段 2：核心 ThemeHelper

**任务**
1. 读取：`SystemUsesLightTheme` / `AppsUseLightTheme` 当前值（缺键/缺值容错，默认视为浅色）
2. 写入：`SetTheme(bool? systemLight, bool? appsLight)`，单次写后广播 `WM_SETTINGCHANGE("ImmersiveColorSet")`（FR-8）
3. `ToggleAll()`：两者同时取反（FR-1 逻辑核心）
4. 自启注册：`IsAutoStartEnabled()` / `SetAutoStart(bool)`，写 `HKCU\...\Run`，值为 `"<exe>" --minimized`（FR-5）
5. DWM 标题栏帮助方法：`ApplyTitleBarTheme(Form, bool dark)`（FR-9）

**验收**：`reg query` 断言写入值正确；切换后任务栏即时变色；单测覆盖读写往返与容错分支。

## 阶段 3：主窗口 UI

**任务**
1. 固定小窗约 340×240：`FormBorderStyle.FixedDialog`，禁最大化/最小化按钮，居中启动
2. 状态行（FR-3）+ 一键全切大按钮（FR-1，扁平样式，文案随状态切换）
3. 两组互斥开关：Windows 模式 / 应用模式 各 [浅色|深色] 两钮，选中高亮（FR-2）
4. 首次切换成功后底部提示"关闭窗口将最小化到托盘"
5. 窗口关闭 → `Hide()`（FR-4①）；切换后同步刷新按钮态/状态行/标题栏明暗

**验收**：4 种组合可设置且 `reg query` 断言；UI 状态与注册表始终一致；中文显示无乱码。

## 阶段 4：托盘常驻

**任务**
1. `TrayController`：`NotifyIcon` 封装，GDI+ 动态绘制图标（半明半暗圆点，主题反转时重绘，FR-4④）
2. 左键单击 = `ToggleAll()`（FR-4②）；右键菜单：显示主窗口 / 切换主题 / 开机自启（带 ✓ 状态）/ 退出（FR-4③）
3. 退出时正确 `Dispose` NotifyIcon（防残留幽灵图标）
4. 系统主题被外部修改（如用户手动去设置页改）时托盘与窗口状态刷新（监听 `WM_SETTINGCHANGE`，仅自身空闲时）

**验收**：SPEC FR-4 全部行为点通过；任务管理器确认单进程；注销/重启托盘不残留。

## 阶段 5：测试

**任务**
1. `tests/ThemeSwitcher.Tests`（xunit）：ThemeHelper 读写往返、两键独立、缺键容错、`ToggleAll` 逻辑、自启开关
2. 端到端脚本化验证：运行 exe → 模拟点击/命令行触发 → `reg query` 断言 → **恢复测试前原始主题**（先记录初始值）
3. `dotnet test` / `dotnet format --verify-no-changes` 通过

**验收**：SPEC 第 7 节验收标准 1–6 全部通过；测试后系统主题与测试前一致。

## 阶段 6：发布与仓库基线

**任务**
1. `scripts/make-icon.py`（Pillow，本机已有）生成多尺寸 `.ico` → csproj `ApplicationIcon`
2. `scripts/publish.cmd`：`dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true`
3. `README.md`：截图、用法、两种发布方式（框架依赖/self-contained）、构建指南
4. `LICENSE`（MIT）
5. `.github/workflows/ci.yml` 四门禁：`dotnet format --verify-no-changes` / `dotnet build -warnaserror` / `dotnet test` /（零外部依赖，无锁文件门禁，CI 内注明缘由）
6. 远程仓库（若推 GitHub）：branch protection（required = test job）、public 时追加 gitleaks 全历史扫描 job 与 SECURITY.md——届时另行确认

**验收**：发布产物单文件运行正常；CI 本地等效命令全绿。

## 阶段 7：交付审查

**任务**：委派 code-reviewer 子代理（只读、证伪导向，假设至少存在 2 处错误）审查全部源码与脚本；P0/P1 问题修复后复验。

**验收**：无未解决的 P0/P1；SPEC 全部验收标准勾选。

## 里程碑摘要

| 里程碑 | 内容 | 对应 |
| --- | --- | --- |
| M1 | 骨架可运行，单实例生效 | 阶段 1 |
| M2 | 核心切换可用（窗口按钮） | 阶段 2–3 |
| M3 | 托盘体验完整（功能全量） | 阶段 4 |
| M4 | 测试齐备，可交付审查 | 阶段 5 |
| M5 | 发布就绪（v1.0） | 阶段 6–7 |

## 风险与对策

| 风险 | 影响 | 对策 |
| --- | --- | --- |
| 端到端测试真实改动系统主题 | 用户桌面状态被打扰 | 测试前记录初始值，测试后原样恢复（PLAN 硬性要求） |
| `HWND_BROADCAST` 在系统忙时超时 | 切换不即时 | `SMTO_ABORTIFHUNG` + 1s 超时；失败时重试一次并提示 |
| 杀毒软件误报单文件 exe | 发布版不可用 | 框架依赖发布体积小、特征少；README 提示白名单方案 |
| Win11 更新改变主题机制 | 功能失效 | 机制为公开文档化行为，风险低；SPEC 固化键值便于回归排查 |
| 托盘图标泄漏（进程退出未清理） | 幽灵图标 | 统一在 `ApplicationExit` 路径 Dispose |

## 回滚策略

每阶段独立 commit；任何阶段失败可 `git revert` 回上一里程碑而不影响已交付功能。
