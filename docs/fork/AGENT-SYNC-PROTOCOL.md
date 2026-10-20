# Agent 上游同步协议

本文件定义 coding agent 在上游发新版时**可以自主做到哪一步、必须什么时候停下来问人**。
配合 `scripts/sync-upstream.ps1`（机械动作）与 `scripts/check-upstream.ps1`（检查）使用。

---

## 0. 触发条件

满足任一即进入本协议：

1. 用户说"同步上游 / 合并上游 / 上游更新了"；
2. `powershell -NoProfile -File scripts/check-upstream.ps1` 退出码 `10`；
3. 会话开始时例行巡检（可选，建议每天一次）。

---

## 1. 决策速查

| 情形 | agent 行为 |
|---|---|
| 合并无冲突 + 无新失败 | 自动完成：合并回 `main`、更新游标、推送、写报告 |
| 冲突只出现在 `settings` / 新增文件 / `AppServices`（低冲突面） | 自主解决，逐文件编译 |
| 冲突出现在 `AppHost*.cs` / `DockWindow` / `LauncherWindow`（中冲突面） | 自主解决，但报告里必须逐条列出决策理由 |
| 冲突出现在 `GlassDesk.UI` / `LauncherLayout*` / `DockLayoutPolicy`（高冲突面） | **停下来问人**，把 `artifacts/fork-sync/<时间戳>/REPORT.md` 摘要给用户 |
| 上游删除了自有功能的挂载点（枚举、装配调用、XAML 容器） | **停下来问人**，给出"重放到哪 / 放弃哪个功能"的选项 |
| 出现基线外新失败 | 先判断：冲突解法错 → 自己修；上游行为变了 → 修改测试断言并在报告说明；判断不了 → 问人 |
| 已解 3 轮仍有冲突/失败 | **停下来问人**，附完整报告与已尝试的解法 |
| 任何需要改 `UpdateClient` / `UpdateInstaller` / `UpdateRelease` 内部逻辑的情况 | **停下来问人** |

---

## 2. 执行流程

### 步骤 1：检查

```powershell
powershell -NoProfile -File scripts/check-upstream.ps1
```

- 退出 `0` → 已最新，结束（告诉用户）。
- 退出 `10` → 有新快照，继续。
- 退出 `1` → remote/网络问题，报告用户，不要自行改 remote。

### 步骤 2：预演

```powershell
powershell -NoProfile -File scripts/sync-upstream.ps1 -DryRun
```

把 `diff --stat` 摘要给用户看，并预先判断冲突面等级（对照 `AGENTS.md` §5 的表）。

### 步骤 3：同步

```powershell
powershell -NoProfile -File scripts/sync-upstream.ps1
```

按退出码分支：

| 退出码 | 含义 | 下一步 |
|---|---|---|
| `0` | 合并+构建+测试通过 | 跳到步骤 6 |
| `1` | 前置检查失败（工作区脏 / remote 缺 / 分支已存在） | 修正环境后重跑；不要 hack 脚本绕过 |
| `2` | 有冲突 | 步骤 4 |
| `3` | 合并成功但构建/测试失败 | 步骤 5 |

### 步骤 4：解冲突（逐文件循环）

```powershell
# 看冲突清单与上游证据
Get-Content artifacts/fork-sync/<时间戳>/REPORT.md
git diff --name-only --diff-filter=U
```

对每个冲突文件，按这个顺序：

1. **先读懂上游**：`git diff <基线>..<目标tag> -- <文件>` 与上一轮自己的 `git log` 说明。
2. **判断意图**：上游是在修 bug、改结构，还是加功能？
   - 修 bug 且与自己改动重叠 → 接受上游实现，把自己的意图重放到新结构上；
   - 改结构 → 按新结构重写自己的增量，**不要**保留旧结构；
   - 加功能且与自己的功能同区域 → 两者都保留，注意命名与可见性开关不要串。
3. **改完立刻编译**：
   ```powershell
   dotnet build src/GlassDesk.App/GlassDesk.App.csproj -c Release
   ```
   一个文件一次编译，不要攒。
4. 冲突全部解完：
   ```powershell
   git add -A
   git commit -m "sync: 合并上游 <tag>，解决 <N> 处冲突"
   powershell -NoProfile -File scripts/sync-upstream.ps1 -Resume
   ```

**冲突解法硬约束**

- **提交身份**：只能用仓库级配置的 `gzg66 <64040020+gzg66@users.noreply.github.com>`。每次 commit 前 `git log -1 --format='%an <%ae>'` 自检；出现真实姓名就停下问人，不要静默强推。
- 禁止 `git checkout --ours/--theirs -- <文件>` 整文件覆盖（除了该文件自己完全没改过的情况）。
- 禁止删除或跳过测试来"变绿"。
- 禁止为了编译通过而删掉上游新增的校验/安全检查。
- 禁止为了省事把自己的功能整体注释掉；要么重放，要么标记 TODO 并在报告里点名。
- 不许改上游标识符的命名来"对齐自己的习惯"。

### 步骤 5：处理验证失败

```powershell
Get-Content artifacts/fork-sync/<项目>.log -Tail 60
```

分类：

- **编译错误** → 自己的冲突解法有语法/类型问题，修。
- **新失败用例**（报告里的"新失败用例"列表）：
  - 断言的是自有行为 → 自己的功能被上游覆盖，重放功能；
  - 断言的是上游行为 → 上游改行为但没改测试（压缩快照常见），更新断言并在报告说明；
  - 断言的是打包/安装流程 → 极可能是公开快照缺文件（`build/package.ps1`），属基线，忽略。
- 修完再跑 `-Resume`，最多 3 轮；超了停下来问人。

### 步骤 6：收尾（agent 可自主执行）

1. 手验清单（**无法自动化的部分，必须让用户做**）：
   - Dock 玻璃背景在窗口最大化/还原后是否刷新；
   - 启动台分页、图标拖拽、边缘翻页；
   - 设置项开关即时生效、重启后保留；
   - 托盘菜单、开机自启、单实例。
   → 把这份清单作为"待用户确认"列进交付说明。
2. 合并回主线：
   ```powershell
   git checkout main
   git merge --no-ff sync/<tag>
   ```
3. 更新游标并提交：
   ```powershell
   Set-Content docs/fork/LAST-SYNCED '<tag>' -Encoding UTF8
   git add docs/fork/LAST-SYNCED
   git commit -m "chore: 同步游标更新到 <tag>"
   ```
4. 推送：**默认不推**。列出待推送 commit（sha + 标题 + 冲突面），征得用户同意后再 `git push origin main`；一次同意只对当次有效。
5. 若 `BASELINE.json` 里的失败集合因上游修复而变化，同步更新它并在报告里说明。
6. 删除同步分支：`git branch -d sync/<tag>`

---

## 3. 交付说明模板

```markdown
## 上游同步 <tag>

- 区间：<基线>..<tag>（N 个文件变更，+A/-B 行）
- 冲突：N 处（文件清单）
  - `路径` — 上游做了什么 / 我怎么解 / 为什么
- 提交身份：`git log -1 --format='%an <%ae>'` 输出（必须为 noreply 别名）
- 验证：build 0 error；Core 161/161；AppServices 372（46 基线失败）；Dock 477/477
- 基线外新失败：无 / 已修（说明）
- 自有功能：全部保留 / 受影响项（说明）
- 待用户手验：Dock 玻璃重建、启动台分页、设置项、托盘/自启
- 风险：<高冲突面模块> 下次同步预计仍需人工
```

---

## 4. 自动化边界

**可以自动化**：检查、预演、合并、低/中冲突面解冲突、构建、测试、报告、游标更新、本地提交。

**需要用户逐次批准**：任何 `git push`（含 force）、建 PR、建 release、改远端分支。

**不可自动化**（必须人）：高冲突面模块的语义决策、上游删挂载点时的取舍、手验 GUI 行为、任何涉及更新器/安装器内部逻辑的改动。

建议节奏：上游正式版发在 `everlosttime/glassdesk-updates`，源码快照后发。每次看到 `glassdesk-updates` 有新 release，一周内跑一次 `check-upstream.ps1`。
