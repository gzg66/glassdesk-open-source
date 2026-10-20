# patch 008: sync-model-rebase（同步模型切到 rebase 重放）

- 开单: 2026-10-20
- 状态: 已合入
- 能不能不改源码做到？ 是 —— 本 patch 不碰产品代码，只改流程与文档
- 为什么上游不会接受 / 该不该推上游: 纯 fork 私有工作流，不推
- 触及的上游文件: 无（`AGENTS.md` / `docs/fork/*.md` / `scripts/*.ps1` 都是 fork 自己的文件，公开快照里没有）
- 新增文件: `docs/fork/README.md`（权威版）、`patches/001..003` 台账
- 上游同类实现/已有 PR: 无
- 回滚方式: 可单独 revert（只影响流程文档，不影响构建）
- 验证:
  - [x] `bash .fork/bin/fork-doctor.sh` 0 硬失败
  - [x] `bash .fork/bin/fork-sync.sh --check` 上游无新提交、镜像分支 0 额外提交
  - [x] `bash scripts/fork-verify.sh` 构建 0 警告 0 错误

## 目的

把「上游发快照后用 merge 合进来」换成「patch 层 rebase --onto 重放」。
merge 一旦进 `main`，历史里混进上游节点：patch 不能单独 revert、不能用 `range-diff` 验证没丢东西、
不能日后拆开推上游，`fork-doctor` 还会判硬失败。旧的 PowerShell 双脚本（`sync-upstream.ps1` /
`check-upstream.ps1`）正是 merge 模型的实现，一并降级。

## 改动要点

| 文件 | 改动 |
|---|---|
| `AGENTS.md` | §2 硬事实 2 改为「只 rebase 不 merge」；§7 上游同步整段换成 `.fork/bin/*.sh`；§8 禁区同步 |
| `docs/fork/FORK-GUIDE.md` | §2「为什么用 merge」重写为「为什么用 rebase」，含反方论据（N 次冲突 vs 1 次）与 rerere 抵消；§7 升级流程换命令 |
| `docs/fork/AGENT-SYNC-PROTOCOL.md` | 全文重写机械步骤：检查/预演/重放/解冲突/验证/收尾全部走 `.fork/bin`；新增「patch 数变少怎么办」决策行 |
| `docs/fork/README.md` | 重写为唯一权威：分支布局（`upstream-mirror`/`main`）、命令、验证命令、红线、债务表 |
| `scripts/sync-upstream.ps1` `check-upstream.ps1` | 顶部加 DEPRECATED 横幅，指向 `.fork/bin` 替代品；文件保留作历史参考 |
| `docs/fork/PATCHES.md` | 台账重建（本 patch 顺带刷新） |

脚手架类文件（`.fork/*`、`scripts/fork-init-local.sh`、`scripts/fork-verify.*`、`.gitignore`）在 `patch(000)` 里，
不在本 patch 范围。

## 风险 / 以后要盯的地方

- rebase 每次同步是「逐个 patch 撞冲突」；`rerere` 已开 + patch 应保持小，否则退化成硬分叉
- `fork-init-local.sh` 依赖 skill 路径（`FORK_SKILL_INIT` 可覆盖）；skill 目录不可写时它仍然可用
- `.fork/bin` 是仓库自带副本：同步脚本升级时要手动比对 skill 版本，别让它和 skill 漂移
