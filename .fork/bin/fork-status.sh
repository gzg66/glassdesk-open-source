#!/usr/bin/env bash
# fork-status.sh —— 二开现状总览：上游偏差、patch 清单、台账完整性、失效预警。
#
#   bash fork-status.sh            # 人类可读
#   bash fork-status.sh --json     # 机器可读
#   bash fork-status.sh --ledger   # 重建 docs/fork/PATCHES.md
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/lib.sh"

JSON=0; LEDGER=0
for a in "$@"; do case "$a" in --json) JSON=1;; --ledger) LEDGER=1;; *) die "未知参数: $a";; esac; done

fork_load
need_upstream
UP="$UPSTREAM_REMOTE/$UPSTREAM_BRANCH"
BASE="$(git merge-base "$UP" "$INTEGRATION_BRANCH")"
INCOMING="$(git rev-list --count "$MIRROR_BRANCH..$UP" 2>/dev/null || echo 0)"
BEHIND_SINCE="$(git rev-list --count "$BASE..$UP" 2>/dev/null || echo 0)"
PATCH_N="$(git rev-list --count "$BASE..$INTEGRATION_BRANCH" 2>/dev/null || echo 0)"
UP_HEAD="$(git rev-parse --short "$UP")"

# 每 patch 一行: id|subject|files|age_days
rows=""
missing_doc=0
while IFS= read -r sha; do
  [ -n "$sha" ] || continue
  subj="$(git log -1 --format=%s "$sha")"
  id="$(printf '%s' "$subj" | sed -n "s/^${PATCH_PREFIX}(\([0-9]*\)).*/\1/p")"
  [ -n "$id" ] || id="---"
  nf="$(git show --pretty=format: --name-only "$sha" | sed '/^$/d' | wc -l | tr -d ' ')"
  ts="$(git log -1 --format=%ct "$sha")"
  age=$(( ( $(date +%s) - ts ) / 86400 ))
  pad="$(printf '%0*d' "$PATCH_ID_WIDTH" "$id" 2>/dev/null || echo "$id")"
  doc="$(ls "$(patch_dir)" 2>/dev/null | grep -E "^${id}-" | head -1 || true)"
  [ -n "$doc" ] || { case "$id" in "---"|"000") doc="(脚手架)" ;; *) missing_doc=$((missing_doc+1)); doc="-" ;; esac; }
  rows="${rows}${pad}|${subj}|${nf}|${age}|${doc}"$'\n'
done <<EOF
$(git rev-list --reverse "$BASE..$INTEGRATION_BRANCH" 2>/dev/null)
EOF

if [ "$LEDGER" = "1" ]; then
  out="$FORK_ROOT/docs/fork/PATCHES.md"
  mkdir -p "$(dirname "$out")"
  {
    printf '# patch 台账（自动生成，勿手改）\n\n'
    printf '生成时间: %s ｜ 上游: `%s` @ %s ｜ 集成分支: `%s`\n\n' "$(stamp)" "$UP" "$UP_HEAD" "$INTEGRATION_BRANCH"
    printf '每行的明细在 `docs/fork/patches/`。改 patch 就改那里的文件，再重建本表。\n\n'
    printf '| # | 提交 | 改动文件 | 龄期(天) | 明细文档 |\n|---|---|---|---|---|\n'
    printf '%s' "$rows" | awk -F'|' 'NF>2 {printf "| %s | %s | %s | %s | %s |\n",$1,$2,$3,$4,$5}'
    printf '\n合计: %s 个 patch（预算 %s）\n' "$PATCH_N" "$MAX_PATCHES"
  } > "$out"
  ok "已重建 docs/fork/PATCHES.md"
  exit 0
fi

if [ "$JSON" = "1" ]; then
  esc() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }
  printf '{\n "upstream":"%s","upstream_head":"%s","incoming":%s,"behind_since_last_sync":%s,\n' \
    "$(esc "$UP")" "$UP_HEAD" "$INCOMING" "$BEHIND_SINCE"
  printf ' "patch_count":%s,"patch_budget":%s,"missing_docs":%s,\n' "$PATCH_N" "$MAX_PATCHES" "$missing_doc"
  printf ' "patches":['
  first=1
  printf '%s' "$rows" | while IFS='|' read -r id subj nf age doc; do
    [ -n "$id" ] || continue
    if [ "$first" = "1" ]; then first=0; else printf ','; fi
    printf '{"id":"%s","subject":"%s","files":%s,"age_days":%s,"doc":"%s"}' \
      "$(esc "$id")" "$(esc "$subj")" "$nf" "$age" "$(esc "$doc")"
    printf '\n'
  done
  printf ' ]\n}\n'
  exit 0
fi

hdr "上游"
dim "远端      : $UP @ $UP_HEAD"
dim "镜像分支  : $MIRROR_BRANCH"
if [ "$INCOMING" -eq 0 ]; then ok "已是最新"
else warn "落后 $INCOMING 个提交（其中 $BEHIND_SINCE 个在 patch 基线之后）"; fi

hdr "patch 层（$PATCH_N / 预算 $MAX_PATCHES）"
if [ "$PATCH_N" -eq 0 ]; then dim "还没有 patch"; else
  printf '%-6s %-58s %-5s %-6s %s\n' "#" "提交" "文件" "龄期" "明细"
  printf '%s' "$rows" | awk -F'|' 'NF>2 {printf "%-6s %-58.58s %-5s %-6s %s\n",$1,$2,$3,$4,$5}'
fi

hdr "问题"
if [ "$missing_doc" -gt 0 ]; then warn "$missing_doc 个 patch 没有 docs/fork/patches/<id>-*.md 明细 —— 三个月后没人知道它为什么存在"; fi
STALE="$(printf '%s' "$rows" | awk -F'|' -v d="$STALE_DAYS" 'NF>2 && $4>d {printf "%s ",$1}')"
[ -n "$STALE" ] && warn "patch 龄期 > ${STALE_DAYS} 天: $STALE——顺手确认上游是不是已经做了同样的事"
if [ "$PATCH_N" -gt "$MAX_PATCHES" ]; then warn "patch 数超预算 $MAX_PATCHES，该把通用的推上游 / 该拆仓库了"; fi
[ "$missing_doc" -eq 0 ] && [ -z "$STALE" ] && ok "无"

dim ""
dim "体检: bash .fork/bin/fork-doctor.sh    台账: bash .fork/bin/fork-status.sh --ledger"
