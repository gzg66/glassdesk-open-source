# 启动台右键菜单：按程序显示真实「任务」列表 — 设计

日期：2026-10-21
状态：待批准
关联代码：`src/GlassDesk.Launcher/LauncherWindow.Interaction.cs`、`src/GlassDesk.Platform/Shell/*`、`src/GlassDesk.AppServices/LaunchService.cs`、`src/GlassDesk.Platform/Shell/ShellExecutor.cs`

## 1. 意图

按 Win 唤出启动台后，右键某个程序，菜单顶部应出现**这个程序自己注册的任务**，与 Windows 开始菜单右键看到的「任务」一致：

- Edge → `新建窗口(N)`、`新建无痕窗口(I)`
- Chrome → `打开新的窗口(N)`、`打开新的无痕式窗口(I)`
- Windows Terminal → `Windows PowerShell`、`命令提示符`、`Ubuntu-22.04`
- Office → `新建 Microsoft Excel 工作表` 等

要点（用户原话）：**不从安装目录取、不硬编码每个程序、后续新增的程序也要自动识别**。因此能力必须来自 Windows 数据本身，靠「按程序逐个配规则表」不算完成。

## 2. 非目标

- **不**读取「最近打开的文件/网页」（`AutomaticDestinations`）。用户明确只要任务，这一版不做。
- **不**做「固定到跳转列表」「清除最近」「固定项」写操作。只读。
- **不**修改 Dock 的右键菜单。本版只动启动台；Dock 走自己的 `OnTileContextMenuRequested`，后续若要复用同一服务再另开任务。
- **不**改应用库存储格式、不加设置项开关（若后续要开关，按 `AGENTS.md` §5 成对加）。

## 3. 已实测事实（本章是实现前提，不可跳过）

数据来源不是安装目录，而是每个用户的跳转列表存储：

```
%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\<16位hex>.customDestinations-ms
```

本机实测（Windows 11，同一用户）：44 个文件，其中 **25 个能解出任务**，覆盖 Edge / Chrome / Chromium-for-Testing / Windows Terminal / VS Code / Cursor / Trae / PowerShell ISE / Office(Word/Excel/PowerPoint 部分) / Steam / Battle.net / iTunes。

解析出的真实内容示例（本机实测原文）：

| 程序 | 任务标题 | 命令行参数 |
|---|---|---|
| Edge | `新建窗口(N)` | `--edge-jumplist-new-window` |
| Edge | `新建无痕窗口(I)` | `--inprivate` |
| Chrome | `打开新的窗口(N)` | `--new-window`（部分档案夹另有 `--incognito`） |
| Windows Terminal | `命令提示符` | 指向 `wt.exe` 的 profile 参数 |
| PowerShell ISE | `以管理员身份运行 ISE` | — |

**大部分程序没有跳转列表文件**（记事本、计算器、多数 UWP 等）。这是 Windows 的行为，不是解析失败：这些程序右键就只显示现有的通用菜单项。

### 3.1 文件格式（逆向结论，已用 44 个真实文件验证）

文件**不是**标准 OLE 复合文档（`classic` 变体）。结构分三层：**文件头 → 分类头 → item**，
每个分类带上若干 item：

```
file   := <u32 版本=2> <u32 分类数> <8 字节保留>
category := <u16 名字字符数=0 或 n> [<名字 UTF-16 × n>] <u32 条目数>
item   := <16 字节 ShellLink CLSID> <shell link (MS-SHLLINK)> <property store>*
```

**分类决定一条 item 是不是「任务」**（实测结论，实现依据）：

- 有名字的分类（`常去网站`、`最近关闭`、`最近` 等）是**数据驱动**的「最近/常去」条目，**不是**任务。
- **没有名字**的分类才是程序注册的任务（Windows 在开始菜单里把它显示为「任务」）。
  实测：Chrome / Edge 的任务在文件末尾，VS Code 的任务在最前面，Windows Terminal 整个文件只有一个无名分类。
- 不按分类筛的后果（实测）：本机 129 条里有 67 条是「最近关闭」，右键 Chrome 会得到
  几十条 puppeteer / Playwright 临时档案夹的记录。筛后是 62 条，Steam 8 条、Edge/Chrome 各 2 条、
  Windows Terminal 4 条、PowerShell ISE 3 条，与开始菜单一致。
- 分类头的定位方式：item 起点 = link 偏移 − 16，而分类头总是正好结束在它的第一个 item 起点上；
  所以反向「头必须结束在 item 起点」来定位头，不需要跳过 item 那段长度未知的存储块尾巴。

其余细节：

- **shell link**：标准的 MS-SHLLINK 结构。文件里可以直接按签名定位：
  `4C 00 00 00` + `{00021401-0000-0000-C000-000000000046}`。
  参数与目标路径**不要自己解**——用系统自带的 `IShellLink` 读（见 §4.2）。
- **property store** 每块形如：
  ```
  uint32 totalSize      // 含本字段
  "1SPS"                // 4 字节签名
  byte[16] fmtid        // 存储格式 id
  body                  // totalSize - 24 字节
  ```
  - **标题**在 `fmtid == {F29F85E0-4FF9-1068-AB91-08002B27B3D9}`（FMTID_SummaryInformation）的那一块里。
  - 标题值的锚点（本机 44 文件验证有效）：`1F 00 00 00` 后紧跟 `uint32 charCount`，再跟 `charCount` 个 UTF-16LE 代码单元，末尾一个 `U+0000`。
  - 同一个 item 通常还有第二块 store（图标），不用。

### 3.2 已验证不可行的路线（写入文档避免重复踩）

- `PSGetPropertyFromPropertyStorage`（propsys）读这些 store：**返回 `0x8007000D` ERROR_INVALID_DATA**，对长度做 24..120 字节穷举也不成立。存储块不是 MS-PROPSTORE 序列化属性存储，是跳转列表自己的轻量封装。
- 「在整段二进制里找第一段像样的 UTF-16 字符串」这种启发式：在本机数据上会把二进制垃圾当标题（Edge / PowerShell ISE / VS Code 都出现过）。必须按 §3.1 的 store 边界 + fmtid + 锚点定位。

### 3.3 覆盖率限制（实测）

- 8 个文件（多为 UWP / 部分 Office）用当前锚点解不出标题。策略：**解不出标题的条目直接不显示**（宁可少一个菜单项，不显示垃圾字符串）。
- 一个程序可能有**多个** `customDestinations-ms`（不同配置文件/不同安装路径），任务需要合并去重。
- 参数里可能带档案夹路径（实测 Edge 有 `--user-data-dir="C:\Temp\edge-debug-profile"`）。这一版**原样透传**——它和开始菜单里点击的行为一致。

## 4. 设计

### 4.1 新增文件（冲突面：低，全部新文件）

| 文件 | 项目 | 职责 |
|---|---|---|
| `src/GlassDesk.Platform/Shell/JumpListTaskReader.cs` | Platform | 扫 `CustomDestinations` 目录 → 读出 `JumpListTask` 列表。只读文件、只读解析、不缓存 |
| `src/GlassDesk.Platform/Shell/JumpListFormat.cs` | Platform | 纯函数：定位 link 签名、算 link 长度区间、扫 store、取标题。**无 COM、无 IO**，是本功能唯一有格式风险的部分，必须单测覆盖 |
| `src/GlassDesk.AppServices/JumpListTaskCatalog.cs` | AppServices | 建索引（exe 路径 → 任务列表）、缓存与失效、按 `AppEntry` 查任务、**执行**任务（`ShellExecutor` 调用收在这一层） |
| `src/GlassDesk.Launcher/LauncherWindow.JumpListTasks.cs` | Launcher | `partial LauncherWindow`：`ContextMenu.Opened` 时填充任务项、点击后交给 catalog 执行 |
| `tests/GlassDesk.AppServices.Tests/JumpListTaskTests.cs` | 测试 | 见 §7 |

唯一需要**改**的现有文件：`LauncherWindow.Interaction.cs` 的 `BuildTileMenu`，追加 1 行 `AttachJumpListTasks(menu, id);`。`AGENTS.md` §5 允许这种追加式改动。

依赖方向遵守现有分层：Launcher → AppServices → Platform。`GlassDesk.Launcher.csproj` **不新增** `GlassDesk.Platform` 引用（现在只是靠传递引用侥幸能编）：执行任务的方法放在 AppServices 的 catalog 上，Launcher 只认 catalog。

### 4.2 读取算法

```
1. 目录：Environment.GetFolderPath(ApplicationData)
         + "\Microsoft\Windows\Recent\CustomDestinations"
2. 对每个 *.customDestinations-ms（按 LastWriteTime 降序，总量 ~350KB）：
   a. 读全部字节（读不到/被占用 → 跳过该文件，不抛）。
   b. 找所有 link 签名偏移。
   c. 每个 link i 的区域 = [linkStart_i, linkStart_{i+1})（最后一个到 EOF）。
   d. 用 IShellLink 解析该 link：
      - IStream = SHCreateMemStream(区域字节起始 128KB 上限)   // 给足即可，link 自身按结构停
      - IPersistStream::Load → GetPath(SLGP_UNCPRIORITY) / GetArguments / GetWorkingDirectory
      - 失败 → 跳过该 link
   e. 在该区域内按 §3.1 扫 store，取 FMTID_SummaryInformation 块的标题锚点。
      区域里出现多个候选，按出现顺序取第一个通过校验的；解析不出 → 该 link 跳过。
   f. 产出 JumpListTask { ExecutablePath, Arguments, WorkingDirectory, Title, SourceFile, SourceFileWriteTimeUtc }
3. 目标路径规范化（见 §4.4）；exe 后缀以外的一律丢弃。
```

`IShellLink` 复用现成 interop：`src/GlassDesk.Platform/Interop/ShellCom.cs` 的 `IShellLinkW`、`Ole32.CoCreateInstance`，并按需补 `IPersistStream`（`{00000109-0000-0000-C000-000000000046}`）与 `SHCreateMemStream`（shlwapi 导出，走 `NativeLibrary`/`DllImport`，与仓库现有 `Shell32`/`Ole32` 写法一致）。

**为什么不自己解 link**：参数编码有 ANSI/Unicode、环境变量、相对路径、IDList 等分支；交给系统解析与 `.lnk` 解析保持同一份行为（`ShortcutResolver` 已是同样取舍，理由见其注释）。

**工作目录要读**：`IShellLinkW.GetWorkingDirectory` 已经声明在 `ShellCom.cs`，`ShortcutInfo` 也已经带 `WorkingDirectory`，`ShellExecutor.Open` 本来就有这个参数。丢了它，任务会在启动台自己的当前目录里跑起来——这种错不报错、难归因。

### 4.3 归属规则（哪个任务属于哪个程序）

一个文件里可能混着指向别的 exe 的 link（实测：VS Code / Cursor / Trae 的文件里都有指向 `explorer.exe` 的条目）。

规则：**任务只归属到它自己 link 里的目标 exe**。即索引键就是 `link.GetPath()` 的规范化结果。不做「文件主 exe」推断——推断会把 `explorer.exe` 的条目算到 VS Code 头上。

### 4.4 匹配规则（`AppEntry` → 索引键）

按顺序尝试，命中即止：

1. `entry.Target` 本身是 `.exe` → 规范化后查索引。
2. `entry.Target` 是 `.lnk`/`.url` 或不是 `.exe` → 用现成的 `ShortcutResolver` 解出真实目标再查（`ShortcutResolver` 已处理相对路径/环境变量）。
3. **文件名唯一回退**：取 `Path.GetFileName(目标)`，若索引里同名 exe 只有唯一一个键，则采用。
   - 理由：实测 Edge 的目标可能是 `%ProgramFiles(x86)%\...\msedge.exe`，而 entry 存的是 `C:\Program Files (x86)\...`；iTunes 在 `WindowsApps` 带版本号的目录里，升级后路径就变。只靠全路径匹配会漏。
   - 唯一性约束保证不会把「另一个 chrome.exe」的任务挂错。
4. **代理 exe 回退**：去掉 `_proxy` 后缀再按文件名唯一匹配。
   - 理由：Edge/Chrome 有 `msedge_proxy.exe` / `chrome_proxy.exe` 这类代理 exe 的先例，跳转列表可能指向它。**本机 44 个文件实测没有任何 `*_proxy.exe`**（`grep -aoiE "[a-z0-9_.-]*proxy\.exe"` 无输出），所以这条是防御性规则，不是已知故障；写成「唯一才采用」避免误挂。

规范化：`Environment.ExpandEnvironmentVariables` → `Path.GetFullPath` → `StringComparer.OrdinalIgnoreCase` 比较。

**实现时删掉了两项计划**（记录在此以免下次又写一遍）：

- **不做 8.3 短名还原**（`GetLongPathNameW`）：实测 `IShellLink::GetPath` 给的就是长名，文件里虽有 `PROGRA~2` 字样但读出来是 `Program Files (x86)`；剩下的短名差异由「文件名唯一」回退兜住。
- **没有「同目录下唯一就算」这一级**：同目录常常住着另一个程序（`powershell.exe` 与 `powershell_ise.exe` 都在 System32 那个目录），按目录猜会把邻居的任务挂过来，用户看到的是「右键 A，点出来的是 B」。①–④ 已经覆盖了当初想用它处理的两种真实情况（Edge 的环境变量路径、iTunes 的版本号目录）。

### 4.5 缓存与刷新

`JumpListTaskCatalog`（AppServices）持有一份**不可变快照**：`IReadOnlyDictionary<string /*规范化 exe 路径*/, IReadOnlyList<JumpListTask>>` + 构建时间 + 失败计数。

- **谁拥有**：catalog 是 `static` 懒加载缓存，没有实例生命周期，因此**不需要**改 `AppServiceHost` / `AppHost*.cs` 的装配层（避免碰中冲突面）。理由：它只是一份只读文件系统查询的缓存，没有可释放资源、没有监听器。
- **失效**：距上次构建 > 30 秒，**或**目录里最大的 `LastWriteTime` / 文件名集合变化。TTL 检查只做一次目录枚举（~44 项），很便宜。
- **线程与 COM 单元**：`IShellLink` 是 STA 组件，索引构建**就在调用线程同步做**（启动台是 WPF UI 线程 = STA）。实测 44 个文件 / ~350KB / ~200 个 link，量级是毫秒级；先用最简方案，不引入专用 STA 工作线程。若真机上超过 ~50ms，降级策略是「只读最近 N 个文件」（本机最近 12 个已覆盖 Edge/Chrome/Terminal/VS Code），而不是引入线程宿主。
- **首次为空时**：先建索引再填菜单（同步），不采用「本次空、下次才显示」——否则用户第一次右键找不到任何任务，会认为功能没生效。
- 重建永不抛：任何异常 → 保留旧快照，`LastFailureCount++`。
- 不做 `FileSystemWatcher`：目录会被系统频繁写入，30 秒 TTL 已足够。

### 4.6 菜单集成

**关键前提（评审指出）**：`BuildTileMenu` 不是在菜单打开时调用的，而是在**创建磁贴时**调用的（`LauncherWindow.Rendering.cs:151` `button.ContextMenu = BuildTileMenu(slot)`），且磁贴与它的 `ContextMenu` 会被长期复用（`_retainedTilePages` / `PrepareToShow(reuseUnchanged: true)`）。所以不能用「建菜单时就知道有哪些任务」的写法。

做法：

1. `BuildTileMenu` 追加 1 行 `AttachJumpListTasks(menu, id);`（新 partial 文件里）。
2. `AttachJumpListTasks` 给该 `ContextMenu` 挂 `Opened` / `Closed` 处理器：**Opened 时才**查 catalog、把任务项插到最前面、插完把当次生成的任务项记下来；**Closed 时**移除它们（下次打开重新生成）。已存在任务项则不重复插入。任务项插在最前面，后面跟一个 `Separator`，再是现有条目。
3. `CreateTrackedContextMenu` 已有的 Opened/Closed 计数钩子保持不变（否则窗口会误判失活）。

菜单形状：

```
[任务 1] [任务 2] ... [任务 N]
———————————
打开
编辑名称、图标和别名
…（现有条目原样保留）
```

- 任务最多显示 8 条。
- 任务项 = `MenuItem { Header = task.Title }`，与现有 `AddMenu` 风格一致。
- 点击任务：调 `JumpListTaskCatalog.Launch(task)`（内部 `ShellExecutor.Open(exe, args, cwd)`）；返回失败时用现有 `ShowNotice` 说明；成功 `CloseWithTransition()`（与 `ActivateEntry` 成功路径一致）。
- **不走** `LaunchService.ActivateOrLaunch`：任务的语义是「新建窗口/开新东西」，把已有窗口提到前面是错的。
- 分组条目（`LauncherSlotKind.Group`）不参与。

### 4.7 失败与降级

| 情况 | 行为 |
|---|---|
| `CustomDestinations` 目录不存在 | 空索引，菜单与今天完全一致 |
| 单文件读失败/被占用 | 跳过该文件 |
| link 解析失败、store 无标题 | 跳过该条目 |
| 该 entry 没有任务 | 不插入任何项，不加分隔线 |
| 任务执行失败 | `ShowNotice` 显示外壳错误，菜单已关闭、窗口保持打开 |
## 5. 冲突面与同步成本

- **低**（< 5 冲突点）：3 个新 Platform/AppServices/Launcher 文件 + 1 个测试文件全是新增；`LauncherWindow.Interaction.cs` 只在 `BuildTileMenu` 追加一行。
- 不碰 `GlassDesk.UI`（玻璃/着色器）、不碰布局算法，因此不属于高冲突面。

## 6. 风险

1. **格式属未公开文档**。缓解：解析全部集中在 `JumpListFormat` 纯函数里、单测覆盖真实样本的字节序列（合成 fixture，不含个人数据）、解不出就跳过而不是崩。格式变化时最坏结果是「任务消失」，不会影响右键菜单其它功能。
2. **COM 每次读 link**：本机 44 文件约 200 个 link，后台线程一次约几十毫秒。若实测偏慢，退化为「只读最近 N 个文件」（本机实测最近 12 个文件已覆盖 Edge/Chrome/Terminal/VS Code）。
3. **参数原样透传**可能带上档案夹/调试路径，与开始菜单行为一致但不是所有人期望。已在 §3.3 记录，若用户要过滤再单开任务。
4. **基准测试失败数**：验收看「不新增基线外失败」，`GlassDesk.AppServices.Tests` 基线是 378 中 46 失败（上游缺 `build/package.ps1`），改动后必须仍是 46。

## 7. 测试计划（TDD，先写测试再写实现）

`tests/GlassDesk.AppServices.Tests/` 下的 `JumpListFormatTests.cs`、`JumpListTaskReaderTests.cs`、`JumpListIndexTests.cs`、`JumpListTaskCatalogTests.cs`（外加共享 fixture `JumpListFixtures.cs`）：

1. `JumpListFormat`：
   - 合成 fixture：手工拼最小 shell link（`4C` 头 + CLSID + flags `0xE3` + IDList + LinkInfo + Arguments(UTF-16) + IconLocation + TerminalBlock）+ 一段 `size/1SPS/SummaryInformation/锚点标题` 的 store。
   - 断言：能定位 link 边界；能取到标题；store 长度越界 / 标题没有 NUL 结尾 / charCount 离谱 → 返回 null 而不抛。
   - 断言：区域里混入 binary 伪 `1F 00 00 00` 序列时仍取到真标题（对应 §3.2 踩过的坑）。
2. 归属与合并去重：同一 exe 出现在两个文件 → 任务合并，`(Title, Arguments, WorkingDirectory)` 相同的去重，顺序按文件 `LastWriteTime` 降序。
3. 匹配规则：`.exe` 直查、`.lnk` 经 `ShortcutResolver`、文件名唯一回退命中、同名两个 exe 时不回退。
4. 规范化：环境变量展开、大小写不敏感、短名长名视为同一键（用不存在的路径测纯字符串部分）。
5. 缓存失效：注入时钟/文件时间，验证 TTL 与文件变化触发重建；构建抛异常 → 保留旧快照。

真实数据的验证不放进单测（不能提交个人数据，也不该在 CI 里依赖本机文件）：改由 `tests/GlassDesk.LauncherAcceptance` 增加一段手工探针，打印「每个已知程序解出的任务条数」，人工核对 Edge/Chrome/Terminal 有 2+ 条且标题正确。

## 8. 验收清单（交付时必须逐条给结果）

1. `dotnet build src/GlassDesk.App/GlassDesk.App.csproj -c Release` → 0 error。
2. 三个测试工程：`GlassDesk.AppServices.Tests` 失败数仍为 46（无新增），`Core`/`Dock` 仍全绿。
3. 手工（需用户或我在有桌面的 Windows 上跑）：
   - 右键 Edge → 顶部出现 `新建窗口(N)`、`新建无痕窗口(I)`；点「新建无痕窗口」→ 打开 InPrivate 窗口。
   - 右键 Chrome → 出现 Chrome 自己的两条；点击生效。
   - 右键记事本（无跳转列表）→ 菜单与今天完全一致，无空分隔线。
   - 右键 VS Code → 出现 `新窗口` 等自有任务，且**不**出现属于 explorer 的条目。
4. `git status` 干净；提交身份为 `gzg66 <64040020+gzg66@users.noreply.github.com>`。

## 9. 留给实现者决定的点（已定，实现时可推翻但要写进交付说明）

- 任务上限 8 条。
- TTL 30 秒。
- 菜单里任务不加图标（只文字），避免再走一次图标提取链。
- 不新增设置开关。
- 缓存用 `static` 懒加载（不改 `AppServiceHost` / `AppHost*.cs`）。
- 索引构建在调用线程同步做（WPF UI = STA），不引入工作线程。
- 诊断信息对外只暴露一个 `LastFailureCount`（供验收探针用），不进 UI。
- `ContextMenu.Opened` 时插入任务项、`Closed` 时移除。
