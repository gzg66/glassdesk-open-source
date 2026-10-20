# 二开说明（fork 分支）

> 上游：[`everlosttime/glassdesk-open-source`](https://github.com/everlosttime/glassdesk-open-source)
> 上游分支：`main`（压缩快照） ｜ 镜像分支：`upstream-mirror` ｜ 产品主线：`main`
> 脚手架初始化：2026-10-20（fork-thin-patch）

## 一句话规则

**上游代码不改结构，我们的改动一律是薄薄一层 patch，叠在上游之上，随时能整体重放。**

判据只有一条：`git rebase --onto upstream/main <旧基线> main` 之后，patch 还在、语义没变。

## 文档地图

| 文件 | 作用 | 状态 |
|---|---|---|
| `README.md`（本文） | 唯一权威：分支、命令、红线 | ✅ 以此为准 |
| `PATCHES.md` | patch 台账（生成物，勿手改） | 由 `fork-status.sh --ledger` 重建 |
| `patches/NNN-*.md` | 每个 patch 的明细：目的 / 上游态度 / 边界 | 新增 patch 必写 |
| `SYNC-LOG.md` | 每次同步记录 | 同步后追加 |
| `CONFLICTS.md` | 永久冲突决策（同一处反复撞车的解法） | 撞第二次就写 |
| `BASELINE.md` / `BASELINE.json` | 测试基线（哪些失败是上游自带的） | 判定"没引入新失败"的依据 |
| `FORK-GUIDE.md` | 二开决策长文（品牌隔离、为什么 fork、功能往哪写） | 参考用；第 2/7 节的 **merge 升级方案已废弃** |
| `AGENT-SYNC-PROTOCOL.md` | agent 解冲突的操作细则 | 参考用；其中 `git merge --no-ff sync/<tag>` 已废弃，改用本 README 的 rebase 流程 |

## 分支布局

| 分支 | 含义 | 允许的操作 |
|---|---|---|
| `upstream-mirror` | 上游镜像，**永远等于** `upstream/main` | 只允许 fast-forward。**禁止提交**（pre-commit shim 会拒，`fork-doctor` 硬失败） |
| `main` | 我们的 patch 层：`{上游基线} + N 个 patch 提交` | 所有二开工作都在这里，只 rebase 不 merge |
| `patch/NNN-xxx` | 单 patch 独立分支（要评审 / 准备推上游时用） | 从 `main` 切出，合回即删 |

远程：`upstream` = 原项目（fetch 只读，push 已设为 `DISABLED`）｜ `origin` = `gzg66/glassdesk-open-source`（我们的 fork）。

> 布局与 skill 默认值不同：skill 默认镜像分支叫 `main`、集成分支叫 `dev`；本仓库 `main` 已经是产品主线，
> 所以镜像叫 `upstream-mirror`。**重跑 skill 的 `fork-init.sh` 会把 `.fork/config` 写坏**
> （`MIRROR_BRANCH=main` → doctor 会把产品主线判成"镜像被污染"）。
> 补装/重装一律用 `bash scripts/fork-init-local.sh`。

## 一个 patch 长什么样

```
patch(007): fix(conf): 关掉 up.grade.retry 的死循环

目的: 上游重试策略在我们的单机部署下会无限重试
上游: 不适用（私有部署假设），已报 issue #1234
边界: 仅改 config/upstream.yaml 的 3 行 + 新增 ext/retry_guard.py
```

- 编号在 `docs/fork/patches/NNN-<slug>.md`，索引 `PATCHES.md` 是生成物
- 开新 patch：`bash .fork/bin/fork-new-patch.sh <slug>`（自动取下一个号 + 铺明细模板）
- 提交信息必须是 `patch(NNN): type(scope): 说明`，`commit-msg` hook 会拒其它格式
- 单 patch 建议 ≤8 个文件（超了是软提醒，`FORK_STRICT=1` 时硬拒）

## 日常命令

```bash
bash .fork/bin/fork-check-upstream.sh    # 上游有没有更新（有更新 exit 10）
bash .fork/bin/fork-sync.sh --check      # 更新详情 + 冲突面分级（>5 重叠文件 = 高风险）
bash .fork/bin/fork-sync.sh --apply      # rebase 重放 + range-diff 校验 + 跑 TEST_CMD + 记 SYNC-LOG
bash .fork/bin/fork-sync.sh --abort      # 同步搞砸了回滚
bash .fork/bin/fork-status.sh            # patch 清单、龄期、台账缺失
bash .fork/bin/fork-status.sh --ledger   # 重建 docs/fork/PATCHES.md
bash .fork/bin/fork-doctor.sh            # 薄层体检（有 FAIL 就别推）
bash scripts/fork-init-local.sh          # 补装/重装脚手架（保留本仓库的分支命名）
```

## 同步上游（每次上游发快照后）

1. `fork-check-upstream.sh` → 有更新才继续
2. `fork-sync.sh --check` 看冲突面
3. 高风险（>5 重叠文件）先 `git branch backup/$(date +%F)`
4. `fork-sync.sh --apply` —— 内部顺序：rebase → range-diff → 跑 `TEST_CMD` → 写 `SYNC-LOG.md`
5. 冲突解法记进 `CONFLICTS.md`；`fork-doctor.sh` 绿了才允许推
6. 推送**逐次征求同意**（见 `AGENTS.md` §0）

## 本仓库专属：验证命令

构建只能跑在 Windows 上（`.NET 10 + WPF`），WSL 里没有 SDK。`TEST_CMD` 指向
`bash scripts/fork-verify.sh`，它转调 Windows 侧 dotnet 构建 `src/GlassDesk.App`：

```bash
bash scripts/fork-verify.sh          # = TEST_CMD，Release 构建 App 工程
bash scripts/fork-verify.sh --kill   # 先杀掉占用输出目录的 GlassDesk（MSB3021 时用）
bash scripts/fork-verify.sh --full   # 构建 + 三个基线测试工程（慢）
```

## 硬红线

- ❌ 在 `upstream-mirror` 上提交
- ❌ `git merge upstream/main` 进 `main`（必须 `rebase --onto`）
- ❌ 一个 patch 里混入格式化/重排/重命名
- ❌ 改了源码但没写/没更新 `docs/fork/patches/*.md`
- ❌ 直接改第三方 vendored 目录而不在台账里登记
- ❌ 自动化里 `git push`（推送必须人工批准）

## 当前债务（2026-10-20 体检）

`fork-doctor` 仍报 1 个硬失败，来源是本次脚手架之前的三个提交，尚未改写：

| sha | 现状 | 计划 |
|---|---|---|
| `0bee658` | `docs(fork): …`，10 文件 | reword → `patch(001)`，台账补 `001-fork-scaffold.md` |
| `8122254` | 27 文件，混了 5 个功能 | reword → `patch(002)`；**建议拆成 4–5 个 patch**（高冲突面文件：`AppHost.cs`、`LauncherWindow.xaml`、`DockWindow.xaml.cs`） |
| `303eb54` | 10 文件，3 个修复 + 文档 | reword → `patch(003)` |

reword/split 都要改写已推送到 `origin/main` 的历史，需先经同意再 force-push。
