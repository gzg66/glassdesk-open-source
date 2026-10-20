# patch 006: native-taskbar-tray（原生任务栏接管 + 托盘入口）

- 开单: 2026-10-20（由原 `patch(002)` 拆分）
- 状态: 已合入
- 能不能不改源码做到？ 否 —— 要在进程内 hook Win 键并调用 UIA 点任务栏按钮
- 为什么上游不会接受 / 该不该推上游: 改的是 Windows 任务栏行为，属于 fork 私有强力定制，不推
- 触及的上游文件: `App/AppHost.cs`（装配 + 托盘入口）、`Launcher/LauncherWindow.xaml`（托盘按钮）、
  `Settings/SettingsWindow.xaml(.cs)`（系统任务栏页）
- 新增文件: `App/DesktopTaskbarIntegration.cs`、`App/TrayFlyout.cs`、`Platform/Shell/NativeTaskbarSession.cs`、
  `Launcher/LauncherWindow.Tray.cs`、`Settings/SettingsWindow.Taskbar.cs`
- 上游同类实现/已有 PR: 无
- 依赖: **必须排在 patch(005) 之后** —— `DesktopTaskbarIntegration` 直接 `new WinKeyHook(...)`
  并调用 `WinKeyGestureRouter.OnKeyDown/OnKeyUp`
- 回滚方式: 与 patch(005) 一起撤（单独 revert 会留下 WinKey 相关编译错误）
- 验证:
  - [x] `scripts/fork-verify.sh` 构建通过
  - [ ] 手验：隐藏任务栏后 Win 键打开启动台、退出恢复任务栏、托盘按钮弹出图标列表

## 目的

把 Windows 原生任务栏藏掉，Dock 完全接管；但托盘图标不能丢，所以启动台顶部加一个
「托盘图标」按钮，直接点任务栏自己的「显示隐藏的图标」。

## 改动要点

- `NativeTaskbarSession`：隐藏/恢复任务栏（`SHLoadInProc` 路线外的最小实现），
  退出/崩溃兜底恢复，看门狗应付资源管理器重建
- `DesktopTaskbarIntegration`（App 层）：把设置开关接到会话上，`WinKeyHook` 判定
  「单独轻按 Win」→ `ExecuteInteraction(InputAction.ToggleLauncher)`；
  `StatusChanged` 回写设置页状态
- `TrayFlyout`：UIA 找到任务栏的「显示隐藏的图标」按钮并调用；失败返回错误字符串
- `AppHost`：`_desktopTaskbar` 生命周期（创建/Dispose/设置变更应用）、`ShowTrayFlyout()`
  （先收启动台 → 350ms 后点托盘 → 失败退回 `RevealNativeTaskbar()` 60 秒）、
  `LogTaskbarEvent` 写 `Data/logs/desktop-taskbar.log`
- 启动台 `TrayFlyoutButton` 仅在宿主注入了 `ShowTrayFlyout` 时显示

## 风险 / 以后要盯的地方

- **高冲突面**：`AppHost.cs` 是上游高频改动区，本 patch 是它唯一的改动方（其余 patch 不碰装配层）
- 依赖 UIA 寻找任务栏按钮：Windows 版本更新/任务栏改版可能失效 → 已实现「退回临时显示任务栏」兜底
- 后台线程/看门狗与退出顺序：`Dispose` 必须恢复任务栏，否则用户桌面会永久丢任务栏（严重）
- 手验项：隐藏/恢复、Win 键、托盘按钮、资源管理器重启（任务管理器重启 explorer）
