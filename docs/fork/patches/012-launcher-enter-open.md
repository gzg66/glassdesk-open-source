# patch 012: launcher-enter-open（启动台搜索后回车打开第一个匹配）

- 开单: 2026-10-20
- 状态: 待手验
- 能不能不改源码做到？ 否 —— 启动台自己吞掉了回车，没有任何配置点能让它转成「打开」
- 为什么上游不会接受 / 该不该推上游: 属于可推上游的交互补全，但当前以 fork 薄层实现（上游未提 issue）；若上游实现同类行为，删本 patch 台账即可
- 触及的上游文件:
  - `src/GlassDesk.Launcher/LauncherWindow.xaml.cs`（构造函数加 1 行：窗口级 `PreviewKeyDown` 挂载）
- 新增文件:
  - `src/GlassDesk.Core/Input/LauncherEnterRouter.cs`
  - `src/GlassDesk.Launcher/LauncherWindow.Keyboard.cs`
  - `tests/GlassDesk.Core.Tests/LauncherEnterRouterTests.cs`
- 上游同类实现/已有 PR: 无
- 回滚方式: 可单独 revert（撤掉 1 行挂载 + 两个新文件，启动台回到「只能鼠标点击」）
- 验证:
  - [x] `LauncherEnterRouterTests` 7 条（Core 全绿 192/192，基线 185 + 新增 7）
  - [x] `dotnet build src/GlassDesk.App -c Release` → 0 warning / 0 error
  - [ ] 端到端（真实键盘）：Win → 输入 → 回车打开（待用户手验）
- 手验步骤（用户侧）:
  1. 单击 Win 唤出启动台，焦点应在搜索框
  2. 输入一个能命中的应用名（中文 / 拼音 / 别名均可）
  3. 按回车 → 应打开**当前页第一个**匹配项，并播放关闭动画
  4. 输入无匹配的字符串 → 回车不应有任何动作（仍显示「没有匹配的应用。」）
  5. `Esc` 清空搜索后（进入浏览态）按回车 → 不应打开任何应用
  6. Tab 到「添加」按钮后按回车 → 仍应弹出「添加」面板（回车归按钮，不被启动台抢走）
  7. 鼠标右键菜单 / 重命名对话框打开时按回车 → 回车归弹层，不触发打开
  8. 中文输入法组字过程中按回车 → 只上屏候选词，不打开应用

## 目的（一句话，写给人看，不是写给编译器看）

Win 唤出启动台、打字搜索之后，用户本能地按回车；此前回车什么也不做，只能用鼠标点第一个结果。

## 根因

启动台把搜索框焦点交给 `SearchBox`（`LauncherWindow.Focus.cs`），但**没有任何键盘处理**：

- `SearchBox` 的 XAML 只有 `TextChanged`，回车在 `TextBox` 里是空操作；
- 应用图标 `LauncherAppTileButton` 显式 `Focusable=False` + `IsTabStop=False`（见 `LauncherWindow.xaml` 的 `LauncherAppTile` 样式），键盘根本无法选中它们；
- 窗口级也没有 `PreviewKeyDown`，所以回车既没被搜索框处理，也没有落到任何「激活」逻辑上。

即：`ActivateEntry(id)` 只有鼠标点击的 `OnTileActivated` 一条调用路径。

## 实现

判定逻辑放在 Core，可单测（`GlassDesk.Core.Input.LauncherEnterRouter`）：

```
OnEnter(view, context) → (Activate, SlotIndex)
  ImeComposing || InteractionBusy  → 不接管
  !view.IsSearching || view.OpenGroupId != null → 不接管
  view.VisibleCount <= 0            → 不接管
  否则                             → 接管，SlotIndex = view.Page * view.Capacity
```

- 目标是**当前页第一个可见结果**：搜索也分页（`LauncherViewState` 有 `_searchPage`），
  用户翻到第 2 页时回车打开的是眼前第一项，而不是第 1 页里那条看不见的。
- `InteractionBusy` 由窗口折算：`_keepOpenWhileModalCount > 0`（模态对话框）、`_openContextMenuCount > 0`（右键菜单）、`_dragEdit != null` / `_dragOrigin != null`（拖拽整理中）。
- 焦点守卫：只在 `Keyboard.FocusedElement` 是 `SearchBox`、是窗口本身、或为空时接管；焦点落在按钮上（Tab 到「添加」/翻页箭头）时把回车让给按钮。
- 用窗口级 `PreviewKeyDown`（`handledEventsToo: true`）而不是给 `SearchBox` 挂事件：点击非焦点控件、托盘面板、切换动画都可能把窗口内部焦点挪走，但用户意图仍是「我在搜索」。

## 与既有验收探针的关系

`tests/GlassDesk.LauncherAcceptance/MouseOnlyTileCases.cs` 里有用例断言「方向键和确认键不会打开应用或分组」：

- 该探针用 `Keyboard.KeyDownEvent`（冒泡）直接投递给窗口，本 patch 只挂 `PreviewKeyDown`（隧道），不会触发；
- 即使触发，探针未输入搜索词，`IsSearching == false` → router 返回不接管。
