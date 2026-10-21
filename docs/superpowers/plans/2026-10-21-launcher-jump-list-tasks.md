# 启动台右键「任务」列表 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 启动台里右键某个程序时，菜单顶部出现该程序在 Windows 跳转列表里注册的任务（如 Edge 的「新建窗口 / 新建无痕窗口」），点击即执行。

**Architecture:** `GlassDesk.Platform` 新增跳转列表读取器（纯字节解析 + 系统 `IShellLink` 读 link 字段），`GlassDesk.AppServices` 新增索引/匹配/缓存并把执行收在这一层，`GlassDesk.Launcher` 用一个新 partial 文件在 `ContextMenu.Opened` 时插入任务项。只改一行现有代码。

**Tech Stack:** .NET 10（`net10.0-windows10.0.26100.0`）、WPF、源生成 COM 互操作（`[GeneratedComInterface]`）、xUnit。

**Spec:** `docs/superpowers/specs/2026-10-21-launcher-jump-list-tasks-design.md`（施工前必读 §3 格式结论与 §3.2 死路）

## Global Constraints

- 仅 Windows 可构建；禁止 `dotnet build GlassDesk.slnx`（上游 `GlassDesk.UiAcceptance` 目标框架偏低，整解报 `NU1201`）。
- dotnet 用带 SDK 的那份：`"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"`（PATH 里的可能是纯运行时）。构建前先 `Get-Process GlassDesk -ErrorAction SilentlyContinue | Stop-Process -Force`。
- 不动 `dotnet format`、不改 `.editorconfig`、不重排现有文件、不把方法改成表达式体、不改 `global.json`、不碰 `GlassDesk.UI`。
- 能新文件就新文件；必须改的现有文件只在**末尾追加**（`AGENTS.md` §5）。
- 注释与文档用中文，风格对齐现有代码（`<summary>` + `<remarks>`，解释「为什么」而不是「做了什么」）。
- 隐私：任何新增文件、提交信息、注释里不写仓库主人真实姓名/公司邮箱；提交前 `git log -1 --format='%an <%ae>'` 必须是 `gzg66 <64040020+gzg66@users.noreply.github.com>`。
- 提交信息中文，写清「为什么」。推送需用户另行同意，本计划**不含** push。
- 测试基线：`GlassDesk.Core.Tests` 181/181、`GlassDesk.Dock.Tests` 481/481（需要 `D:\dotnet10` junction）、`GlassDesk.AppServices.Tests` 378 中 46 失败（上游缺 `build/package.ps1`，**不要修**）。判定标准是「不新增基线外失败」。
- 仓库里另有一个并行会话在开发别的功能。施工前 `git status` 确认干净；改 `src/GlassDesk.Launcher/LauncherWindow.Interaction.cs` 前先 `claim_files`（paths: `src/GlassDesk.Launcher/LauncherWindow.Interaction.cs`）并重读该文件确认行号仍对；构建/测试前确认没有别的会话正在跑同一输出目录。

## Review Focus

- **文件被系统写坏/写一半时**：用户期望右键菜单照旧能用，不是崩或空菜单。（Task 2 的 `ReadAll`/`ReadFile` 吞异常测试）
- **二进制垃圾被当成标题**：数据里存在伪 `1F 00 00 00` 序列，用户期望看不到乱码菜单项。（Task 1 的 junk 测试）
- **同名/同类 exe 挂错任务**：机器上有多个 `chrome.exe`（Playwright、Chrome for Testing），用户期望任务只属于这个程序。（Task 3 的歧义不回退测试）
- **首次右键没有任务**：用户期望首次打开就能用，不是「第二次才出现」。（Task 4 的首次构建同步测试）
- **任务启动到错误目录**：用户期望行为和开始菜单点击一致（工作目录透传）。（Task 2 的 `GetWorkingDirectory` 断言 + Task 4 把 cwd 传给 `ShellExecutor`）

---

### Task 1: `JumpListFormat` — 纯字节解析（找 link、取标题）

**Files:**
- Create: `src/GlassDesk.Platform/Shell/JumpListFormat.cs`
- Test: `tests/GlassDesk.AppServices.Tests/JumpListFormatTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `internal static class JumpListFormat`
  - `internal static IReadOnlyList<int> FindLinkOffsets(ReadOnlySpan<byte> file)` — 返回每个 shell link 头的偏移（升序）
  - `internal static string? TryReadTitle(ReadOnlySpan<byte> itemRegion)` — 在一个 item 区域（该 link 起点到下一个 link 起点）里按 §3.1 找 `1SPS` store、校验 `FMTID_SummaryInformation`、按 `1F 00 00 00` + `uint32 字符数` + UTF-16LE + 结尾 `U+0000` 取标题；任何一步不成立返回 `null`
  - 常量：`LinkSignatureLength = 20`

- [ ] **Step 1: 写失败测试**

`tests/GlassDesk.AppServices.Tests/JumpListFormatTests.cs`，包含一个内部 fixture 构造器（同文件内 `private static byte[]`）：
`BuildLink(arguments)` 生成最小 MS-SHLLINK：`uint32 0x4C` + CLSID `{00021401-0000-0000-C000-000000000046}` + flags `0x00000020`（仅 HasArguments）+ FileAttributes/3 个 FILETIME/FileSize/IconIndex/ShowCmd/HotKey/Reserved 全 0 + StringData（`uint16 字符数` + UTF-16LE 参数）+ TerminalBlock `uint32 0`。
`BuildTitleStore(title)` 生成 `<uint32 totalSize>` + `"1SPS"` + 16 字节 `FMTID_SummaryInformation {F29F85E0-4FF9-1068-AB91-08002B27B3D9}` + body。body 只有两部分：锚点 `1F 00 00 00`，然后是 `uint32 (字符数 + 1)` + UTF-16LE(title + `\0`)。`totalSize` 含义是**含自身这 4 字节**的总长（即 `4 + 4 + 16 + body.Length`）。

断言（每项一个 `[Fact]`）：
1. `FindLinkOffsets([link, titleStore, link])` 返回两个偏移，第二个 = `first + BuildLink(arguments).Length + BuildTitleStore(title).Length`。
2. `TryReadTitle(区域)` 取回 `"新建窗口(N)"`（含中文）。
3. 区域里先塞 64 字节伪 `1F 00 00 00` + `uint32 3` + 垃圾字节，再塞真 store → 仍返回真标题。
4. 区域里先塞 `1SPS` 但其前 `uint32` 长度越界（不是合法 store）→ 跳过它，仍返回后面真 store 的标题。
5. 标题没有结尾 `U+0000` → `null`。
6. `charCount = 0xFFFFFF` → `null`（不抛）。
7. store 的 `totalSize` 越过区域末尾 → `null`。
8. 空区域 → `null`。

- [ ] **Step 2: 跑测试确认失败**

Run: `"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests/GlassDesk.AppServices.Tests/GlassDesk.AppServices.Tests.csproj -c Release --filter "FullyQualifiedName~JumpListFormatTests"`
Expected: 编译失败 / `JumpListFormat` 不存在。

- [ ] **Step 3: 实现 `JumpListFormat`**

只读 `ReadOnlySpan<byte>`，`BinaryPrimitives` 读小端；每一处边界都用 `if (offset + need > span.Length) return null`；不做异常抛出（`try/catch` 也不要有，靠边界判断）。标题字符数上限取 512，`totalSize` 上限取 8192（实测最大远小于此）。XML 注释里写明 §3.2 的两条死路（`PSGetPropertyFromPropertyStorage` 返回 `0x8007000D`、启发式扫 UTF-16 会抓到垃圾）作为「为什么这样写」的依据。

- [ ] **Step 4: 跑测试确认通过**

Run: 同 Step 2 命令
Expected: 8 passed。

- [ ] **Step 5: 提交**

```bash
git add src/GlassDesk.Platform/Shell/JumpListFormat.cs tests/GlassDesk.AppServices.Tests/JumpListFormatTests.cs
git commit -m "feat(platform): 解析跳转列表文件的 link 位置与任务标题

跳转列表存的是未公开格式：每个 item 是 shell link 加若干
<长度/1SPS/fmtid/body> 存储块。标题只在 FMTID_SummaryInformation
块里，且必须按锚点读——用 propsys 的 PSGetPropertyFromPropertyStorage
读会返回 0x8007000D，靠扫 UTF-16 串则会把二进制垃圾当标题。"
```

---

### Task 2: `JumpListTaskReader` — 读真实文件，link 字段交给系统解析

**Files:**
- Create: `src/GlassDesk.Platform/Interop/Shlwapi.cs`
- Modify: `src/GlassDesk.Platform/Interop/ShellCom.cs`（文件末尾追加 `IPersistStream` 与 `ShellComIds.PersistStream`）
- Create: `src/GlassDesk.Platform/Shell/JumpListTaskReader.cs`
- Test: `tests/GlassDesk.AppServices.Tests/JumpListTaskReaderTests.cs`

**Interfaces:**
- Consumes: `JumpListFormat.FindLinkOffsets`、`JumpListFormat.TryReadTitle`（Task 1）；`ShellComIds.ShellLinkClass`/`ShellLinkW`、`StrategyBasedComWrappers` 用法、`Ole32.CoCreateInstance`（现有 `ShortcutResolver.cs:77` 起的模式）
- Produces:
  - `public sealed record JumpListTask(string ExecutablePath, string Arguments, string WorkingDirectory, string Title, string SourceFile, DateTime SourceFileWriteTimeUtc)`
  - `public static class JumpListTaskReader`
  - `public static IReadOnlyList<JumpListTask> ReadAll(string recentFolder)` — `recentFolder` 是 `...\Microsoft\Windows\Recent`，内部读其 `CustomDestinations\*.customDestinations-ms`，按文件 `LastWriteTimeUtc` 降序
  - `public static IReadOnlyList<JumpListTask> ReadFile(string filePath)` — 单文件；读失败返回空列表而不是抛
  - 内部 `private static bool TryReadLink(byte[] region, out string path, out string arguments, out string workingDirectory)`

- [ ] **Step 1: 写失败测试**

`JumpListTaskReaderTests.cs`。fixture 里的 link 字节全部靠**现有** `ShortcutWriter`（`src/GlassDesk.Platform/Shell/ShortcutWriter.cs`，仓库已有测试在用）造：先 `ShortcutWriter.Write(tempLnk, new ShortcutDefinition(TargetPath: @"C:\不存在\测试程序.exe", Arguments: "--inprivate", WorkingDirectory: @"C:\不存在"))`，再 `File.ReadAllBytes(tempLnk)`——`.lnk` 文件内容就是序列化的 shell link，可以直接拼进 fixture。这样不需要手拼 `LinkInfo`，也不引入第二套二进制写入器。

1. `ReadFile(不存在的路径)` → 空列表、不抛。
2. `ReadFile(内容为随机字节的临时文件)` → 空列表、不抛。
3. `ReadFile(ShortcutWriter 造出的 link 字节 + Task 1 的标题 store)` → 1 条任务：`Title == "新建窗口(N)"`、`Arguments == "--inprivate"`、`WorkingDirectory == @"C:\不存在"`、`Path.GetFileName(ExecutablePath) == "测试程序.exe"`、`SourceFileWriteTimeUtc` 等于文件写入时间。
4. 同一文件里拼两个 link（`linkA + storeA + linkB + storeB`）→ 2 条，顺序与文件内顺序一致。
5. 拼 `link + 无标题的垃圾` → 该 link 被跳过（0 条）。
6. `link` 字节被截断一半 + 标题 store → 该 link 被跳过、不抛。

注：fixture 文件用 `Path.GetTempFileName()`（`ReadFile` 不靠扩展名判断）并在 `finally` 删掉；`ShortcutWriter.Write` 返回 false 时直接 `Assert.Fail`，避免把「写不出样本」当成解析失败。

- [ ] **Step 2: 跑测试确认失败**

Run: `"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests/GlassDesk.AppServices.Tests/GlassDesk.AppServices.Tests.csproj -c Release --filter "FullyQualifiedName~JumpListTaskReaderTests"`
Expected: 编译失败（`JumpListTaskReader` 不存在）。

- [ ] **Step 3: 实现互操作与读取器**

- `Shlwapi.cs`：`[DllImport("shlwapi.dll", ExactSpelling = true)] internal static extern nint SHCreateMemStream(nint init, uint size);` 用 `Marshal.AllocHGlobal` 拷贝 region 字节（容量上限 256KB），`finally` 里 `Marshal.FreeHGlobal`；返回 0 表示失败。
- `ShellCom.cs` 末尾追加：
  ```csharp
  [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
  [Guid("00000109-0000-0000-C000-000000000046")]
  internal partial interface IPersistStream
  {
      void GetClassID(out Guid classId);
      [PreserveSig] int IsDirty();
      void Load(nint stream);
      void Save(nint stream, [MarshalAs(UnmanagedType.Bool)] bool clearDirty);
      void GetSizeMax(out ulong size);
  }
  ```
  `ShellComIds` 里追加 `internal static readonly Guid PersistStream = new("00000109-0000-0000-C000-000000000046");`（追加在类体末尾，不改动已有成员）。注释里写清「vtable 槽位顺序不能漏声明」。
- `JumpListTaskReader.TryReadLink`：`CoCreateInstance(ShellLinkClass → IShellLinkW)`（与 `ShortcutResolver` 完全同一套包装器与释放顺序）→ `Marshal.QueryInterface(linkPointer, in PersistStream, out stream)` → `IPersistStream.Load(shlwapi stream)` → `GetPath(容量 1024, flags 0)`、`GetArguments`、`GetWorkingDirectory`。
- `ReadFile`：`File.ReadAllBytes` 外包 `try/catch (IOException|UnauthorizedAccessException)` → 空列表；用 `FindLinkOffsets` 切区域；`TryReadTitle(region)` 为 null → 跳过该 link；`TryReadLink` 失败 → 跳过。
- `ReadAll`：目录不存在 → 空列表；逐个文件调 `ReadFile`，单个文件异常只跳过它。

- [ ] **Step 4: 跑测试确认通过**

Run: 同 Step 2 命令
Expected: 6 passed。

- [ ] **Step 5: 用真实数据人工核对一次（必须做，结果贴到交付说明）**

在 `tests/GlassDesk.CompatibilityProbe` 里**临时**加一个分支（不入库，核对完删掉）：`--jumplist-tasks` → `JumpListTaskReader.ReadAll(Environment.GetFolderPath(SpecialFolder.ApplicationData) + "\\Microsoft\\Windows\\Recent")`，按 `Path.GetFileName(ExecutablePath)` 分组打印 `标题 | 参数`。命令：

```powershell
"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" run --project tests/GlassDesk.CompatibilityProbe/GlassDesk.CompatibilityProbe.csproj -c Release -- --jumplist-tasks
```

Expected（本机实测基线）：`msedge.exe` 至少 2 条含「新建窗口」「新建无痕窗口」；`chrome.exe` 至少 2 条含「打开新的窗口」「打开新的无痕式窗口」；`wt.exe` 4 条 profile；`Code.exe` 含「新窗口」；`explorer.exe` 不应出现在 `Code.exe` 名下。若某个已知程序解不出标题，记录程序名与条数，**不要**为此改解析逻辑（属于 §3.3 承认的覆盖率限制）。

- [ ] **Step 6: 提交**

```bash
git add src/GlassDesk.Platform/Interop/Shlwapi.cs src/GlassDesk.Platform/Interop/ShellCom.cs src/GlassDesk.Platform/Shell/JumpListTaskReader.cs tests/GlassDesk.AppServices.Tests/JumpListTaskReaderTests.cs
git commit -m "feat(platform): 读跳转列表文件得到每个程序的任务

link 里的路径/参数/工作目录交给系统 IShellLink 解析（IPersistStream
从内存流加载），不自己解 MS-SHLLINK 的编码分支；标题用 Task 1 的
锚点取，取不到就跳过该条目，不显示垃圾字符串。"
```

---

### Task 3: `JumpListIndex` — 归属、规范化、匹配（纯函数）

**Files:**
- Create: `src/GlassDesk.AppServices/JumpListIndex.cs`
- Test: `tests/GlassDesk.AppServices.Tests/JumpListIndexTests.cs`

**Interfaces:**
- Consumes: `JumpListTask`（Task 2）；`AppEntry`（`GlassDesk.Core.Models`）、`ShortcutResolver.Resolve`（作为可注入委托的默认实现）
- Produces:
  - `internal static class JumpListIndex`
  - `internal static string NormalizeExecutable(string path)` — 去空白 → `Environment.ExpandEnvironmentVariables` → `Path.GetFullPath` → 非 `.exe` 返回空串 → 返回原串（**不**小写；比较一律用 `StringComparer.OrdinalIgnoreCase` 的字典）
  - `internal static Dictionary<string, IReadOnlyList<JumpListTask>> Build(IEnumerable<JumpListTask> tasks)` — 键 = `NormalizeExecutable(ExecutablePath)`；空键丢弃；同一键下按 `(Title, Arguments, WorkingDirectory)` 去重（保留先到的）；任务按 `SourceFileWriteTimeUtc` 降序排列
  - `internal static IReadOnlyList<JumpListTask> Match(AppEntry entry, IReadOnlyDictionary<string, IReadOnlyList<JumpListTask>> index, Func<string, string?>? resolveShortcut = null)` — 顺序：① `NormalizeExecutable(entry.Target)` 命中；② `entry.Target` 以 `.lnk` 结尾时用 `resolveShortcut`（默认 `p => ShortcutResolver.Resolve(p)?.Target`）解出目标再命中；③ 文件名唯一回退；④ 文件名去掉 `_proxy` 后唯一回退；⑤ 同目录（`Path.GetDirectoryName` 相等）的键唯一时采用；都不中 → 空列表
  - 常量 `MaxTasksPerApp = 8`（`Match` 返回值按此截断）

- [ ] **Step 1: 写失败测试**

`JumpListIndexTests.cs`（用手造的 `JumpListTask`，不碰文件系统）：
1. `Build` 合并同一 exe 的两个文件来源，按 `SourceFileWriteTimeUtc` 降序，`(Title, Arguments, WorkingDirectory)` 相同只留一条。
2. `Build` 丢弃 `ExecutablePath` 不是 `.exe` 的条目。
3. `NormalizeExecutable("%ProgramFiles(x86)%\\Microsoft\\Edge\\Application\\msedge.exe")` 展开成 `Environment.GetEnvironmentVariable("ProgramFiles(x86)")` 开头；大小写不同的两个路径 `Build` 后命中同一键。
4. `NormalizeExecutable("C:\\x\\readme.txt")` → 空串。
5. `Match`：`entry.Target` 精确等于索引键 → 命中。
6. `Match`：`entry.Target = "C:\\Users\\me\\Desktop\\Edge.lnk"`，注入 `resolveShortcut` 返回索引里的 exe 路径 → 命中。
7. `Match`：索引里只有一个 `msedge.exe`、entry 路径不同但文件名相同 → 命中（文件名唯一回退）。
8. `Match`：索引里有两个不同目录的 `chrome.exe`、entry 文件名 `chrome.exe` → **不命中**（歧义不回退）。
9. `Match`：索引键是 `msedge_proxy.exe`、entry 是 `msedge.exe` → 命中（`_proxy` 回退）。
10. `Match`：索引键与 entry 不同名但同目录、且该目录下只有一个键 → 命中；同目录有两个键 → 不命中。
11. `Match` 结果超过 8 条时截断到 8。

- [ ] **Step 2: 跑测试确认失败**

Run: `"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests/GlassDesk.AppServices.Tests/GlassDesk.AppServices.Tests.csproj -c Release --filter "FullyQualifiedName~JumpListIndexTests"`
Expected: 编译失败。

- [ ] **Step 3: 实现 `JumpListIndex`**

每个回退分支写成独立私有方法，注释说明为什么需要它（`_proxy`：Edge/Chrome 有 `msedge_proxy.exe` 先例，本机实测无，属防御；同目录：iTunes 装在带版本号的 `WindowsApps` 目录里，升级后路径会变）。所有回退都要求**唯一**，避免挂错程序。

- [ ] **Step 4: 跑测试确认通过**

Run: 同 Step 2 命令
Expected: 11 passed。

- [ ] **Step 5: 提交**

```bash
git add src/GlassDesk.AppServices/JumpListIndex.cs tests/GlassDesk.AppServices.Tests/JumpListIndexTests.cs
git commit -m "feat(appservices): 把跳转列表任务按 exe 归一化并匹配到应用条目

匹配四级回退（精确 → .lnk 解出目标 → 文件名唯一 → 代理/同目录唯一）。
所有回退都要求唯一：机器上存在多个 chrome.exe（Playwright、Chrome for
Testing），歧义时宁可没有任务也不能把别个程序的任务挂上来。"
```

---

### Task 4: `JumpListTaskCatalog` — 缓存、TTL、执行

**Files:**
- Create: `src/GlassDesk.AppServices/JumpListTaskCatalog.cs`
- Test: `tests/GlassDesk.AppServices.Tests/JumpListTaskCatalogTests.cs`

**Interfaces:**
- Consumes: `JumpListTaskReader.ReadAll`（Task 2）、`JumpListIndex.Build`/`Match`（Task 3）、`ShellExecutor.Open(string target, string? arguments, string? workingDirectory)`（现有 `GlassDesk.Platform.Shell`）
- Produces:
  - `public sealed record JumpListTaskLaunchResult(bool Started, string? Message)`
  - `public static class JumpListTaskCatalog`
  - `public static IReadOnlyList<JumpListTask> TasksFor(AppEntry? entry)` — `entry` 为 null 返回空列表；需要时（TTL 到期或目录变化）同步重建索引，然后 `Match`
  - `public static JumpListTaskLaunchResult Launch(JumpListTask task)` — `ShellExecutor.Open(task.ExecutablePath, task.Arguments, task.WorkingDirectory)`；`Started == false` 时 `Message = "任务未能启动（外壳错误码 {outcome.ErrorCode}）。"`
  - `public static int LastFailureCount { get; }`
  - 测试接缝（`internal`）：`internal static Func<string> RecentFolder`（默认 `%APPDATA%\Microsoft\Windows\Recent`）、`internal static Func<DateTimeOffset> Clock`、`internal static Func<string, IReadOnlyList<JumpListTask>> Reader`（默认 `JumpListTaskReader.ReadAll`）、`internal static Func<string, string?, string?, (bool Started, int ErrorCode)> LaunchShell`（默认转发 `ShellExecutor.Open`）、`internal static void ResetForTests()`

- [ ] **Step 1: 写失败测试**

`JumpListTaskCatalogTests.cs`（全部用注入的 `Reader`/`Clock`/`RecentFolder`，`ResetForTests()` 在每个 `[Fact]` 开头与 `finally` 里各调一次）：
1. 首次 `TasksFor` 就返回任务（不是第二次才有）——`Reader` 返回 1 条，断言立即命中。
2. `Reader` 只被调用一次：连续两次 `TasksFor`（时钟不变）→ 计数仍为 1。
3. TTL 到期重建：把 `Clock` 前进 31 秒 → 第二次 `TasksFor` 时 `Reader` 计数变 2。
4. `Reader` 抛异常 → `TasksFor` 返回空列表、`LastFailureCount == 1`、不抛；随后 `Reader` 正常返回 → 旧快照仍是空、下一次 TTL 到期后恢复。
5. `Launch` 把三个参数原样交给外壳：注入 `LaunchShell` 记下收到的 `(target, arguments, workingDirectory)`，断言与 `task` 的三个字段逐个相等（这样才能盖住「工作目录忘了透传」这种事）；再注入返回 `(false, 5)` 时 `Launch` 返回 `Started == false` 且 `Message` 含 `5`。

测试不得调用真实 `ShellExecutor`（会真的尝试拉起进程），每个 `[Fact]` 结尾把 `LaunchShell` 还原。

- [ ] **Step 2: 跑测试确认失败**

Run: `"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests/GlassDesk.AppServices.Tests/GlassDesk.AppServices.Tests.csproj -c Release --filter "FullyQualifiedName~JumpListTaskCatalogTests"`
Expected: 编译失败。

- [ ] **Step 3: 实现 `JumpListTaskCatalog`**

`static` 类 + `lock` 保护快照字段（`Dictionary`、构建时间、`LastFailureCount`）；索引构建在调用线程同步进行（WPF UI 线程是 STA，`IShellLink` 需要 STA——注释写明这一点，以及「若真机超过 ~50ms 的降级方案是只读最近 N 个文件，不是引入工作线程」）。`TasksFor(null)` 返回空列表。

- [ ] **Step 4: 跑测试确认通过**

Run: 同 Step 2 命令
Expected: 5 passed。

- [ ] **Step 5: 提交**

```bash
git add src/GlassDesk.AppServices/JumpListTaskCatalog.cs tests/GlassDesk.AppServices.Tests/JumpListTaskCatalogTests.cs
git commit -m "feat(appservices): 跳转列表任务的索引缓存与执行

快照是不可变字典 + 30 秒 TTL + 失败计数；构建在调用线程同步做，因为
IShellLink 是 STA 组件而启动台就是 UI 线程，不额外引线程宿主。执行收在
这一层，启动台项目因此不需要引用 GlassDesk.Platform。"
```

---

### Task 5: 启动台菜单接入

**Files:**
- Create: `src/GlassDesk.Launcher/LauncherWindow.JumpListTasks.cs`
- Modify: `src/GlassDesk.Launcher/LauncherWindow.Interaction.cs`（`BuildTileMenu` 末尾追加 1 行）
- Test: 手工 GUI（无自动化测试；`tests/GlassDesk.Dock.Tests` 现有断言不涉及菜单项，跑一遍确认没弄坏）

**Interfaces:**
- Consumes: `JumpListTaskCatalog.TasksFor(AppEntry)`、`JumpListTaskCatalog.Launch(JumpListTask)`（Task 4）；`_services?.Library.Find(id)`、`ShowNotice(string, bool)`、`CloseWithTransition()`、`CreateTrackedContextMenu()`（现有成员）
- Produces: `private void AttachJumpListTasks(ContextMenu menu, string entryId)`

- [ ] **Step 1: 先 `claim_files` 并重读将改的文件**

`claim_files(paths: ["src/GlassDesk.Launcher/LauncherWindow.Interaction.cs"])`，然后 `read` 该文件确认 `BuildTileMenu` 仍在 88 行附近、结尾是 `return menu;`。

- [ ] **Step 2: 写菜单接入（无测试先行，靠 Step 4 手验）**

新 partial 文件实现：`AttachJumpListTasks` 挂 `menu.Opened` / `menu.Closed`；`Opened` 里用 `List<MenuItem>` 记录本次插入的任务项，`Insert(0, …)` 到最前并追加一个 `Separator`（仅当确实有任务且菜单里还没有任务项）；`Closed` 里移除这些项与分隔线，清空列表。点击任务项时：`var result = JumpListTaskCatalog.Launch(task);` 失败 `ShowNotice(result.Message!)`，成功 `CloseWithTransition()`。

`LauncherWindow.Interaction.cs` 的 `BuildTileMenu` 中，普通程序分支的最后一个 `AddMenu(...)` 之后（`}` 之前）追加一行：`AttachJumpListTasks(menu, id);`。

- [ ] **Step 3: 构建**

```powershell
Get-Process GlassDesk -ErrorAction SilentlyContinue | Stop-Process -Force
"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build src/GlassDesk.App/GlassDesk.App.csproj -c Release
```
Expected: 0 error、0 新增 warning（`GlassDesk.Launcher` 不得新增 `GlassDesk.Platform` 引用）。

- [ ] **Step 4: 手工验收（GUI，需真实桌面）**

```powershell
"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" run --project src/GlassDesk.App/GlassDesk.App.csproj -c Release
```
逐条核对并记录结果：
1. 右键 Edge → 顶部两条任务；点「新建无痕窗口」→ 真的出现 InPrivate 窗口，启动台关闭。
2. 右键 Chrome → Chrome 自己的两条；点击生效。
3. 右键记事本（或任何没有跳转列表的程序）→ 菜单与改动前完全一致，**没有**空分隔线。
4. 右键 VS Code → 有「新窗口」，且**没有**属于 explorer 的条目。
5. 同一磁贴第二次右键 → 任务项不重复（验证 Closed 清理逻辑）。
6. 右键菜单打开时窗口不自动隐藏（验证 `_openContextMenuCount` 计数没被破坏）。

- [ ] **Step 5: 跑三个测试工程确认无新增失败**

```powershell
"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj -c Release
"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests/GlassDesk.Dock.Tests/GlassDesk.Dock.Tests.csproj -c Release
"$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests/GlassDesk.AppServices.Tests/GlassDesk.AppServices.Tests.csproj -c Release
```
Expected: Core 181/181 通过；Dock 481/481 通过；AppServices 失败仍为 46（含 Task 1–4 新增的全绿）。

- [ ] **Step 6: 提交**

```bash
git add src/GlassDesk.Launcher/LauncherWindow.JumpListTasks.cs src/GlassDesk.Launcher/LauncherWindow.Interaction.cs
git commit -m "feat(launcher): 右键菜单顶部显示该程序自己的任务

BuildTileMenu 是在建磁贴时调用的、菜单对象会被长期复用，所以任务项
在 ContextMenu.Opened 时才插入、Closed 时移除，保证任务变化能被看到。
分组条目不带任务。"
```

---

### Task 6: 交付收尾（文档、身份、状态、手验清单）

**Files:**
- Modify: `docs/superpowers/specs/2026-10-21-launcher-jump-list-tasks-design.md`（仅当实测结论与文档不符时更正，例如覆盖率数字）
- 无代码改动

- [ ] **Step 1: 全库诊断**

`lens_diagnostics(mode=all)` 确认没有本次改动引入的阻塞错误；有误报就用 `lens_diagnostic_mark` 标记。

- [ ] **Step 2: 身份与干净度自检**

```bash
git status --short
git log -1 --format='%an <%ae>'
```
Expected: 工作区干净；身份输出 `gzg66 <64040020+gzg66@users.noreply.github.com>`；没有 `Data/`、`artifacts/` 混入。

- [ ] **Step 3: 写交付说明**（不进仓库，直接回给用户）

必须包含四样：改了什么（文件级）、自检命令与结果（含 Task 2 Step 5 的真实数据核对输出）、冲突面等级（低）+ 残留风险（§3.3 覆盖率限制、`CustomDestinations` 格式属未公开文档）、需要用户手验的 GUI 行为（Task 5 Step 4 的 6 条）。

- [ ] **Step 4: 等用户确认后再询问是否推送**

推送必须逐次获得明确同意；不在本计划范围内。
