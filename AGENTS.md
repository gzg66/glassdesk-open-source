# AGENTS.md — GlassDesk 二次开发仓库契约

> 本文件是给所有 coding agent 的**常驻规则**，每次会话都要遵守。
> 与本文件冲突的"通用最佳实践"一律以本文件为准。
> 展开版：`docs/fork/FORK-GUIDE.md`；上游同步细则：`docs/fork/AGENT-SYNC-PROTOCOL.md`。

---

## 0. 身份与隐私（最高优先级硬规则）

**任何提交、推送、PR/issue、代码注释、文档里，都不许出现仓库主人的真实姓名或公司邮箱。**

- 本仓库 git 身份已固定在仓库级配置：
  - `user.name = gzg66`
  - `user.email = 64040020+gzg66@users.noreply.github.com`
- 提交前自检：`git log -1 --format='%an <%ae>'` 必须输出上面两个值。若显示其它值：
  1. 不要推；
  2. `git config user.name gzg66` / `git config user.email 64040020+gzg66@users.noreply.github.com`（**仓库级，不加 `--global`**）；
  3. 已提交未推的，用 `git commit --amend --reset-author --no-edit` 或 `git rebase <基准> --exec "git commit --amend --no-edit --reset-author"` 改写。
- 全局 git 身份是另一套用途（工作仓库），**不要用 `--global` 改它**。
- 禁止在新增文件里写本机完整路径以外的个人可识别信息；发布/故障报告里也不要写真实姓名。
- 若发现历史里已经混入真名，立即告知用户，不要静默强推。

**推送（push）必须逐次获得用户明确同意。**

- 默认只做本地提交，**不 fetch-push、不 force-push、不建 PR、不建 release**。
- 需要推送时：先列出"将要推送的 commit（sha + 标题 + 影响范围）"，说明是否 force、是否改历史，等用户回"推"再执行。
- 一次批准的推送只覆盖当次那一批 commit；后续新增 commit 要重新征求同意。
- 已经在远端的分支不要擅自改写历史（历史改写会改变他人看到的 sha）。

---

## 1. 这是什么仓库

- GlassDesk（Windows 11 桌面美化：玻璃 Dock + 启动台 + 设置中心）的 **Apache-2.0 二次开发 fork**。
- 上游 `everlosttime/glassdesk-open-source` **只发布压缩快照**：`main` 每个 commit 是一个完整版本的 squash，公开历史只有 v1.2.2 / v1.3.0 / v1.3.1 三个快照，原始开发历史在私有库。
- 本仓库是自己的 fork：`origin = gzg66/glassdesk-open-source`。
- 技术栈：.NET 10（`net10.0-windows10.0.26100.0`）+ WPF，**只能 Windows 构建**。`global.json` 固定 SDK `10.0.401`（`rollForward: latestFeature`，实测 10.0.402 可用）。
- 规模：src ~40k 行（UI 9.4k / Platform 8.4k / App 7.7k / Core 3.8k / Dock 3.8k / AppServices 3.6k / Launcher 2.4k / Settings 1.2k），tests ~32k 行、13 个工程。

**架构**：`Core`(模型/存储/契约) → `Platform`(Win32/注册表/显示) → `AppServices`(业务) → `UI`(控件/着色器) → `Dock` / `Launcher` / `Settings`(窗口) → `App`(组装，`AppHost*.cs`)。

---

## 2. 三条硬事实

1. **没有插件体系**。无 DI、无加载器，`internal sealed partial` 在 `AppHost.cs` 手工装配，`InternalsVisibleTo` 只对 tests 开放。→ **功能只能 in-tree 改**。
2. **上游历史是压缩快照**。→ **不要 cherry-pick 上游 commit**；升级只能整版合并；**同步用 merge，不用 rebase**。
3. **程序内置自动更新**，指向 `everlosttime/glassdesk-updates`。→ 不改更新源，自己的构建会被官方安装包静默覆盖。

---

## 3. 分支模型（已搭建完成，不可打折）

```
origin/main           自己的 fork（gzg66/glassdesk-open-source）
origin/upstream-mirror  上游镜像分支
main                  我们的产品主线（跟踪 origin/main）
upstream-mirror       本地镜像分支，跟踪 upstream/main，永不提交
sync/vX.Y.Z           上游同步分支，从 main 出
feat/xxx              功能分支，从 main 出
```

- `upstream` = `everlosttime/glassdesk-open-source`，push URL 已设为 `DISABLED`，**不要改回来**。
- 禁止在 `upstream-mirror` 上提交。
- 每次提交单一目的；提交信息写清"为什么"。
- 开工前先 `git status` + `git remote -v` 确认状态；`origin` 必须指自己的 fork。

---

## 4. 品牌隔离清单（分叉第一优先级，尚未执行）

未完成前，**不要**在有官方 GlassDesk 安装版同一台 Windows 上运行本仓库构建的产物（会抢单实例锁 + 共用 `%APPDATA%\GlassDesk`）。

| 项 | 位置（按符号名定位） | 现值 |
|---|---|---|
| 用户数据目录名 | `src/GlassDesk.Core/Storage/AppPaths.cs` → `ProductFolderName` | `"GlassDesk"` |
| 单实例互斥名 | `src/GlassDesk.App/AppHost.cs` → `InstanceName` | `"GlassDesk.Host"` |
| 更新源（硬编码兜底） | `src/GlassDesk.App/AppHost.Updates.cs` → `defaultSource` | `.../everlosttime/glassdesk-updates/releases/latest` |
| 更新源（运行期配置） | `packaging/update-source.json`（`GlassDesk.App.csproj` 复制为输出目录 `update-source.json`，覆盖 `defaultSource`） | 同上 |
| 安装身份 AppId | `src/GlassDesk.Platform/Installation/*` → `AppId`；`packaging/GlassDesk-Capture.iss` 有 `#if AppIdValue != "GlassDesk.Independent"` 断言，必须同步改 | `"GlassDesk.Independent"` |
| 卸载注册表键 | `InstallationLocator`：`...\Uninstall\GlassDesk.Independent_is1` | 同上 |
| 安装校验显示名/文件名 | `InstallationLocator`：`displayName == "GlassDesk"`、`GlassDesk.exe` | `GlassDesk` |
| 开机自启 Run 值名 | `src/GlassDesk.Platform/Startup/StartupRegistration.cs` → `ValueName` | `"GlassDesk.Independent"` |
| 程序集/产品名 | `GlassDesk.App.csproj`（`AssemblyName`/`AssemblyTitle`）；`Directory.Build.props`（`Product`/`Company`） | `GlassDesk` |
| 品牌资源 | `src/GlassDesk.App/Assets/GlassDesk.ico`、`GlassDeskBrand.png`、`GlassDeskBrandMark.xaml` | — |
| 版本号 | `Directory.Build.props`：`Version` / `FileVersion` / `AssemblyVersion` | `1.3.1` / `1.3.1.0` / `0.2.2.0` |

**注意**：改完 `AppPaths` / `InstanceName` 后 `InstallationLocator.Inspect` 会返回 Mismatch，"启动自动检查更新"不再触发；但设置中心的**手动"检查更新"仍会请求 `defaultSource`**，所以更新源必须显式改掉并留注释。

**更新源指回自己仓库时**：release 资产名必须匹配 `GlassDesk-{version}-win-x64-Setup.exe` 且带 SHA256（见 `UpdateRelease`、`tests/GlassDesk.AppServices.Tests/UpdateClientTests.cs`）。

**许可**：保留 `LICENSE`、`NOTICE`、`docs/third-party/`；改动过的上游文件加修改声明（Apache-2.0 §4(b)）；**必须换名换图标**（README 明示不授予商标权利）。

---

## 5. 修改纪律（控制同步冲突面）

- **能新文件就别改老文件**；必须改时**追加**在原位置之后，不重排、不改顺序。
- **禁止**：全库 `dotnet format`、批量改 using、改 `.editorconfig`、把方法改成表达式体、重命名上游标识符。
- 改动尽量压在**一个项目**里；跨项目优先"新增 + `AppHost*.cs` 加几行装配"。
- **不要改 `README.md`**（上游文件）。产品说明写 `docs/fork/`。
- 增删设置项要成对：`Core/Models/Settings/*` 加字段 → `Settings` UI → `Dock`/`Launcher` 消费 → 测试。模板见 v1.3.1 的 `ShowLauncherEntry` / `ShowThisPcEntry` / `ShowDesktopIconsEntry`。

**冲突面分级（交付说明必须标注）**

| 级别 | 位置 | 同步成本 |
|---|---|---|
| **低** | 新增 `AppServices`/`Core` 文件、`Settings` 加项、`Dock` 加内置入口 | <5 冲突点 |
| **中** | `AppHost*.cs` 装配层、`DockWindow` / `LauncherWindow` 交互 | 需人工过逻辑 |
| **高** | `GlassDesk.UI`（玻璃/着色器/动画）、`LauncherLayout*` / `DockLayoutPolicy` | 基本每次重做；超 ~3000 行考虑硬分叉 |

---

## 6. 构建与测试（仅 Windows）

```powershell
dotnet build src/GlassDesk.App/GlassDesk.App.csproj -c Release

dotnet test tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj -c Release
dotnet test tests/GlassDesk.AppServices.Tests/GlassDesk.AppServices.Tests.csproj -c Release
dotnet test tests/GlassDesk.Dock.Tests/GlassDesk.Dock.Tests.csproj -c Release
```

**基线：不是全绿，别当成自己改坏了**（详见 `docs/fork/BASELINE.md`，机器可读 `docs/fork/BASELINE.json`）

| 工程 | 基线 |
|---|---|
| Core | 181/181 通过 |
| Dock | 481/481 通过（**依赖 `D:\dotnet10` → SDK 目录的 junction**，测试里写死了作者机器路径；没有 junction 会有 5 个 `Win32Exception`） |
| AppServices | 378 中 46 失败 —— 公开快照缺 `build/package.ps1`，**属上游问题，不要修** |

- 判定标准是"**不引入基线外新失败**"，不是"必须全绿"。
- **构建前先退出正在运行的 GlassDesk**，否则输出目录被占用，报 `MSB3021 无法将文件…复制到…because it is being used by another process`。这不是代码错误：
  ```powershell
  Get-Process GlassDesk -ErrorAction SilentlyContinue | Stop-Process -Force
  ```
- **发布便携版（自我使用/分发都走脚本，别手打命令）**
  ```powershell
  powershell -NoProfile -File scripts/publish-portable.ps1          # 输出 artifacts\publish
  powershell -NoProfile -File scripts/publish-portable.ps1 -Run     # 发布后立即启动
  powershell -NoProfile -File scripts/publish-portable.ps1 -Zip     # 额外打 artifacts\GlassDesk-<ver>-win-x64-portable.zip
  powershell -NoProfile -File scripts/publish-portable.ps1 -ResetData # 丢弃旧数据，从空设置/空应用库开始
  ```
  运行时数据（`settings.json`、`apps.json`、图标、布局）就在输出目录的 `Data\` 下。脚本会在清理前把它备份到 `artifacts\data-snapshot`，发布完（打完 zip 之后）再放回去——**重新发布不再清空设置与已导入的程序**；只有 `-ResetData` 才会丢。
  脚本自动处理三件事：① 解析**带 SDK** 的 dotnet（PATH 里的 `C:\Program Files\dotnet` 可能只有运行时，手打会报 `No .NET SDKs were found`，**这不是 global.json 问题**）；② 停掉占用输出目录的 GlassDesk（否则 `MSB3021`）；③ 复制 `LICENSE`/`NOTICE`/`docs/third-party`，并校验输出里没有 `Data/`、`settings.json`、`apps.json`，且确为 self-contained（有 `coreclr.dll`/`hostfxr.dll`）。
  实测：285 文件 / 195.9MB（不含 Data）/ `GlassDesk.exe` 0.28MB / fileVersion 1.3.1.0；zip 76.6MB；增量耗时 ~16s。
  退出码：`1` 前置检查或清理失败、`2` publish 失败、`3` 产物校验或启动失败。
  分发只拷 `artifacts\publish`（或 zip）；`Data/` 是运行时在 exe 同目录生成的，**不要打进分发包**。
- **禁止 `dotnet build GlassDesk.slnx`**：`tests/GlassDesk.UiAcceptance` 目标框架偏低，整解报 `NU1201`（上游已知）。
- `tests/*Acceptance`、`*Probe` 是验收探针，需真实交互桌面，手工跑，不进 CI。
- SDK 低于 10.0.401 直接失败；报错别去改 `global.json`。
- 运行编译产物：本机已装系统级 **.NET 10 Windows Desktop Runtime 10.0.13**（`C:\Program Files\dotnet`），直接双击 exe 即可。换机器若只有用户级 SDK，apphost 会报 "You must install .NET"，此时设 `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"`，或按 README 做 `dotnet publish -r win-x64 --self-contained true`。
- dotnet CLI 装在 `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`（用户级 SDK 10.0.402，不在 PATH）；同步脚本会自动解析，手工跑命令时用全路径或先加 PATH。
- 改 `src/GlassDesk.UI/Effects/TransitionGlass.hlsl` 后必须跑 `build/compile-transition-shader.ps1`。

---

## 7. 上游同步

现在没有上游更新时**不要**跑同步。检查与执行：

```powershell
powershell -NoProfile -File scripts/check-upstream.ps1     # 0=最新, 10=有新快照, 1=环境问题
powershell -NoProfile -File scripts/sync-upstream.ps1 -DryRun
powershell -NoProfile -File scripts/sync-upstream.ps1      # 0=OK 1=前置失败 2=有冲突 3=验证失败
```

- 有冲突：读 `artifacts/fork-sync/<时间戳>/REPORT.md`，逐文件解，每解一个立即编译，然后 `-Resume` 续跑。
- 冲突处理与自主边界**严格按** `docs/fork/AGENT-SYNC-PROTOCOL.md`：低/中冲突面可自主解决，高冲突面或上游删挂载点必须停下问人；最多 3 轮。
- 同步完成后必须：合并回 `main` → 更新 `docs/fork/LAST-SYNCED` → 推送 → 交付说明含手验清单。
- 压缩快照下 `git diff` 是唯一可信的上游变更来源，**不要靠 commit message 推断**。

---

## 8. 禁区

- 不在 `upstream-mirror` 提交；不 rebase 已合并的上游快照；不 cherry-pick 上游 commit。
- 不改 `UpdateClient` / `UpdateInstaller` / `UpdateRelease` 的内部校验逻辑（只改装配层与配置来源）。
- 不提交 `Data/`、`artifacts/`、日志、截图、本机路径。
- 不为"整洁"重构未涉及的上游代码；不删测试求绿；不用 `-X ours/theirs` 整文件覆盖冲突。
- 不乱改 `AssemblyVersion`。

---

## 9. 交付前检查（缺一不算完成）

1. `dotnet build src/GlassDesk.App/GlassDesk.App.csproj -c Release` → 0 error。
2. 三个测试工程：**无基线外新失败**（基线见 §6）。
3. `git status` 干净，无 `Data/`、`artifacts/` 混入。
4. `git log -1 --format='%an <%ae>'` 为 `gzg66 <64040020+gzg66@users.noreply.github.com>`。
5. 说明里写清：**改了什么（文件级）**、**自检命令与结果**、**冲突面等级 + 残留风险**、**需要用户手验的 GUI 行为**。
