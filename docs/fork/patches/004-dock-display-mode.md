# patch 004: dock-display-mode（修「显示方式」点了没反应）

- 开单: 2026-10-20（由原 `patch(002)` 拆分）
- 状态: 已合入
- 能不能不改源码做到？ 否 —— `Normalize()` 在保存路径就地改值，配置层无开关
- 为什么上游不会接受 / 该不该推上游: **是上游 bug**，应当回流（公开快照里 `Normalize` 无条件 `Dock.AutoHide = false`）
- 触及的上游文件: `Core/Models/Settings/{GlassDeskSettings,SettingsPageEditor,DisplaySettings}.cs`、
  `Core/Storage/SettingsStore.cs`、`Dock/DockWindow.xaml.cs`、`Settings/SettingsWindow.xaml(.cs)`
- 新增文件: `tests/Core.Tests/DockVisibilitySettingsTests.cs`
- 上游同类实现/已有 PR: 未见
- 回滚方式: 可单独 revert（与 patch(003)/(005)/(006) 文件不重叠，除 `SettingsWindow.xaml.cs` 的 hunk 上下文）
- 验证:
  - [x] `DockVisibilitySettingsTests`（`WithDockVisibility` 只改 `AutoHide`，其余字段保留）
  - [x] `scripts/fork-verify.sh` 构建通过

## 目的

设置页「显示方式」选「自动隐藏」后再点别的控件，选择会被顶回「常驻显示」。
根因：`GlassDeskSettings.Normalize()` 在**保存路径**也执行 `Dock.AutoHide = false`，
把用户刚做的选择就地改掉；UI 只是诚实地把改后的值读回来。

## 改动要点

- `Normalize(bool disableRemovedFeatures = false)`：只有读盘路径传 `true` 才迁移旧配置的 `AutoHide`
- `SettingsStore.Load` 传 `Normalize(disableRemovedFeatures: true)`；保存路径保持 `false`
- `SettingsPageEditor.WithDockVisibility`：显示方式只改 `AutoHide`，其余字段原样保留（单测覆盖）
- `OnDockControlChanged` 忽略 `Unchecked`（WPF 同组切换先给旧按钮发 Unchecked，按旧状态保存会把选择顶回去）
- `DockWindow` 刷新：`AutoHide=false` 走原 `ForceShown`，`true` 才跟指针做 enter/leave/poll

## 风险 / 以后要盯的地方

- **改了上游方法签名**（`Normalize`），这是本仓库唯一「非增量」的改动点；上游同期若改这个方法，rebase 必然撞车
- `AutoHide` 语义从「永远关掉」变成「用户可选」，玻璃/动画在自动隐藏态下的表现需真机验（手验项）
- `SettingsWindow.xaml.cs` 与 patch(006) 同文件：rebase 时按 hunk 归属判断（本 patch 的 hunk 全在 Dock 区）
