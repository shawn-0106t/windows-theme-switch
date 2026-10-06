# HANDOFF — v1.0 交付后问题交接（#1 副屏任务栏 / #2 窗口贴靠托盘）

| 项目 | 内容 |
| --- | --- |
| 文档版本 | v1.1（归档） |
| 日期 | 2026-10-06 |
| 状态 | **两个问题均已修复**（分支 `fix/handoff-secondary-taskbar-and-tray-docking`：问题 1 = commit `51d64ac`，问题 2 = commit `182e0bf`；本 commit 归档文档）。**遗留真机人工确认项**见各问题的"验收状态" |
| 基线代码 | commit `d9214b6`（main，阶段 0–7 全部完成，CI 三门禁全绿，e2e 4/4 PASS） |
| 环境 | Windows 11（10.0.26300，25H2 内核）、双显示器、.NET 8 Desktop Runtime 已装 |

## 背景

v1.0 已交付：主窗口一键全切 + Windows/应用模式独立开关、托盘常驻（左键全切）、
开机自启、单实例、CLI（`--toggle/--set/--status`）。用户实测**大部分正常**，
以下两个问题为实测反馈，本文档固化复现信息、根因分析与候选方案，供下一轮实施。

---

## 问题 1：切换主题后副屏任务栏不跟随（仅任务栏，其余 UI 正常）——已修复（`51d64ac`）

**落地实现**（原候选方案 A+B 组合）：
- `SetTheme` 在首次"写+播"后，向 `Shell_TrayWnd` / `Shell_SecondaryTrayWnd` 全部实例
  （`EnumWindows` 枚举，多副屏可有多个）定向补发 `WM_SETTINGCHANGE`（超时 200ms）
- 延迟 120ms 重写相同值并二次广播（可注入，单测传 0）；延迟窗口内外部已写入新值时
  跳过重写让位新切换；全程同步完成（CLI 退出语义）
- 单测 +3、e2e 4/4 PASS、code-reviewer 复核 Approve（曾发现 150% DPI 相关问题于问题 2 侧修复）

**验收状态**：注册表与广播路径已自动化验证；**副屏任务栏视觉同步仍需双屏真机人工确认**
（三条切换路径 × 双向，见下方"验证方法"）。若 A+B 组合仍偶发残留，按方案 C 加托盘
"修复主题"手动项即可收敛。

**第二轮深挖（2026-10-06 下午，用户反馈"任务栏仍然有 bug"后）**——上游 bug 比原记录更深：

- **实测规律**（视觉采样自动化验证，`scripts/verify-secondary-taskbar.ps1` / `verify-gui-path.ps1`）：
  - CLI 形态切换（无 GUI 实例运行）：16/16 稳定生效，主/副屏全跟随
  - **GUI 实例的第一次切换：8/8 必触发任务栏 partial/冻结**（主/副屏部分区域滞留，
    后续任何广播——含外部进程——都无法唤醒；与 ADM #1172"typically partial"+"重启
    explorer 才恢复"完全吻合）；冻结态连 CLI 也失效，`rundll32 user32.dll,UpdatePerUserSystemParameters`
    +广播或较长 idle 可解冻
  - GUI 会话内第二次起的切换恢复有效
- **已证伪假说**（均实验排除）：副屏任务栏窗口类名错误（实测确认就是 Shell_SecondaryTrayWnd）、
  托盘图标更新竞争、WndProc 嵌套刷新（改 BeginInvoke 无效）、UI 线程阻塞（改后台线程无效）、
  切换挪至 CLI 子进程（GUI 启动的子进程同样触发）、UseShellExecute 启动、窗口先激活再点击、
  启动时等值预热（Explorer 不对无变化值走渲染路径）、PostMessage 异步广播（lParam 跨进程
  无效，任务栏直接忽略）
- **本轮落地**：GUI 切换改为后台线程执行（不卡 UI，且为"第二次起有效"的形态）；WndProc
  自刷新改 BeginInvoke（消除广播内嵌套 UI 工作）；CLI 新增 `--set-sys` / `--set-apps`
  单模式参数；两个视觉验证脚本入库（自动化复现/回归本问题，不再依赖人眼）
- **后续候选**：等 Windows 上游修复；把"GUI 进程首次切换触发冻结"数据反馈到
  AutoDarkMode #1172（ADM 常驻 GUI 形态与本规律一致）；方案 C 的重启 explorer 兜底
  （破坏性，默认不做）

**方案 C 落地（2026-10-06，同日第三轮）**：托盘菜单"修复主题" + CLI `--repair`
（FR-12）已实现——等值重写（完整广播+定向+reapply）+ `UpdatePerUserSystemParameters` +
再广播。真机验证（`scripts/verify-repair.ps1`）确认上游 bug 存在**两种坏状态**：
- 完全冻结态（任何广播无效，任务栏整体不动）：修复主题实测**可唤醒**（此前手工实验解冻即此态）
- GUI 首切 partial 滞后一拍态：修复主题（CLI 进程执行）**效果有限**，任务栏仍滞后；
  且 GUI 会话内后续每次切换持续滞后一拍（Step3 数据）。该态下实测唯一恢复手段仍是
  重启 explorer 或较长时间 idle 后的系统自愈
- 结论：修复主题作为低成本兜底保留（对冻结态有效、正常态无害），partial 态根治
  依赖上游修复

**第三轮收尾（2026-10-06，computer-use 真机验证）——应用侧停止追修**：

- computer-use（UIA 驱动真实点击）完整验证 GUI 路径：主窗口 AX 树完整、一键全切
  UI/注册表即时一致、**副屏任务栏自第二次切换起完全正常**（问题 1 的原始目标基本
  解决）、关闭=最小化到托盘正常；但**主屏任务栏在 GUI 会话内持续滞后一拍**
- 验证过程中系统进入**深层冻结态**，至此全部唤醒手段实验完毕且无效：CLI 往返切换、
  `--repair` ×2、90/120s 空闲自愈、设置页官方"浅色→深色"往返（官方路径在更浅的坏
  态下成功过一次，深冻态下同样无效）——即 ADM #1172 所述"仅重启 explorer 可恢复"
  的最顽固形态
- **决策**（与用户确认）：应用侧不再追加 workaround（累计十一种手段证伪，继续加复杂
  度只会引入新风险）；不加"重启资源管理器"菜单项；根治等待 Windows 上游（26300 为
  25H2 内核，正式版可能已修）
- **深冻态恢复指引**（用户手动）：设置页手动切换一次；或重启 explorer（任务管理器
  结束 explorer.exe 后新建任务运行 explorer.exe，会关闭已打开的资源管理器窗口）
- 遗留已知行为（接受现状）：GUI 常驻会话内主屏任务栏可能滞后一拍显示上一拍颜色，
  副屏正常；CLI 路径（无 GUI 实例时）100% 稳定

<details>
<summary>原始交接内容（复现 / 根因 / 候选方案，供回溯）</summary>

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

</details>

---

## 问题 2：主窗口启动时紧靠托盘图标——已修复（`182e0bf`）

**落地实现**（原方案 1 + 决策点默认值）：
- 新增 `TrayDocking`：`FindWindow("Shell_TrayWnd")` 定位任务栏 → `Screen.FromHandle`
  → 工作区靠任务栏一侧的角对齐（底部 = 托盘正上方 8px；顶/左/右同侧兜底）；纯函数可单测
- `StartPosition` 改 `Manual`；启动构造与**每次** `ShowAndActivate` 都重新贴靠（不记忆拖动）
- `OnLoad` 再贴靠一次兜底：构造期 `Size` 可能未完成 DPI/字体缩放（code-review 首轮发现的
  Major：150% DPI 下右下锚点会溢出屏幕），Load 在缩放后、首绘前触发，无闪烁
- 单测 +6（四边停靠/无任务栏/非零原点副屏坐标）

**验收状态**：位置计算已单测覆盖；**窗口实际视觉位置需真机人工确认**
（启动 / `--minimized` 后托盘"显示主窗口" / 第二实例激活三种情形 × 底部任务栏 × 150% DPI）。

<details>
<summary>原始交接内容（需求 / 候选方案 / 决策点，供回溯）</summary>

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

</details>

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
- ~~修复完成后本 HANDOFF 归档：问题节标记 resolved + commit 号，或移入 `docs/archive/`~~
  → 已于 2026-10-06 归档（本文档 v1.1）；两条"验收状态"中的真机人工确认项完成前不建议删除本文档
