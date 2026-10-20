# patch 009: fork-tooling-update（.fork/bin 跟随 skill 修复 + systemd 巡检）

- 开单: 2026-10-20
- 状态: 已合入
- 能不能不改源码做到？ 是 —— 只动 fork 工具与配置，不碰产品代码
- 为什么上游不会接受 / 该不该推上游: 纯 fork 私有工作流，不推
- 触及的上游文件: 无
- 新增文件: 无（`.fork/bin` 是仓库自带副本，从 skill 同步过来）
- 上游同类实现/已有 PR: 无
- 回滚方式: 可单独 revert（只影响本地工具；revert 后 `--mirror` 参数与编号检测失效）
- 验证:
  - [x] skill 端端到端自测：`bash ~/.pi/agent/skills/fork-thin-patch/scripts/selftest.sh` → **61 通过 / 0 失败**
  - [x] `bash scripts/fork-init-local.sh` 重跑幂等：`.fork/config` 无 diff，doctor 0 硬失败
  - [x] `systemctl --user list-timers` → `fork-upstream-2459945330.timer` 已排程（每天 08:17）

## 目的

三处真实的工具缺口，都是本仓库首次 init 时踩出来的：

1. **skill 的 `fork-init.sh` 不能给镜像分支单独命名** → `main` 已是产品主线时，重装会把 `MIRROR_BRANCH` 写成 `main`，
   `fork-doctor` 立刻把产品主线判成「镜像被污染」。已给 skill 加 `--mirror` 参数。
2. **`fork-status.sh` 把无编号提交显示成 `000---` 并标成 `(脚手架)`**，不计入缺文档统计，于是 status 报「问题 ✓ 无」，
   而 doctor 同时报硬失败。已改为 `---` / `(编号缺失)` 并计入统计。
3. **`.NET/Rust/Go 仓库的 `bin/` 忽略规则会连带忽略 `.fork/bin/`**，导致「工具随仓库走」变成假话。
   `fork-init.sh` 现在会追加 `!.fork/bin/` 与 `!.fork/bin/**`。

另外：`fork-init.sh` 在**重跑**时会因为「最后一次提交不是 patch(000)」而自己新建一个 `patch(000)` 提交
（本仓库就因此多出一个重复的 000，已 `git reset --soft` 撤销并并入本 patch）。`scripts/fork-init-local.sh`
现在强制补 `--no-commit`，提交动作留给人和 agent 显式执行。

## 改动要点

| 文件 | 改动 |
|---|---|
| `.fork/bin/fork-status.sh` | 编号缺失单独计数并告警；`printf '%0*d'` 前导零修正（不再出现 `000---`） |
| `.fork/config` | `TEST_CMD='bash scripts/fork-verify.sh'` 在重装后不再被清空 |
| `scripts/fork-init-local.sh` | 显式传 `--mirror upstream-mirror`；强制 `--no-commit`；装 systemd timer 走 `--auto-fetch systemd` |
| 系统单元 | `~/.config/systemd/user/fork-upstream-2459945330.{service,timer}`，每天 08:17 跑 `fork-check-upstream.sh --quiet`，日志 `.fork/state/systemd.log` |

skill 侧改动（不在本仓库，记录在此便于回溯）：
`fork-init.sh` 新增 `--mirror` / 镜像名与集成名撞车即拒 / `.gitignore` 取回 `.fork/bin` / 提交时补 100755；
`fork-status.sh` 见上；`selftest.sh` 新增场景 0e（8 条断言）；`SKILL.md` 命令表补 `--mirror`。

## 风险 / 以后要盯的地方

- `.fork/bin` 是 skill 的副本，两边会漂移：**改 skill 后必须重跑 `scripts/fork-init-local.sh` 并提交本仓库差异**
- systemd timer 里的 `GIT_PROXY` 是 init 时从环境抓的 `http://192.168.0.1:7892`；换网络环境要同步改 `.fork/config` 与单元文件
- WSL 里的 timer 只在 WSL 实例运行期间触发（`Persistent=true` 会在下次启动时补跑）
