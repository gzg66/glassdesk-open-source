# patch 001: fork-workflow-scaffold（二开工作流与脚本）

- 开单: 2026-10-20（编号补登，提交原为 `docs(fork): …`）
- 状态: 已合入
- 能不能不改源码做到？ 不适用 —— 本 patch 不改产品代码，只加文档与发布/巡检脚本
- 为什么上游不会接受 / 该不该推上游: 上游只发压缩快照、无 CI 约定，这些是 fork 私有工作流，不推
- 触及的上游文件: 无（10 个文件全为新增）
- 新增文件:
  - `AGENTS.md` / `CLAUDE.md`（仓库契约，给所有 agent 的常驻规则）
  - `docs/fork/{FORK-GUIDE,AGENT-SYNC-PROTOCOL}.md`、`BASELINE.md/.json`、`LAST-SYNCED`
  - `scripts/{check-upstream,sync-upstream,publish-portable}.ps1`
- 上游同类实现/已有 PR: 无
- 回滚方式: 可单独 revert（纯新增，不影响构建）
- 验证:
  - [x] `publish-portable.ps1` 产出可运行便携版（285 文件 / 195.9MB，不含 `Data/`）
  - [x] `scripts/fork-verify.sh` 构建 0 警告 0 错误

## 目的

让「上游压缩快照 + 我们长期二开」这件事有可执行的纪律：谁能推、身份怎么写、基线哪些失败是上游自带的、发布怎么打包。

## 改动要点

- 分支模型：`upstream-mirror` 纯镜像、`main` 产品主线
- 基线量化：Core 181、Dock 477→481、AppServices 378 里 46 个上游自带失败
- 发布脚本把运行时 `Data/` 从输出目录备份后恢复，避免每次发布清空用户设置

## 风险 / 以后要盯的地方

- 同 patch 内的 `sync-upstream.ps1`（merge 模型）已被 `.fork/bin/fork-sync.sh`（rebase 重放）取代，见 patch 004
- `docs/fork/FORK-GUIDE.md` 第 2/7 节的 merge 论证已废弃，只保留“为什么 fork / 功能往哪写”的部分
