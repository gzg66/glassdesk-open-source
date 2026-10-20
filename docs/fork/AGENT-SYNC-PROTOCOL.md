# Agent 上游同步协议

本文件定义 coding agent 在上游发新版时**可以自主做到哪一步、必须什么时候停下来问人**。
机械动作一律走 `.fork/bin/`（`fork-check-upstream.sh` 检查、`fork-sync.sh` 重放），
命令在 WSL / Git Bash 里执行。**不再使用 `scripts/sync-upstream.ps1` / `check-upstream.ps1`**（merge 模型，已 legacy）。

模型只有一条：`upstream-mirror` 是只读镜像，`main` 是 patch 层，同步 = `git rebase --onto upstream/main <旧基线> main`。

---

## 0. 触发条件

满足任一即进入本协议：

1. 用户说"同步上游 / 上游更新了"；
2. `bash .fork/bin/fork-check-upstream.sh` 退出码 `10`；
3. 会话开始时例行巡检（systemd timer 每天 08:17 已自动跑，日志 `.fork/state/systemd.log`）。

---

## 1. 决策速查

| 情形 | agent 行为 |
|---|---|
| 重放无冲突 + 无新失败 | 自动完成：rebase 收尾、更新游标、写 `SYNC-LOG.md`、报告；**推送仍要问人** |
| 冲突只出现在 `settings` / 新增文件 / `AppServices`（低冲突面） | 自主解决，逐文件编译 |
| 冲突出现在 `AppHost*.cs` / `DockWindow` / `LauncherWindow`（中冲突面） | 自主解决，但报告里必须逐条列出决策理由 |
| 冲突出现在 `GlassDesk.UI` / `LauncherLayout*` / `DockLayoutPolicy`（高冲突面） | **停下来问人**，把 `fork-sync.sh --check` 的冲突面清单与 `git log` 证据给用户 |
| 上游删除了自有功能的挂载点（枚举、装配调用、XAML 容器） | **停下来问人**，给出"重放到哪 / 放弃哪个功能"的选项 |
| 出现基线外新失败 | 先判断：冲突解法错 → 自己修；上游行为变了 → 修改测试断言并在报告说明；判断不了 → 问人 |
| 已解 3 轮仍有冲突/失败 | **停下来问人**，附完整报告与已尝试的解法 |
| 任何需要改 `UpdateClient` / `UpdateInstaller` / `UpdateRelease` 内部逻辑的情况 | **停下来问人** |
| rebase 后 patch 数变少（`range-diff` 有提交消失） | 逐条核对：上游已实现 → `git rebase --skip` 并从台账删；不是 → **问人**，不要默认跳过 |

---

## 2. 执行流程

### 步骤 1：检查

```bash
bash .fork/bin/fork-check-upstream.sh
```

- 退出 `0` → 已最新，结束（告诉用户）。
- 退出 `10` → 有新快照，继续。
- 退出 `2` → remote/网络问题，报告用户，不要自行改 remote。

### 步骤 2：预演（冲突面分级）

```bash
bash .fork/bin/fork-sync.sh --check
```

输出里三件事必须转述给用户：上游新提交数、patch 层提交数、**重叠文件数与清单**（>5 个 = 高风险，先 `git branch backup/$(date +%F)`）。
再对照 `AGENTS.md` §5 的冲突面分级表判断能不能自主干。

### 步骤 3：重放

```bash
bash .fork/bin/fork-sync.sh --apply
```

脚本内部顺序：`fetch` → `git rebase --onto upstream/main <旧基线> main`（冲突则停住等人工）→
`range-diff` 校验 patch 是否都还在 → 跑 `TEST_CMD`（`bash scripts/fork-verify.sh`）→ 追加 `docs/fork/SYNC-LOG.md`。

按退出码分支：

| 退出码 | 含义 | 下一步 |
|---|---|---|
| `0` | 重放 + 校验 + 测试通过 | 步骤 6 |
| `2` | 环境问题（工作区脏 / 找不到 `upstream/main` / 镜像被污染） | 修环境；不要 hack 脚本绕过 |
| `20` | rebase 停在冲突点 | 步骤 4 |
| `1` | 重放成功但 `TEST_CMD` 失败 | 步骤 5 |

### 步骤 4：解冲突（逐文件循环）

```bash
git status                     # 冲突清单
git diff --diff-filter=U        # 冲突内容（rerere 已开，重复冲突会自动复用）
```

对每个冲突文件，按这个顺序：

1. **先读懂上游**：`git log -1 --format=%B <上游commit>` 与 `git diff <旧基线>..upstream/main -- <文件>`。
2. **判断意图**：上游是在修 bug、改结构，还是加功能？
   - 修 bug 且与自己改动重叠 → 接受上游实现，把自己的意图重放到新结构上；
   - 改结构 → 按新结构重写自己的增量，**不要**保留旧结构；
   - 加功能且与自己的功能同区域 → 两者都保留，注意命名与可见性开关不要串。
3. **改完立刻编译**：`bash scripts/fork-verify.sh`（一个文件一次，不要攒）。
4. 该文件解完 `git add <file> && git rebase --continue`；解不下去 `bash .fork/bin/fork-sync.sh --abort` 回到同步前。

**冲突解法硬约束**

- **提交身份**：只能用仓库级配置的 `gzg66 <64040020+gzg66@users.noreply.github.com>`。每次 rebase 结束后 `git log -1 --format='%an <%ae>'` 自检；出现真实姓名就停下问人，不要静默强推。
- 禁止 `git checkout --ours/--theirs -- <文件>` 整文件覆盖（除了该文件自己完全没改过的情况）。
- 禁止删除或跳过测试来"变绿"。
- 禁止为了编译通过而删掉上游新增的校验/安全检查。
- 禁止为了省事把自己的功能整体注释掉；要么重放，要么标记 TODO 并在报告里点名。
- 不许改上游标识符的命名来"对齐自己的习惯"。
- 同一处冲突解第二次，写进 `docs/fork/CONFLICTS.md`（下次照抄）。

### 步骤 5：处理验证失败

```bash
bash scripts/fork-verify.sh          # 构建，最快的信号
bash scripts/fork-verify.sh --full   # 构建 + Core/Dock 基线测试
```

分类：

- **编译错误** → 自己的冲突解法有语法/类型问题，修。
- **新失败用例**（对照 `docs/fork/BASELINE.md`）：
  - 断言的是自有行为 → 自己的功能被上游覆盖，重放功能；
  - 断言的是上游行为 → 上游改行为但没改测试（压缩快照常见），更新断言并在报告说明；
  - 断言的是打包/安装流程 → 极可能是公开快照缺文件（`build/package.ps1`），属基线，忽略。
- 修完重跑；最多 3 轮，超了停下来问人。

### 步骤 6：收尾（agent 可自主执行，推送除外）

1. 手验清单（**无法自动化的部分，必须让用户做**）：
   - Dock 玻璃背景在窗口最大化/还原后是否刷新；
   - 启动台分页、图标拖拽、边缘翻页；
   - 设置项开关即时生效、重启后保留；
   - 托盘菜单、开机自启、单实例。
   → 把这份清单作为"待用户确认"列进交付说明。
2. 更新游标并提交**一个新 patch**（走编号，不要临时 `chore:`）：
   ```bash
   bash .fork/bin/fork-new-patch.sh sync-v<tag>     # 自动取号 + 铺明细
   printf '%s\n' 'v<tag>' > docs/fork/LAST-SYNCED
   git add docs/fork/LAST-SYNCED docs/fork/patches docs/fork/SYNC-LOG.md
   git commit -m "patch(NNN): chore(fork): 同步游标更新到 v<tag>"
   ```
3. 体检：`bash .fork/bin/fork-doctor.sh` —— 有 FAIL 不许推。
4. 推送：**默认不推**。列出待推送 commit（sha + 标题 + 冲突面），说明是否 force，征得用户同意后再
   `git push origin upstream-mirror main`；一次同意只对当次有效。
5. 若 `BASELINE.json` 里的失败集合因上游修复而变化，同步更新它并在报告里说明。

---

## 3. 交付说明模板

```markdown
## 上游同步 <tag>

- 区间：<基线>..<tag>（N 个文件变更，+A/-B 行）
- 重放：patch 层 X 个提交，range-diff 比对通过（无丢失）
- 冲突：N 处（文件清单）
  - `路径` — 上游做了什么 / 我怎么解 / 为什么
- 提交身份：`git log -1 --format='%an <%ae>'` 输出（必须为 noreply 别名）
- 验证：fork-verify 构建 0 error；Core 181/181；Dock 481/481；AppServices 378（46 基线失败）
- 基线外新失败：无 / 已修（说明）
- 自有功能：全部保留 / 受影响项（说明）
- 待用户手验：Dock 玻璃重建、启动台分页、设置项、托盘/自启
- 风险：<高冲突面模块> 下次同步预计仍需人工
```

---

## 4. 自动化边界

**可以自动化**：检查、冲突面分级、rebase 重放、低/中冲突面解冲突、构建、测试、写台账、本地提交、体检。

**需要用户逐次批准**：任何 `git push`（含 `--force-with-lease`）、建 PR、建 release、改远端分支、改写已推送历史。

**不可自动化**（必须人）：高冲突面模块的语义决策、上游删挂载点时的取舍、手验 GUI 行为、任何涉及更新器/安装器内部逻辑的改动。

节奏：上游正式版发在 `everlosttime/glassdesk-updates`，源码快照后发。systemd timer 每天检查；
看到新 release 后一周内跑一次 `fork-sync.sh --check`。
