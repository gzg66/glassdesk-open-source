#!/usr/bin/env bash
# fork-init-local.sh —— 补装/重装 fork-thin-patch 脚手架（本仓库参数固定版）。
#
# 镜像分支叫 upstream-mirror、产品主线叫 main，必须显式传给 fork-init.sh：
# 不给 --mirror 时它会默认镜像名 = 上游分支名（main），与 --integration 撞车直接报错。
#
#   bash scripts/fork-init-local.sh [fork-init 的其它参数，例如 --no-commit 或 --auto-fetch systemd]
set -uo pipefail

ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || { echo "不在 git 仓库内" >&2; exit 2; }
cd "$ROOT" || exit 2

SKILL_INIT="${FORK_SKILL_INIT:-$HOME/.pi/agent/skills/fork-thin-patch/scripts/fork-init.sh}"
[ -f "$SKILL_INIT" ] || SKILL_INIT="/home/pg/.pi/agent/skills/fork-thin-patch/scripts/fork-init.sh"
[ -f "$SKILL_INIT" ] || { echo "找不到 fork-init.sh，用 FORK_SKILL_INIT=<路径> 指定" >&2; exit 2; }

UPSTREAM_URL="$(git remote get-url upstream | sed 's|^DISABLED$|https://github.com/everlosttime/glassdesk-open-source.git|')"
FORK_URL="$(git remote get-url origin)"

echo "== 跑 skill init（mirror=upstream-mirror / integration=main，总是不自动提交）"
# --branch main: 上游分支叫 main；--mirror upstream-mirror: 本地只读镜像；--integration main: 产品主线
# --no-commit: fork-init 看到“最后一次提交不是 patch(000)”会自己新建一个 patch(000) 提交，
#             第二次重装时就在历史里叠出两个 000。提交这一动作必须留给人/agent 显式做。
EXTRA=("$@")
case " $* " in *" --no-commit "*) ;; *) EXTRA+=("--no-commit") ;; esac
bash "$SKILL_INIT" "$UPSTREAM_URL" "$ROOT" \
  --fork-url "$FORK_URL" \
  --branch main \
  --mirror upstream-mirror \
  --integration main \
  --auto-fetch none \
  "${EXTRA[@]}" || { echo "fork-init.sh 失败" >&2; exit 1; }

echo "== 校正/幂等（防 skill 默认值回退）"
if grep -q '^MIRROR_BRANCH=' .fork/config; then
  sed -i 's|^MIRROR_BRANCH=.*|MIRROR_BRANCH=upstream-mirror|' .fork/config
else
  printf 'MIRROR_BRANCH=upstream-mirror\n' >> .fork/config
fi
grep -q '^INTEGRATION_BRANCH=' .fork/config \
  || printf 'INTEGRATION_BRANCH=main\n' >> .fork/config
git config fork.mirrorBranch upstream-mirror
git config fork.integrationBranch main
git config fork.upstreamRef upstream/main

# TEST_CMD 会被 fork-init 覆写成空，补回来
if ! grep -q '^TEST_CMD=.bash scripts/fork-verify.sh.$' .fork/config; then
  sed -i "s|^TEST_CMD=.*|TEST_CMD='bash scripts/fork-verify.sh'|" .fork/config
fi

# TEST_CMD 会被 fork-init 覆写成空，补回来
if ! grep -q '^TEST_CMD=.bash scripts/fork-verify.sh.$' .fork/config; then
  sed -i "s|^TEST_CMD=.*|TEST_CMD='bash scripts/fork-verify.sh'|" .fork/config
fi

# /mnt/d 是 DrvFs（core.filemode=false），新拷进来的脚本默认 100644；
# 仓库里记成 100755，换到真 Linux 克隆才不会 Permission denied。
git add -A .fork/bin .fork/hooks >/dev/null 2>&1 || true
for f in .fork/bin/*.sh .fork/hooks/*; do
  git update-index --chmod=+x "$f" 2>/dev/null || true
done

echo "== 体检"
bash .fork/bin/fork-doctor.sh
