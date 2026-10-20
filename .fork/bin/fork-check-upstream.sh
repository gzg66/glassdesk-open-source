#!/usr/bin/env bash
# fork-check-upstream.sh —— 便宜的上游巡检。给 cron / systemd timer / pre-push 用。
#
#   bash fork-check-upstream.sh              # 节流 fetch（默认 6h 内不重复联网）
#   bash fork-check-upstream.sh --force      # 强制 fetch
#   bash fork-check-upstream.sh --quiet      # 静默，只有更新时输出一行
#   bash fork-check-upstream.sh --json       # 机器可读
#
# 退出码: 0 = 无更新, 10 = 有更新, 2 = 出错（网络/配置）
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/lib.sh"

FORCE=0; QUIET=0; JSON=0
for a in "$@"; do case "$a" in
  --force) FORCE=1 ;; --quiet|-q) QUIET=1 ;; --json) JSON=1 ;;
  -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
  *) die "未知参数: $a" ;;
esac; done

fork_load
UP="$UPSTREAM_REMOTE/$UPSTREAM_BRANCH"

# 节流
LAST="$FORK_DIR/state/last-fetch"
NOW="$(date +%s)"
if [ "$FORCE" = "0" ] && [ -f "$LAST" ]; then
  THEN="$(cat "$LAST" 2>/dev/null || echo 0)"
  if [ $((NOW - THEN)) -lt $((FETCH_MAX_AGE_HOURS * 3600)) ]; then
    # 诊断信息走 stderr：--json 时 stdout 必须只有 JSON
    [ "$QUIET" = "0" ] && printf '%s跳过 fetch（%s 分钟前拉过，阈值 %sh）%s\n' "$C_DIM" "$(( (NOW-THEN)/60 ))" "$FETCH_MAX_AGE_HOURS" "$C_RST" >&2
  else FORCE=1; fi
else FORCE=1; fi

if [ "$FORCE" = "1" ]; then
  if ! timeout 60 git fetch --quiet --tags --prune "$UPSTREAM_REMOTE" 2>/dev/null; then
    err "fetch $UPSTREAM_REMOTE 失败（离线/凭证）"; exit 2
  fi
  printf '%s\n' "$NOW" > "$LAST" 2>/dev/null || true
fi

git rev-parse --verify -q "$UP" >/dev/null || { err "没有 $UP"; exit 2; }
INCOMING="$(git rev-list --count "$MIRROR_BRANCH..$UP" 2>/dev/null || echo 0)"
BASE="$(git merge-base "$UP" "$INTEGRATION_BRANCH" 2>/dev/null || true)"
OUR_FILES="$(git diff --name-only "$BASE".."$INTEGRATION_BRANCH" 2>/dev/null || true)"
UP_FILES="$(git diff --name-only "$BASE".."$UP" 2>/dev/null || true)"
OVERLAP="$(comm -12 <(printf '%s\n' "$OUR_FILES" | sed '/^$/d' | sort -u) \
                    <(printf '%s\n' "$UP_FILES" | sed '/^$/d' | sort -u) || true)"
N_OVERLAP="$(printf '%s\n' "$OVERLAP" | sed '/^$/d' | wc -l | tr -d ' ')"
HEAD_SHORT="$(git rev-parse --short "$UP")"

# 状态快照
mkdir -p "$FORK_DIR/state"
printf '{"checked_at":"%s","upstream":"%s","incoming":%s,"overlap_files":%s}\n' \
  "$(stamp)" "$HEAD_SHORT" "$INCOMING" "$N_OVERLAP" > "$FORK_DIR/state/upstream.json" 2>/dev/null || true

if [ "$JSON" = "1" ]; then
  printf '{"upstream_head":"%s","incoming":%s,"overlap_files":%s,"overlap":[%s]}\n' \
    "$HEAD_SHORT" "$INCOMING" "$N_OVERLAP" \
    "$(printf '%s' "$OVERLAP" | sed '/^$/d' | sed 's/.*/"&"/' | paste -sd, -)"
  [ "$INCOMING" -gt 0 ] && exit 10
  exit 0
fi

if [ "$INCOMING" -eq 0 ]; then
  [ "$QUIET" = "0" ] && ok "上游无更新（$UP @ $HEAD_SHORT）"
  exit 0
fi

if [ "$QUIET" = "1" ]; then
  printf '上游 +%s 提交（冲突面 %s 文件）→ %s\n' "$INCOMING" "$N_OVERLAP" "$(git rev-parse --short "$MIRROR_BRANCH")..$HEAD_SHORT"
  exit 10
fi

hdr "上游有更新"
dim "$UP @ $HEAD_SHORT，落后 $INCOMING 个提交"
git --no-pager log --oneline --no-decorate "$MIRROR_BRANCH..$UP" | head -20
[ "$INCOMING" -gt 20 ] && dim "… 共 $INCOMING"
if [ "$N_OVERLAP" -gt 0 ]; then
  warn "冲突面 $N_OVERLAP 个文件与我们的 patch 重叠:"
  printf '%s\n' "$OVERLAP" | sed 's/^/    /'
else
  ok "冲突面：无重叠"
fi
dim ""
dim "下一步: bash .fork/bin/fork-sync.sh --apply"
exit 10
