# GlassDesk 二次开发 Fork 指南

本文件是 `AGENTS.md` 的展开版：为什么这么定、怎么一步步落地、出问题怎么判断。
日常操作看 `AGENTS.md`；想改策略、或遇到同步冲突时看本文件。

---

## 1. 上游仓库的事实（先说清约束）

| 项 | 实际情况 | 后果 |
|---|---|---|
| 许可证 | Apache-2.0（`LICENSE` + `NOTICE` + `docs/third-party/`） | 可商用、可闭源衍生；须保留版权/许可/修改声明 |
| 历史 | `main` 只有 4 个 commit，全是 `Release public source snapshot` 压缩快照（v1.2.2 / v1.3.0 / v1.3.1 / 一次 fix） | 无法 cherry-pick 单个修复；只能整版合并 |
| 分发 | 正式版 release 发在 `everlosttime/glassdesk-updates`，源码快照另外发 | 盯 updates 仓库可提早发现新版本 |
| 插件 | 无 DI、无加载器，`internal sealed partial` 手工装配 | 功能只能改源码 |
| CI | 无 `.github` | 自己建 |
| 平台 | WPF / .NET 10 / Windows 11 x64 | Linux/macOS 无法构建；WSL 里也不行，需 Windows 主机 |
| 已知缺陷 | 整解编译因 `GlassDesk.UiAcceptance` 目标框架偏低报 `NU1201` | 只构建 `GlassDesk.App.csproj` |

---

## 2. 为什么选这个方案

### 选定的方案
Fork + `upstream` 纯净镜像分支 + `main` 自有产品主线 + **merge 整版快照**升级。

### 为什么 `main` 不保持上游镜像
教科书做法是"`main` 永远等于 upstream、功能全在 feature 分支"。这里反过来更合适：
- 你**不会**给上游提 PR（上游只从私有库导出快照，公开仓库不是协作入口），所以"干净历史方便 PR"的收益为 0。
- 你会长期在产品层面改名、调设置、加功能；把 `main` 当上游镜子只会让你天天在自己的东西上写 `if fork`。
- 因此：`upstream` 专门当镜子，`main` 专门当产品。

### 为什么用 merge 而不是 rebase
上游每次升级是"一个 commit 覆盖整版改动"，delta 可能几千行。rebase 你的 N 个提交 = 在同一个大 delta 上连续撞 N 次冲突；merge 只在边界上撞一次。配合 `rerere` 效果更好。

### 为什么不用 submodule / subtree / vendor
- submodule/subtree：需求要改 Dock/WPF 内部（UI、布局、渲染），跨仓边界根本够不着。
- vendor（拷进来脱钩）：丢掉 `git diff` 这个唯一的上游变更追踪手段，等于蒙眼升级。

### 为什么不做插件层
没有扩展点。硬做两条路都不划算：反射 hack 脆弱；改 upstream 源码加加载器，本身已经是"改核心"，比直接改业务代码冲突面更大。

### 什么时候切换成硬分叉（停止同步）
任一条件成立即停：
- 改动集中在 `GlassDesk.UI`（玻璃/着色器/动画）超过约 3000 行；
- 连续两个版本同步时冲突超过 20 个文件；
- `glassdesk-updates` 超过一年没有 release（上游停更）；
- 产品定位与原名冲突，需要彻底独立品牌。

---

## 3. 第 0 步：迁移 remote（克隆上游源码后必须先做）

你目前的状态：`origin` 直接指向上游 `everlosttime/glassdesk-open-source`，`main` == 上游快照。

**本节已执行完毕，当前状态（2026-10-20）**：

| 项 | 现值 |
|---|---|
| `origin` | `gzg66/glassdesk-open-source`（自己的 fork，可推） |
| `upstream` | `everlosttime/glassdesk-open-source`，push URL = `DISABLED` |
| `main` | 产品主线，跟踪 `origin/main` |
| `upstream-mirror` | 上游镜像分支，跟踪 `upstream/main`，已推送到 fork |
| 本地 tag | `v1.2.2` / `v1.3.0` / `v1.3.1` 已推送 |

下面是当初的执行步骤，留作换机器 / 重建仓库时参考：

```powershell
# 1) 在 GitHub 上 Fork everlosttime/glassdesk-open-source 到你自己的账号
#    gh repo fork everlosttime/glassdesk-open-source --clone=false

# 2) 把原来的 origin 改名为 upstream
git remote rename origin upstream
git remote set-url --push upstream DISABLED        # 防误推

# 3) 加自己的 fork 作为 origin
git remote add origin https://github.com/gzg66/glassdesk-open-source.git
git fetch origin
git branch upstream-mirror upstream/main            # 镜像分支（不要叫 upstream，会和远端名撞）
git push -u origin main --tags
git push -u origin upstream-mirror

git remote -v      # origin=gzg66/...（可推）、upstream=everlosttime/...（push: DISABLED）
```

> 镜像分支名用 `upstream-mirror` 而不是 `upstream`：本地分支 `upstream` 与远端名 `upstream` 同名会导致 `git log upstream` 之类的歧义解析。

---

## 4. 第 1 步：品牌隔离（不做完别装到自己机器上）

原因：单实例互斥名和 `%APPDATA%\GlassDesk` 数据目录与官方版共享。两份程序同时存在会：
- 抢单实例锁，互相把对方顶掉；
- 共用 `settings.json` / `apps.json` / `dock-layout.json`，字段语义不同可能写坏用户数据；
- 你打的安装包 AppId 与官方一致，卸载/更新会互相干扰。

逐项改（完整表见 `AGENTS.md` §4）：

1. `AppPaths.ProductFolderName` → 你的品牌名（数据目录随之隔离）
2. `AppHost.InstanceName` → 你的品牌名
3. `packaging/update-source.json` + `AppHost.Updates.cs` 的 `defaultSource` → 你的 releases，或指向占位值并在注释里写明是刻意关闭
4. `InstallationLocator.AppId` ⇄ `packaging/GlassDesk-Capture.iss` 的 `AppIdValue` 断言、卸载注册表键、显示名/`GlassDesk.exe` 文件名校验 → 成套替换
5. `StartupRegistration.ValueName`（HKCU Run 项名）
6. `Directory.Build.props` 的 `Product` / `Company` / `Version`；`GlassDesk.App.csproj` 的 `AssemblyName` / `AssemblyTitle`
7. 图标与品牌资源：`src/GlassDesk.App/Assets/GlassDesk.ico`、`GlassDeskBrand.png`、`GlassDeskBrandMark.xaml`
8. `packaging/GlassDesk-Capture.iss` 的安装包名与显示名

**改完必须做的事**
- 用自己的版本号序列，避免和上游 `1.3.x` 比较时打架。
- 若保留自动更新：确认 `UpdateRelease` 能从你的 release 里解析出 `GlassDesk-{version}-win-x64-Setup.exe` 与 SHA256，否则更新会一直报"无法检查更新"。
- 保留 `LICENSE` / `NOTICE` / `docs/third-party/`，在改动过的上游文件头部加一行修改声明。

---

## 5. 第 2 步：建立基线（先证明"什么都没改也能构建通过"）

```powershell
dotnet build src/GlassDesk.App/GlassDesk.App.csproj -c Release
dotnet test tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj -c Release
dotnet test tests/GlassDesk.AppServices.Tests/GlassDesk.AppServices.Tests.csproj -c Release
dotnet test tests/GlassDesk.Dock.Tests/GlassDesk.Dock.Tests.csproj -c Release
dotnet run --project src/GlassDesk.App/GlassDesk.App.csproj -c Release
```

把结果记进 `docs/fork/BASELINE.md`（自建）：SDK 版本、Windows build、显卡、哪些测试本来就不绿。
**这是以后判断"是我改坏了还是上游本来就坏"的唯一依据。**

注意：程序数据写在运行目录旁的 `Data`；启动开发版前先从托盘退出已安装的正式版（上游 README 明确要求）。

---

## 6. 第 3 步：建 CI（上游没有）

最小可用 GitHub Actions（Windows runner）：

- `runs-on: windows-latest`
- `actions/setup-dotnet` 装 **10.0.401 或更新的 feature band**（`global.json` 硬钉版本，老 SDK 直接失败；`rollForward: latestFeature` 只放宽 band 内）
- 步骤：restore → `build App.csproj` → 三个 `dotnet test`
- 不要跑 `GlassDesk.slnx` 整解（`NU1201`）；不要把 `*Acceptance` / `*Probe` 放进 CI（需真实交互桌面）

另加"上游有新快照就提醒"的定时任务：比对 `everlosttime/glassdesk-open-source` 的 tag 与 `docs/fork/LAST-SYNCED`，不一致就开 issue/PR。

同步游标文件 `docs/fork/LAST-SYNCED` 只写一行，例如 `v1.3.1`。同步脚本用它算 diff 范围。

---

## 7. 上游发布后怎么升级

```powershell
pwsh -NoProfile -File scripts/sync-upstream.ps1 -DryRun   # 看 diff 规模
pwsh -NoProfile -File scripts/sync-upstream.ps1 -TargetTag v1.4.0
```

脚本做的事：校验 remote 状态 → `fetch --tags` → 打印 `LAST-SYNCED..目标` 的 `diff --stat` → 从 `main` 建 `sync/<tag>` → `merge` → 跑三个测试工程 → 提示人工 review 与合并步骤。

冲突处理原则：
1. **先看上游那边改了什么、为什么改**（`git diff <base> <tag> -- <冲突文件>`），再决定你的改动是否还成立。
2. 上游修了 bug 而你改过同一处 → 优先接受上游实现，把你的意图重放到新结构上。
3. 解完冲突立刻 `dotnet build` + 三个 `dotnet test`，不要攒着一起跑。
4. 合并提交信息写清"同步上游 vX.Y.Z，解决 N 处冲突，其中语义冲突 M 处"，方便回溯。

同步完成后更新 `docs/fork/LAST-SYNCED`。

---

## 8. 功能往哪写（按冲突面排序）

| 你想加的东西 | 落点 | 冲突面 |
|---|---|---|
| Dock 内置入口（类似 v1.3.1 的启动台/此电脑按钮） | `Core/Models/Settings/DockSettings.cs` 加开关 → `Dock/DockWindow.xaml(.cs)` 加按钮与可见性 → `Settings/SettingsWindow.xaml(.cs)` 加勾选 → 测试 | 低 |
| 新设置项 | `Core/Models/Settings/*` + `Settings` UI + 消费方 | 极低 |
| 新业务能力（搜索源、导入器、统计、快捷键） | 新文件放 `AppServices/`，契约放 `Core/Contracts/InternalContracts.cs`，在 `AppHost*.cs` 加装配 | 低 |
| 新窗口 / 新托盘菜单项 | `App/` 下新增 partial + `AppHost*.cs` 挂载 | 中 |
| 改玻璃材质 / 着色器 / 放大动画 | `src/GlassDesk.UI/`（9.4k 行核心） | 高 |
| 改启动台分页 / Dock 布局算法 | `Core/Models/LauncherLayout*`、`Dock/DockLayoutPolicy` | 高 |

**加法式改动模板**（上游自己就是这么加功能的，照抄最省事）：参考 v1.3.1 引入 `ShowLauncherEntry` / `ShowThisPcEntry` / `ShowDesktopIconsEntry` 的完整路径——设置模型加 bool → DockWindow 同步 `Visibility` → 设置窗口加 CheckBox → 相关计数/布局规则同步。

---

## 9. 排查清单

| 现象 | 首查 |
|---|---|
| 启动抢掉官方版窗口 / 官方版被顶掉 | `AppHost.InstanceName` 是否已改 |
| 设置莫名丢失 / 数据串了 | `AppPaths.ProductFolderName` 是否已改，是否与官方共用 `%APPDATA%\GlassDesk` |
| 弹出官方版本更新、你的功能消失 | `defaultSource` + 输出目录 `update-source.json` 未改 |
| 设置里"检查更新"永远失败 | 更新源改成自己的仓库，但资产名/SHA256 不匹配 `UpdateRelease` 解析规则 |
| 安装后自动更新不再提示 | `InstallationLocator` 判定 Mismatch（显示名/`GlassDesk.exe`/卸载键名与 AppId 不一致）——成套改名，或显式在 `InitializeUpdates` 里关掉并注释 |
| 整解编译 `NU1201` | 上游已知问题，只构建 `GlassDesk.App.csproj` |
| SDK 未找到 | `global.json` 要求 .NET 10.0.401+ |
| 改 HLSL 没效果 | 没跑 `build/compile-transition-shader.ps1` |
| Linux/WSL 里构建失败 | WPF 只能在 Windows 构建，正常现象 |

---

## 10. 法务与署名

- Apache-2.0：可闭源商用，但必须保留 `LICENSE`、`NOTICE`、`docs/third-party/` 全部内容与版权声明。
- 修改过的文件要标注"已被修改"（第 4(b) 条）。
- 保留上游对 AndroidLiquidGlass（Kyant）、Unicode Unihan 等第三方的归属说明。
- **必须更换产品名与图标**：README 明确"项目许可不授予第三方商标权利"，沿用 GlassDesk 名称与品牌资源出分叉有商标风险。
