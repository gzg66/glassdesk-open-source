# patch 007: fullscreen-focus-publish（全屏误判 / 焦点与搜索 / 发布保留数据）

- 开单: 2026-10-20（编号补登，提交原为 `fix: 全屏误判 / 启动台焦点与搜索 / 发布保留数据`）
- 状态: 已合入
- 能不能不改源码做到？ 否 —— 三处都在进程内的判定逻辑与发布脚本
- 为什么上游不会接受 / 该不该推上游: 「最大化窗口被当全屏」「Win 唤出后键盘不进本进程」是通用 bug，**值得回流上游**；
  「发布保留数据」是 fork 自己的发布流程，不推
- 触及的上游文件（10 个）: `src/GlassDesk.App/AppHost.cs`、`src/GlassDesk.Platform/Shell/FullscreenDetector.cs`、
  `AGENTS.md`、`scripts/publish-portable.ps1`、`docs/fork/BASELINE.*`
- 新增文件: `src/GlassDesk.Platform/Shell/FullscreenWindowRules.cs`、
  `src/GlassDesk.Launcher/LauncherWindow.Focus.cs`、`LauncherWindow.SearchReset.cs`、
  `tests/GlassDesk.Dock.Tests/FullscreenWindowRulesTests.cs`
- 上游同类实现/已有 PR: 未见（未查上游私有历史，公开快照无对应修复）
- 回滚方式: 可单独 revert（三块互不依赖，注意均含 `AppHost.cs` 一行装配）
- 验证:
  - [x] Core 181/181、Dock 481/481、AppServices 378（46 项基线失败）
  - [x] 实测最大化记事本置前：`verdict=NotFullscreen`、`observation.IsFullscreen=False`、`IsFullscreenActive()=False`
  - [x] `scripts/fork-verify.sh` 构建通过

## 目的

三个独立回归：Dock 唤不出（全屏误判）、启动台唤出后不能直接打字、发布丢用户数据。

## 改动要点

1. 全屏判定加「铺满 + 最大化 + 带 `WS_CAPTION` 的普通窗口 = 不是全屏」规则，`EvaluateWindowPath` 与
   `ReadPerformanceObservation` 两条路径都接入（后者决定 Dock 是否进「游戏休眠」）
2. `TakeKeyboardFocus()` 用 `AttachThreadInput` 挂前台线程后设置前台窗口/焦点；`ResetSearchForOpen()` 每次唤出清空搜索
3. `publish-portable.ps1` 清理输出目录前把 `Data/` 备份到 `artifacts/data-snapshot`，打包后恢复；`-ResetData` 才显式清空

## 风险 / 以后要盯的地方

- 全屏规则与上游 `FullscreenDetector` 后续改动可能冲突（上游改这块的频率低，风险小）
- `AttachThreadInput` 属 Win32 边缘用法，跨 Windows 版本需回归
- 与 patch 006 共用 `AppHost.cs`，rebase 时按 hunk 处理，不要整文件取舍
