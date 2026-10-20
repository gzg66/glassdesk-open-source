#!/usr/bin/env bash
# fork-thin-patch 共享库。被 .fork/bin/*.sh 与 hooks 引用。
# 约定: 被 source，不单独执行。

set -uo pipefail

# ---------- 输出 ----------
if [ -t 1 ] && [ "${NO_COLOR:-}" = "" ]; then
  C_OK=$'\033[32m'; C_WARN=$'\033[33m'; C_ERR=$'\033[31m'
  C_DIM=$'\033[2m'; C_B=$'\033[1m'; C_RST=$'\033[0m'
else
  C_OK=''; C_WARN=''; C_ERR=''; C_DIM=''; C_B=''; C_RST=''
fi
ok()   { printf '%s✓%s %s\n' "$C_OK" "$C_RST" "$*"; }
warn() { printf '%s!%s %s\n' "$C_WARN" "$C_RST" "$*"; }
err()  { printf '%s✗%s %s\n' "$C_ERR" "$C_RST" "$*" >&2; }
dim()  { printf '%s%s%s\n' "$C_DIM" "$*" "$C_RST"; }
hdr()  { printf '\n%s%s%s\n' "$C_B" "$*" "$C_RST"; }
die()  { err "$*"; exit "${2:-2}"; }

# ---------- 语境 ----------
fork_load() {
  FORK_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" \
    || die "不在 git 仓库内"
  FORK_DIR="$FORK_ROOT/.fork"
  FORK_CFG="$FORK_DIR/config"
  [ -d "$FORK_ROOT/.git" ] || [ -f "$FORK_ROOT/.git" ] || die "不在 git 工作树内"
  [ -f "$FORK_CFG" ] || die "缺少 $FORK_CFG —— 这个仓库还没初始化二开结构，先跑 bash scripts/fork-init.sh"
  # shellcheck disable=SC1090
  set -a; . "$FORK_CFG"; set +a
  : "${UPSTREAM_REMOTE:=upstream}"
  : "${UPSTREAM_BRANCH:=main}"
  : "${MIRROR_BRANCH:=main}"
  : "${INTEGRATION_BRANCH:=dev}"
  : "${PATCH_PREFIX:=patch}"
  : "${PATCH_ID_WIDTH:=3}"
  : "${MAX_FILES_PER_PATCH:=8}"
  : "${MAX_PATCHES:=40}"
  : "${STALE_DAYS:=180}"
  : "${TEST_CMD:=}"
  : "${UPSTREAM_URL:=}"
  : "${FETCH_MAX_AGE_HOURS:=6}"
  : "${GIT_PROXY:=}"
  # cron/systemd 里没有 shell 的代理变量，配置里写了就用它
  if [ -n "$GIT_PROXY" ]; then
    export https_proxy="$GIT_PROXY" http_proxy="$GIT_PROXY" HTTPS_PROXY="$GIT_PROXY" HTTP_PROXY="$GIT_PROXY"
  fi
  cd "$FORK_ROOT" || die "无法进入仓库根目录"
}

upstream_ref() { printf '%s/%s' "$UPSTREAM_REMOTE" "$UPSTREAM_BRANCH"; }

need_upstream() {
  git rev-parse --verify -q "$(upstream_ref)^{commit}" >/dev/null \
    || die "找不到 $(upstream_ref) —— 先 git fetch $UPSTREAM_REMOTE（或检查 .fork/config）"
}

require_clean() {
  local dirty
  dirty="$(git status --porcelain --untracked-files=normal | grep -v '^?? .fork/state/' || true)"
  [ -z "$dirty" ] || die "工作区不干净，先 stash/commit：
$dirty"
}

on_rebase() { [ -d "$FORK_ROOT/.git/rebase-merge" ] || [ -d "$FORK_ROOT/.git/rebase-apply" ]; }
on_merge()  { [ -f "$FORK_ROOT/.git/MERGE_HEAD" ]; }

# 本地 patch 层: merge-base 之后的 commit（排除 upstream 自身）
local_commits() { git rev-list --reverse "$(upstream_ref)..HEAD" 2>/dev/null; }

# patch 层触碰的全部文件（相对 upstream 基线）
patch_touched_files() {
  local base
  base="$(git merge-base "$(upstream_ref)" HEAD 2>/dev/null)" || return 0
  git diff --name-only "$base"..HEAD 2>/dev/null
}

stamp() { date -u +%Y-%m-%dT%H:%M:%SZ; }
day()   { date +%Y-%m-%d; }

# 每 patch 一个文档
patch_dir() { printf '%s/docs/fork/patches' "$FORK_ROOT"; }
