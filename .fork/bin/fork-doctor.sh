#!/usr/bin/env bash
# fork-doctor.sh —— 薄 patch 层体检。便宜、可反复跑、失败即红线。
#
#   bash fork-doctor.sh           # 报告；有 FAIL 则 exit 1
#   bash fork-doctor.sh --quiet   # 只打印问题
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/lib.sh"

QUIET=0
for a in "$@"; do case "$a" in --quiet) QUIET=1;; *) die "未知参数: $a";; esac; done

fork_load
need_upstream
UP="$UPSTREAM_REMOTE/$UPSTREAM_BRANCH"
FAIL=0; WARN_N=0
bad()  { FAIL=$((FAIL+1)); err "$*"; }
soft() { WARN_N=$((WARN_N+1)); warn "$*"; }

hdr "fork-doctor"

# 1) 镜像分支必须纯上游祖先关系
if git rev-parse --verify -q "refs/heads/$MIRROR_BRANCH" >/dev/null; then
  EXTRA="$(git rev-list --count "$UP..$MIRROR_BRANCH" 2>/dev/null || echo 0)"
  if [ "$EXTRA" -gt 0 ]; then
    bad "镜像分支 $MIRROR_BRANCH 有 $EXTRA 个非上游提交 —— 这是薄层破裂的根因。
    修: git switch $MIRROR_BRANCH && git reset --hard $UP"
  else ok "镜像分支干净（$MIRROR_BRANCH 是 $UP 的祖先/等于）"; fi
else
  bad "镜像分支 $MIRROR_BRANCH 不存在"
fi

# 2) patch 层里不能混入上游的 merge commit
MERGES="$(git rev-list --merges "$UP..$INTEGRATION_BRANCH" 2>/dev/null | wc -l | tr -d ' ')"
if [ "$MERGES" -gt 0 ]; then
  bad "集成分支含 $MERGES 个 merge 提交 —— 说明曾经 git merge upstream 进来，薄层已断。
    修: 回到 rebase 模型；历史已脏的话用
    git rebase --onto $UP <旧基线> $INTEGRATION_BRANCH"
else ok "集成分支无 merge 提交（纯 rebase 层）"; fi

# 3) 每个 patch 的体量
BASE="$(git merge-base "$UP" "$INTEGRATION_BRANCH")"
TOTAL=0; THICK=0
while IFS= read -r sha; do
  [ -n "$sha" ] || continue
  case "$(printf '%s' "$(git log -1 --format=%s "$sha")" | sed -n "s/^${PATCH_PREFIX}(0*0):.*/BOOT/p")" in
    BOOT) continue ;;
  esac
  TOTAL=$((TOTAL+1))
  nf="$(git show --pretty=format: --name-only "$sha" | sed '/^$/d' | wc -l | tr -d ' ')"
  subj="$(git log -1 --format=%s "$sha")"
  if [ "$nf" -gt "$MAX_FILES_PER_PATCH" ]; then
    THICK=$((THICK+1)); soft "太厚: $(printf '%.60s' "$subj") 改了 $nf 个文件（上限 $MAX_FILES_PER_PATCH）"
  fi
  # 大改写检测: 删除行数
  del="$(git show --numstat --format= "$sha" | awk '{s+=$2} END{print s+0}')"
  if [ "${del:-0}" -gt 300 ]; then
    soft "大改写: $(printf '%.60s' "$subj") 删除 $del 行 —— 要么拆，要么承认这是 fork-depth 不是 patch"
  fi
done <<EOF2
$(git rev-list "$BASE..$INTEGRATION_BRANCH" 2>/dev/null)
EOF2
[ "$TOTAL" -eq 0 ] && dim "还没有 patch"
[ "$TOTAL" -gt 0 ] && [ "$THICK" -eq 0 ] && ok "所有 patch 体量在上限内（$TOTAL 个）"

# 4) 提交信息规范
BADMSG="$(git log --format='%h %s' "$BASE..$INTEGRATION_BRANCH" 2>/dev/null | grep -vE "^[0-9a-f]+ ${PATCH_PREFIX}\([0-9]+\): " || true)"
if [ -n "$BADMSG" ]; then bad "提交信息不合规（应为 ${PATCH_PREFIX}(NNN): type(scope): 说明）:
$BADMSG"; else [ "$TOTAL" -gt 0 ] && ok "提交信息规范"; fi

# 5) 台账：每个 patch 该有明细文档
MISSING=""
while IFS= read -r sha; do
  [ -n "$sha" ] || continue
  subj="$(git log -1 --format=%s "$sha")"
  id="$(printf '%s' "$subj" | sed -n "s/^${PATCH_PREFIX}(\([0-9]*\)).*/\1/p")"
  [ -n "$id" ] || continue
  [ "$id" = "000" ] && continue
  ls "$(patch_dir)" 2>/dev/null | grep -qE "^${id}-" || MISSING="$MISSING $id"
done <<EOF3
$(git rev-list "$BASE..$INTEGRATION_BRANCH" 2>/dev/null)
EOF3
if [ -n "$MISSING" ]; then soft "缺明细文档的 patch:$MISSING —— 补 docs/fork/patches/<id>-<slug>.md"; else
  [ "$TOTAL" -gt 0 ] && ok "台账完整"; fi

# 6) 预算
if [ "$TOTAL" -gt "$MAX_PATCHES" ]; then soft "patch 数 $TOTAL 超预算 $MAX_PATCHES —— 该回流上游或重新评估 fork"; fi

# 7) hooks / rerere 是否还在
SHIM="$FORK_ROOT/.git/hooks/pre-commit"
if [ -f "$SHIM" ] && grep -q "fork-thin-patch shim" "$SHIM" 2>/dev/null; then
  ok "git hooks 已装（shim 常驻）"
else
  soft "缺少 .git/hooks shim —— 守卫失效，重跑 bash scripts/fork-init.sh 补装"
fi
[ -x "$FORK_ROOT/.fork/hooks/commit-msg" ] || soft "工作树里缺 .fork/hooks/commit-msg（当前分支没有脚手架？）"
[ "$(git config --get rerere.enabled || true)" = "true" ] || soft "rerere 未开，重复冲突会重复手工解"
[ -f "$FORK_DIR/config" ] && ok "配置存在"

# 8) 工作区状态
if on_rebase; then bad "有未完成的 rebase 停在半路 —— 先 --continue 或 --abort"; fi
DIRTY="$(git status --porcelain | grep -v '^?? .fork/state/' | wc -l | tr -d ' ')"
[ "$DIRTY" -gt 0 ] && dim "工作区有 $DIRTY 个未提交改动（不阻塞，但同步前需 clean）"

hdr "结论"
if [ "$FAIL" -gt 0 ]; then err "$FAIL 个硬失败，$WARN_N 个提醒 —— 先修硬失败"; exit 1; fi
if [ "$WARN_N" -gt 0 ]; then warn "0 硬失败，$WARN_N 个提醒（可接受，但要记账）"; else ok "薄层健康"; fi
exit 0
