# patch 005: winkey-gesture（Win 键手势判定）

- 开单: 2026-10-20（由原 `patch(002)` 拆分）
- 状态: 已合入
- 能不能不改源码做到？ 否 —— 全局键盘钩子与手势状态机只能在进程内实现
- 为什么上游不会接受 / 该不该推上游: 上游没有「改 Win 键行为」的路线；属于 fork 私有交互
- 触及的上游文件: 无（纯新增）
- 新增文件: `Core/Input/WinKeyGestureRouter.cs`、`Platform/Input/WinKeyHook.cs`、
  `tests/Core.Tests/WinKeyGestureRouterTests.cs`
- 上游同类实现/已有 PR: 无
- 回滚方式: 可单独 revert（无调用方时是死代码，编译不受影响）
- 验证:
  - [x] `WinKeyGestureRouterTests` 8 条（先 RED 后 GREEN）
  - [x] `scripts/fork-verify.sh` 构建通过（此时还没有调用方，见 patch(006)）

## 目的

要支持「单独轻按 Win 键打开启动台，Win+其它键仍是系统组合键」，需要一个可单测的手势状态机。

## 改动要点

- `WinKeyGestureRouter`（Core，纯逻辑）：`OnKeyDown` / `OnKeyUp` 决定返回
  `WinKeyDirective`（放行 / 吞掉 / 触发启动台），组合键前缀一旦判定为组合就永久放行
- `WinKeyHook`（Platform）：低级键盘钩子 `WH_KEYBOARD_LL`，单例 `_owner` 防重入，
  `Dispose` 时卸载
- 判定与接线分离：本 patch 只有状态机与钩子，**真正接上在 `patch(006)`**（`DesktopTaskbarIntegration`）

## 风险 / 以后要盯的地方

- 低级键盘钩子对输入延迟敏感；`DecideWinKey` 内不能做重活
- 与 patch(006) 强耦合：单独 revert 本 patch 会留下 `DesktopTaskbarIntegration` 的编译错误
  → 要撤就一起撤（台账已标明依赖方向）
- Win 键行为受系统「粘滞键 / 筛选键」设置影响，手验项
