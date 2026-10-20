#!/usr/bin/env bash
# fork-init-local.sh —— 补装/重装 fork-thin-patch 脚手架，并保留本仓库的分支命名。
#
# 为什么需要它：skill 的 fork-init.sh 假设「镜像分支名 == 上游分支名」，
# 而本仓库镜像叫 upstream-mirror、产品主线叫 main。直接重跑会把 .fork/config
# 写成 MIRROR_BRANCH=main，随后 fork-doctor 会把产品主线判成「镜像被污染」。
#
#   bash scripts/fork-init-local.sh [fork-init 的其它参数，例如 --no-commit]
set -uo pipefail

ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || { echo "不在 git 仓库内" >&2; exit 2; }
cd "$ROOT" || exit 2

SKILL_INIT="${FORK_SKILL_INIT:-$HOME/.pi/agent/skills/fork-thin-patch/scripts/fork-init.sh}"
[ -f "$SKILL_INIT" ] || SKILL_INIT="/home/pg/.pi/agent/skills/fork-thin-patch/scripts/fork-init.sh"
[ -f "$SKILL_INIT" ] || { echo "找不到 fork-init.sh，用 FORK_SKILL_INIT=<路径> 指定" >&2; exit 2; }

UPSTREAM_URL="$(git remote get-url upstream | sed 's|^DISABLED$|https://github.com/everlosttime/glassdesk-open-source.git|')"
FORK_URL="$(git remote get-url origin)"

echo "== 跑 skill init（镜像名先按 main，随后纠正）"
# --branch main: 上游分支叫 main；--integration main: 产品主线也叫 main
bash "$SKILL_INIT" "$UPSTREAM_URL" "$ROOT" \
  --fork-url "$FORK_URL" \
  --branch main \
  --integration main \
  --auto-fetch none \
  "$@" || { echo "fork-init.sh 失败" >&2; exit 1; }

echo "== 纠正本仓库的分支命名"
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
