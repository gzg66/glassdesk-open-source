# patch 011: launcher-jumplist-tasks

- 开单: 2026-10-21
- 状态: 待评审
- 能不能不改源码做到？(钩子/配置/插件/JWT/hook 点) 结论: **不能**。启动台右键菜单在 `LauncherWindow.Interaction.cs:BuildTileMenu` 里生成，没有插件点；上游不会读跳转列表。
- 为什么上游不会接受 / 该不该推上游: 属我们自己的产品方向（按 Win 唤出的启动台右键应显示程序自己的任务）。上游只发布压缩快照、无插件体系，推上游无从谈起。
- 触及的上游文件: `src/GlassDesk.Launcher/LauncherWindow.Interaction.cs`（`BuildTileMenu` 末尾追加 1 行）、`src/GlassDesk.Platform/Interop/ShellCom.cs`（类末追加 `IPersistStream` 与 `ShellComIds.PersistStream`）
- 新增文件（放我们自己的扩展目录）: 无独立扩展目录，按上游分层新增：`Platform/Shell/JumpListFormat.cs`、`Platform/Shell/JumpListTaskReader.cs`、`Platform/Interop/Shlwapi.cs`、`AppServices/JumpListIndex.cs`、`AppServices/JumpListTaskCatalog.cs`、`Launcher/LauncherWindow.JumpListTasks.cs`
- 上游同类实现/已有 PR: 无
- 回滚方式: 单独 revert 本 patch 即可（两个上游文件的改动都是纯追加，revert 不牵动其它逻辑）
- 验证:
  - [x] 单元/集成测试（35 条，见下）
  - [x] 手工验证步骤: 启动台右键 Edge（新建窗口 / 新建无痕窗口并真的开 InPrivate）、Chrome（两条）、记事本（菜单与改动前一致、无空分隔线）、VS Code（有「新窗口」且不串 explorer 条目）、第二次右键不重复、菜单打开时窗口不自动隐藏 —— 2026-10-21 人工确认
  - [ ] `bash .fork/bin/fork-sync.sh --apply` 后仍通过

## 目的（一句话，写给人看，不是写给编译器看）

按 Win 唤出的启动台里，右键某个程序时应能看到该程序自己注册的任务（Edge 的「新建窗口 / 新建无痕窗口」这类），而不是只有通用的「打开 / 编辑」。

## 改动要点

数据来源是 `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\*.customDestinations-ms`（Windows 跳转列表存储），不是安装目录；解析方式与实测结论见 `docs/superpowers/specs/2026-10-21-launcher-jump-list-tasks-design.md` §3。link 里的目标/参数/工作目录交给系统 `IShellLink`（`IPersistStream` 从内存流加载），标题按 store 锚点读，读不到就跳过该条目。

## 风险 / 以后要盯的地方

- 格式属未公开文档：最坏结果是任务消失，不会影响右键菜单其它项。
- 覆盖率有限：多数程序（记事本、计算器、多数 UWP）本来就没有跳转列表，这些程序右键只有通用项，属 Windows 行为不是缺陷。

## 验证结果（2026-10-21）

- 单测：`JumpList` 前缀 36 条全绿（格式 9 / 读取 6 / 归一化匹配 14 / 缓存执行 7）。
- 全量：Core 185/185、Dock 481/481（首跑有 25 条 capture 用例偶发 `TimeoutException`，未改动的 main 上同样复现，隔一会儿重跑全绿）、AppServices 368/414（失败 46 条仍为基线）。
- 实机探针：`GlassDesk.CompatibilityProbe --jumplist-tasks` → 62 条任务；Steam 8、Edge 2、Chrome 2（按标题去重后）、Windows Terminal 4、PowerShell ISE 3、VS Code 1。
- 已知不覆盖：Office（只注册了「最近」分类）与多数 UWP 没有任务分类，右键只有通用菜单项。
