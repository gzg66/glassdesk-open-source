# patch 003: launcher-desktop-add（从桌面批量添加）

- 开单: 2026-10-20（由原 `patch(002)` 拆分）
- 状态: 已合入
- 能不能不改源码做到？ 否 —— 扫描与导入都在进程内
- 为什么上游不会接受 / 该不该推上游: 交互增强，上游未含；可作 PR 候选
- 触及的上游文件: `Platform/Interop/Shell32.cs`（新增 3 个 guid/常量）、`Platform/Shell/KnownFolders.cs`、
  `Launcher/LauncherWindow.Dialogs.cs`（菜单一项 + `ImportPaths` 改走 `ImportRequests`）
- 新增文件: `Platform/Shell/DesktopEntryScanner.cs`、`Platform/Shell/ShellDisplayName.cs`、
  `tests/AppServices.Tests/DesktopEntryScannerTests.cs`
- 上游同类实现/已有 PR: 无
- 回滚方式: 可单独 revert（纯增量 + 菜单一行）
- 验证:
  - [x] `DesktopEntryScannerTests` 6 条（先 RED 后 GREEN）
  - [x] `scripts/fork-verify.sh` 构建通过

## 目的

启动台菜单只有「网站…」「文件夹…」，要从桌面加程序只能一个个点。补一个整批导入。

## 改动要点

- `DesktopEntryScanner`：扫公共桌面（`%PUBLIC%\Desktop`）与当前用户桌面（一次性拿两个已知文件夹，
  然后本地过滤，省一次 COM 往返）
- `ShellDisplayName`：用 `SHGetNameFromIDList` + `SIGDN_NORMALDISPLAY` 取资源管理器里显示的名字
  （文件名 `notepad.exe` → 显示「记事本」）
- 顶层扫描去重：不同桌面目录里同一个 `TargetPath` 只保留一条
- `LauncherWindow.Dialogs.AddFromDesktop()` 把结果整批塞进 `ImportRequests`

## 风险 / 以后要盯的地方

- 依赖外壳 API（`Shell32.FolderIdDesktop` / `FolderIdCommonDesktop` / `SigdnNormalDisplay`）
- OneDrive 重定向桌面时行为需真机验（手验项）
- 与 patch(004)/(006) 共用 `SettingsWindow.xaml(.cs)` 之外的启动台文件，注意 hunk 上下文
