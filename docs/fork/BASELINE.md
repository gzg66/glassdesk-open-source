# 测试基线（改动前本来就是什么状态）

没有基线，就分不清"我改坏了"和"上游本来就红"。本文件记录首次构建时的实测状态；机器可读版本在 `BASELINE.json`，同步脚本用它判定"是否引入新失败"。

---

## 采集环境

| 项 | 值 |
|---|---|
| OS | Windows 11 专业工作站版 10.0.26200 |
| .NET SDK | 10.0.402（用户级安装于 `%LOCALAPPDATA%\Microsoft\dotnet`，不在 PATH） |
| .NET Desktop Runtime | 10.0.13（系统级，`C:\Program Files\dotnet`）——编译产物可直接运行，无需 DOTNET_ROOT |
| 仓库 | `D:\study\workspace\个人项目\glassdesk-open-source` |
| 代码版本 | v1.3.1 快照（`c825c9d`），未做任何代码修改 |
| 采集日期 | 2026-10-20（加入自有测试后重新采集） |

## 实测结果

| 测试工程 | total | passed | failed | 结论 |
|---|---|---|---|---|
| `GlassDesk.Core.Tests` | 192 | 192 | 0 | 全绿（含自有 DockVisibility 3 个、WheelNavigation 8 个、Normalize 回归 1 个、WinKeyGestureRouter 8 个、WinKeyIdentity 4 个、LauncherEnterRouter 7 个） |
| `GlassDesk.AppServices.Tests` | 378 | 332 | **46** | 基线红，原因见下（含自有 DesktopEntryScanner 测试 6 个） |
| `GlassDesk.Dock.Tests` | 481 | 481 | 0 | 全绿（**需要先做下面的 junction**） （含自有 FullscreenWindowRules 4 个）|
| `dotnet build src/GlassDesk.App/... -c Release` | — | — | — | 0 警告 0 错误 |

## 已知失败原因 1：Dock 测试硬编码作者机器路径

`tests/GlassDesk.Dock.Tests/Windows11BackdropCompatibilityTests.cs` 里直接写死：

```csharp
var start = new ProcessStartInfo("D:\\dotnet10\\dotnet.exe") { ... };
```

这是上游作者本机的 SDK 路径。别人的机器上没有这个文件 → 5 个测试抛 `Win32Exception: 系统找不到指定的文件`。

**修复（不改源码，避免增加冲突面）**：

```powershell
cmd /c mklink /J D:\dotnet10 "%LOCALAPPDATA%\Microsoft\dotnet"
```

建完 junction 后 Dock 测试 477/477 全绿。**不要把测试里的路径改成 `Environment.ProcessPath`**——那是改上游文件，会永久增加同步冲突面；上游如果把路径参数化了你再跟随。

## 已知失败原因 2：公开快照缺 `build/package.ps1`

46 个 `AppServices` 失败全部源自 `Assert.Contains` / `Assert.Equal` 之类断言，根因是测试要读：

```
build\package.ps1
```

而公开源码快照里 `build/` 只有 `build.ps1`、`publish.ps1`、`compile-transition-shader.ps1`、`New-GlassDeskIcon.ps1`。**`package.ps1` 属于未导出的私有部分。**

表现：
- `AcceptanceScriptPathTests.Inno编译参数必须逐项传递…` → `FileNotFoundException: build\package.ps1`
- `PackageOutputGuardTests.*` → 期望退出码 2，实际拿到别的（脚本不存在）
- `PackageVerificationTests.*` → 同上

**处理：记录为已知失败，不修。** 自己补一个 `package.ps1` 等于凭空维护上游私有脚本，风险大于收益（打包走 `publish.ps1` + `ISCC` 手工流程即可，见 README）。

---

## 判定规则（同步脚本用的）

- `BASELINE.json` 的 `projects.<工程>.knownFailures` 列出基线内失败用例的完整名。
- 同步后：**实际失败集合 ⊆ 基线集合** → 通过（视为无回归）。
- 出现基线外新失败 → 脚本退出码 `3`，必须处理：要么是冲突解法错，要么上游改了行为而测试断言过期。
- 上游修好了基线失败时，顺手把该用例从 `BASELINE.json` 里删掉，并在同步报告里说明。

## 重新采集基线

```powershell
# 跑三个工程并导出 trx
dotnet test tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj -c Release --logger "trx;LogFileName=Core.trx" --results-directory artifacts/testresults
dotnet test tests/GlassDesk.AppServices.Tests/GlassDesk.AppServices.Tests.csproj -c Release --logger "trx;LogFileName=AppServices.trx" --results-directory artifacts/testresults
dotnet test tests/GlassDesk.Dock.Tests/GlassDesk.Dock.Tests.csproj -c Release --logger "trx;LogFileName=Dock.trx" --results-directory artifacts/testresults
```

然后按上面的字段重写 `BASELINE.json`（或让 agent 用 TRX 重新生成）。

## 不要做的事

- 不要去修那 46 个失败（缺上游私有脚本，修了也是假绿）。
- 不要为了全绿而 `Skip` 掉测试。
- 不要把 junction 换成源码 patch。
