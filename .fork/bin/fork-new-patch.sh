#!/usr/bin/env bash
# fork-new-patch.sh —— 开一个新 patch：编号 + 台账明细 +（可选）独立分支。
#
#   bash fork-new-patch.sh <slug> [--id 007] [--files "a.py b.py"] [--branch] [--upstreamable]
#
# 约定：一个 patch = 一个逻辑改动 = 一个（或几个关联）提交 = 一份明细文档。
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/lib.sh"

SLUG=""; WANT_ID=""; FILES=""; MAKE_BRANCH=0; UPSTREAMABLE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --id) WANT_ID="${2:-}"; shift 2 ;;
    --files) FILES="${2:-}"; shift 2 ;;
    --branch) MAKE_BRANCH=1; shift ;;
    --upstreamable) UPSTREAMABLE=1; shift ;;
    -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
    -*) die "未知参数: $1" ;;
    *) SLUG="$1"; shift ;;
  esac
done
[ -n "$SLUG" ] || die "用法: bash fork-new-patch.sh <slug>  例如 fix-tls-verify-loop"
SLUG="$(printf '%s' "$SLUG" | tr '[:upper:] _' '[:lower:]--' | tr -cd 'a-z0-9.-')"

fork_load
need_upstream
DIRTY="$(git status --porcelain | grep -v '^?? .fork/state/' | wc -l | tr -d ' ')"
[ "$DIRTY" -gt 0 ] && warn "工作区有 $DIRTY 个未提交改动（不影响开单，但别和 patch 混在一起提交）"
CUR="$(git symbolic-ref --short HEAD)"
[ "$CUR" = "$INTEGRATION_BRANCH" ] || warn "当前在 $CUR，不是 $INTEGRATION_BRANCH"

BASE="$(git merge-base "$UPSTREAM_REMOTE/$UPSTREAM_BRANCH" "$INTEGRATION_BRANCH")"
MAXID=0
while IFS= read -r s; do
  id="$(printf '%s' "$s" | sed -n "s/^${PATCH_PREFIX}(\([0-9]*\)).*/\1/p")"
  [ -n "$id" ] || continue
  [ "$id" -gt "$MAXID" ] && MAXID="$id"
done <<EOF
$(git log --format=%s "$BASE..$INTEGRATION_BRANCH" 2>/dev/null)
EOF
for f in "$(patch_dir)"/*.md; do
  [ -f "$f" ] || continue
  id="$(basename "$f" | sed -n 's/^\([0-9]*\)-.*/\1/p')"
  [ -n "$id" ] && [ "$id" -gt "$MAXID" ] && MAXID="$id"
done
NEXT=$((MAXID + 1))
ID="${WANT_ID:-$(printf "%0*d" "$PATCH_ID_WIDTH" "$NEXT")}"
DOC="$(patch_dir)/${ID}-${SLUG}.md"

mkdir -p "$(patch_dir)"
if [ -f "$DOC" ]; then
  warn "明细已存在: docs/fork/patches/${ID}-${SLUG}.md（复用）"
else
  cat > "$DOC" <<EOF
# patch ${ID}: ${SLUG}

- 开单: $(day)
- 状态: 进行中 | 待评审 | 已合入 | 已废弃   ← 选一个
- 能不能不改源码做到？(钩子/配置/插件/JWT/hook 点) 结论:
- 为什么上游不会接受 / 该不该推上游:
- 触及的上游文件: ${FILES:-（填相对路径，越少越好）}
- 新增文件（放我们自己的扩展目录）:
- 上游同类实现/已有 PR:
- 回滚方式: 单独 revert 本 patch 即可？（应该可以，否则包太大了）
- 验证:
  - [ ] 单元/集成测试
  - [ ] 手工验证步骤:
  - [ ] \`bash .fork/bin/fork-sync.sh --apply\` 后仍通过

## 目的（一句话，写给人看，不是写给编译器看）

## 改动要点

## 风险 / 以后要盯的地方
EOF
  ok "明细: docs/fork/patches/${ID}-${SLUG}.md"
fi

if [ "$MAKE_BRANCH" = "1" ]; then
  BR="patch/${ID}-${SLUG}"
  git switch --quiet -c "$BR" && ok "分支: $BR（基于 $CUR）"
  dim "在分支上提交，回到 $INTEGRATION_BRANCH 后 git cherry-pick $(printf '%.7s' "$(git rev-parse HEAD)")"
fi

hdr "下一个提交信息（pre-commit 会强制这个格式）"
cat <<EOF
  ${PATCH_PREFIX}(${ID}): $( [ "$UPSTREAMABLE" = "1" ] && printf 'feat' || printf 'fix' )(scope): 一句话说明

  目的: <为什么需要，不是改了什么>
  上游: <不适用 / issue#123 / 我们的私货>
  边界: <改了哪些上游文件，为什么非改不可>
EOF

hdr "红线（写的时候自查）"
cat <<EOF
  · 只碰必要的行：不要顺手格式化、不要重排 import、不要重命名上游符号
  · 新逻辑放我们自己的目录（internal/patched/、ext/），上游文件里只留一个调用点
  · 一个 patch 只解决一个需求，方便 revert、方便将来删
  · 能不 patch 就不 patch：先看上游有没有钩子/配置/插件点
EOF
