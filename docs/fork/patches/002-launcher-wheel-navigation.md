# patch 002: launcher-wheel-navigation（启动台滚轮翻页）

- 开单: 2026-10-20（由原 `patch(002)` 拆分）
- 状态: 已合入
- 能不能不改源码做到？ 否 —— 启动台没有暴露滚轮策略的扩展点
- 为什么上游不会接受 / 该不该推上游: 交互增强，上游路线未含；可作为 PR 候选但非 bug
- 触及的上游文件: `src/GlassDesk.Launcher/LauncherWindow.xaml`（1 行：挂 `PreviewMouseWheel`）
- 新增文件: `Core/Models/LauncherWheelNavigation.cs`、`Launcher/LauncherWindow.Wheel.cs`、
  `tests/Core.Tests/LauncherWheelNavigationTests.cs`
- 上游同类实现/已有 PR: 无
- 回滚方式: 可单独 revert（纯增量 + 1 行 XAML 挂点）
- 验证:
  - [x] `LauncherWheelNavigationTests` 8 条（先 RED 后 GREEN）
  - [x] `scripts/fork-verify.sh` 构建通过

## 目的

启动台每页图标放不下时，鼠标滚轮无法翻页，只能点左右箭头。

## 改动要点

- 纯逻辑抽到 `Core/Models/LauncherWheelNavigation`（可单测，不依赖 WPF）
- 交互约定：**页内先滚，到边才翻页**（`LauncherWindow.Wheel.cs`）
- XAML 只在窗口根节点挂一个 `PreviewMouseWheel="OnLauncherWheel"`

## 风险 / 以后要盯的地方

- 与 `LauncherWindow.xaml` 的其它改动同文件；上游改启动台布局时注意 hunk 上下文
- 到边翻页的阈值常量若与触摸板惯性冲突，需在真机上验（手验项）
