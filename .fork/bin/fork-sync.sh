#!/usr/bin/env bash
# fork-sync.sh —— 拉上游更新，把薄 patch 层重放到新上游上。
#
#   bash fork-sync.sh              # = --check，只报告不动手
#   bash fork-sync.sh --check      # 报告 + 冲突面分级；有更新 exit 10
#   bash fork-sync.sh --apply      # 真同步：rebase + range-diff 校验 + 跑测试 + 记台账
#   bash fork-sync.sh --fetch-only # 只 fetch
#   bash fork-sync.sh --abort      # rebase 搞砸了回滚
#   bash fork-sync.sh --apply --no-test
#
# 退出码: 0 = 无更新/成功, 10 = 有上游更新(仅 check), 20 = rebase 冲突待人工, 2 = 环境错误
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/lib.sh"

MODE=check; DO_TEST=1
for a in "$@"; do
  case "$a" in
    --check) MODE=check ;;
    --apply) MODE=apply ;;
    --fetch-only) MODE=fetch ;;
    --abort) MODE=abort ;;
    --no-test) DO_TEST=0 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) die "未知参数: $a" ;;
  esac
done

fork_load
need_upstream
UP="$UPSTREAM_REMOTE/$UPSTREAM_BRANCH"

if [ "$MODE" = abort ]; then
  if on_rebase; then git rebase --abort && ok "已回滚 rebase"; else
    if on_merge; then git merge --abort && ok "已回滚 merge"; else warn "当前没有进行中的 rebase/merge"; fi
  fi
  exit 0
fi

hdr "fetch $UPSTREAM_REMOTE"
if ! git fetch --quiet --tags --prune "$UPSTREAM_REMOTE"; then
  die "fetch 失败。离线/凭证问题？可先 git remote -v 自查"
fi
stamp > "$FORK_DIR/state/last-fetch-ts" 2>/dev/null || true
date +%s > "$FORK_DIR/state/last-fetch" 2>/dev/null || true
[ "$MODE" = fetch ] && { ok "已 fetch"; exit 0; }

# ---------- 现状 ----------
BASE="$(git merge-base "$UP" "$INTEGRATION_BRANCH")"
INCOMING="$(git rev-list --count "$MIRROR_BRANCH..$UP" 2>/dev/null || echo 0)"
AHEAD_UP="$(git rev-list --count "$UP..$MIRROR_BRANCH" 2>/dev/null || echo 0)"
PATCHES="$(git rev-list --count "$BASE..$INTEGRATION_BRANCH" 2>/dev/null || echo 0)"
OUR_FILES="$(git diff --name-only "$BASE".."$INTEGRATION_BRANCH" 2>/dev/null || true)"
UP_FILES="$(git diff --name-only "$BASE".."$UP" 2>/dev/null || true)"
OVERLAP="$(comm -12 <(printf '%s\n' "$OUR_FILES" | sed '/^$/d' | sort -u) \
                    <(printf '%s\n' "$UP_FILES"  | sed '/^$/d' | sort -u) || true)"
N_OVERLAP="$(printf '%s\n' "$OVERLAP" | sed '/^$/d' | wc -l | tr -d ' ')"

hdr "现状"
dim "镜像分支      : $MIRROR_BRANCH  ($UP 之后额外提交 $AHEAD_UP 个)"
dim "集成分支      : $INTEGRATION_BRANCH  (patch 层 $PATCHES 个提交)"
dim "上游新提交    : $INCOMING"
dim "patch 触碰文件: $(printf '%s\n' "$OUR_FILES" | sed '/^$/d' | wc -l | tr -d ' ') 个"

if [ "$AHEAD_UP" -gt 0 ]; then
  warn "镜像分支 $MIRROR_BRANCH 已被污染（$AHEAD_UP 个上游之外的提交）—— 薄层已破"
fi
if [ "$INCOMING" -eq 0 ]; then
  ok "上游没有新提交，收工"
  exit 0
fi

hdr "上游将要合入"
git --no-pager log --oneline --no-decorate "$MIRROR_BRANCH..$UP" | head -40
[ "$INCOMING" -gt 40 ] && dim "… 共 $INCOMING 个，只列前 40"

hdr "冲突面"
if [ "$N_OVERLAP" -eq 0 ]; then
  ok "低风险：上游改动与我们的 patch 无文件重叠，rebase 预期干净"
elif [ "$N_OVERLAP" -le 5 ]; then
  warn "中风险：$N_OVERLAP 个文件双方都动过"
else
  warn "高风险：$N_OVERLAP 个文件双方都动过，先备份再上"
fi
if [ "$N_OVERLAP" -gt 0 ]; then
  printf '%s\n' "$OVERLAP" | sed 's/^/    /'
  printf '\n'
  dim "上游在这些文件上的提交:"
  git --no-pager log --oneline --no-decorate "$MIRROR_BRANCH..$UP" -- $OVERLAP | head -20 | sed 's/^/    /'
fi

if [ "$MODE" = check ]; then
  hdr "动手"
  cat <<EOF
  bash .fork/bin/fork-sync.sh --apply     # 重放 patch 层
  bash .fork/bin/fork-doctor.sh           # 体检
EOF
  exit 10
fi

# ---------- apply ----------
hdr "apply: 重放 patch 层"
require_clean
OLD_TIP="$(git rev-parse "$INTEGRATION_BRANCH")"
OLD_BASE="$BASE"
printf '%s %s %s\n' "$(stamp)" "$OLD_TIP" "$INCOMING" >> "$FORK_DIR/state/sync-history"
[ "$AHEAD_UP" -gt 0 ] && die "镜像分支已污染，先修镜像分支（git switch $MIRROR_BRANCH && git reset --hard $UP）"

git switch --quiet "$INTEGRATION_BRANCH" || die "无法切到 $INTEGRATION_BRANCH"
dim "git rebase --onto $UP $OLD_BASE $INTEGRATION_BRANCH"
if ! git rebase --onto "$UP" "$OLD_BASE" "$INTEGRATION_BRANCH"; then
  printf '\n'
  err "rebase 冲突，已停在冲突点。命令行在上面，按顺序:"
  cat <<EOF
  1. git status / git diff           看冲突
  2. 解决文件（.fork/bin 里的规则见 docs/fork/CONFLICTS.md）
  3. git add <file> && git rebase --continue
     放弃: bash .fork/bin/fork-sync.sh --abort
  4. 全部结束后必须跑: bash .fork/bin/fork-doctor.sh
EOF
  printf '%s rebase-conflict %s\n' "$(stamp)" "$UP" >> "$FORK_DIR/state/sync-history"
  exit 20
fi
ok "rebase 完成"

hdr "校验：range-diff（确认没丢 patch、没丢改动）"
git --no-pager range-diff "$OLD_BASE".."$OLD_TIP" "$UP".."$INTEGRATION_BRANCH" || true
NEW_COUNT="$(git rev-list --count "$UP".."$INTEGRATION_BRANCH")"
if [ "$NEW_COUNT" -ne "$PATCHES" ]; then
  warn "patch 数从 $PATCHES 变成 $NEW_COUNT —— 有 patch 被丢弃/合并，逐个核对上面的 range-diff"
else
  ok "patch 数不变（$NEW_COUNT）"
fi

hdr "同步镜像分支"
git switch --quiet "$MIRROR_BRANCH" && git merge --ff-only --quiet "$UP" && ok "$MIRROR_BRANCH -> $(git rev-parse --short "$UP")"
git switch --quiet "$INTEGRATION_BRANCH"

TEST_RESULT="未配置 TEST_CMD"
if [ "$DO_TEST" = "1" ] && [ -n "$TEST_CMD" ]; then
  hdr "自检: $TEST_CMD"
  if bash -c "$TEST_CMD"; then TEST_RESULT="PASS"; ok "自检通过"
  else TEST_RESULT="FAIL(必须修)"; err "自检失败 —— 不要推送，先修"; fi
fi

hdr "记台账"
LOG="$FORK_ROOT/docs/fork/SYNC-LOG.md"
mkdir -p "$(dirname "$LOG")"
{
  printf '\n## %s 同步\n\n' "$(day)"
  printf -- '- 上游: `%s .. %s`（%s 提交）\n' "$(git rev-parse --short "$OLD_BASE")" "$(git rev-parse --short "$UP")" "$INCOMING"
  printf -- '- patch 重放: %s 个（range-diff 已比对）\n' "$NEW_COUNT"
  printf -- '- 冲突面: %s 个重叠文件%s\n' "$N_OVERLAP" "$( [ "$N_OVERLAP" -gt 0 ] && printf ' — 见上方输出，处理记录补在这里' || printf '（无）')"
  printf -- '- 测试: %s\n' "$TEST_RESULT"
  printf -- '- 人工确认: TODO 谁看的、结论\n'
} >> "$LOG"
ok "已追加到 docs/fork/SYNC-LOG.md（记得补「人工确认」并提交）"

hdr "下一步"
cat <<EOF
  git push origin $MIRROR_BRANCH $INTEGRATION_BRANCH
  bash .fork/bin/fork-doctor.sh
EOF
[ "$TEST_RESULT" = "FAIL(必须修)" ] && exit 1
exit 0
