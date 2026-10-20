#!/usr/bin/env bash
# fork-verify.sh —— 同步后的自检入口（.fork/config 里的 TEST_CMD 指向它）。
#
# 构建只能在 Windows 上跑（.NET 10 + WPF），所以这里转调 scripts/fork-verify.ps1。
# WSL 里用 powershell.exe 互操作；Git Bash 里直接调 powershell.exe。
#
#   bash scripts/fork-verify.sh          # Release 构建 src/GlassDesk.App
#   bash scripts/fork-verify.sh --kill   # 先杀掉占用输出目录的 GlassDesk（MSB3021 时用）
#   bash scripts/fork-verify.sh --full   # 构建 + Core/Dock 基线测试
set -uo pipefail

ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || { echo "不在 git 仓库内" >&2; exit 2; }
cd "$ROOT" || exit 2

PS_ARGS=()
for a in "$@"; do
  case "$a" in
    --kill) PS_ARGS+=("-Kill") ;;
    --full) PS_ARGS+=("-Full") ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    *) echo "未知参数: $a" >&2; exit 2 ;;
  esac
done

PS_EXE=""
for c in powershell.exe pwsh.exe pwsh; do
  if command -v "$c" >/dev/null 2>&1; then PS_EXE="$c"; break; fi
done
[ -n "$PS_EXE" ] || { echo "找不到 powershell.exe —— 这个仓库的构建只能在 Windows 侧跑" >&2; exit 2; }

if command -v wslpath >/dev/null 2>&1; then
  SCRIPT_WIN="$(wslpath -w "$ROOT/scripts/fork-verify.ps1")"
else
  SCRIPT_WIN="$ROOT/scripts/fork-verify.ps1"
fi

# 用 -File 而不是 -Command：路径里有中文，避免二次解析
if [ "${#PS_ARGS[@]}" -gt 0 ]; then
  exec "$PS_EXE" -NoProfile -ExecutionPolicy Bypass -File "$SCRIPT_WIN" "${PS_ARGS[@]}"
fi
exec "$PS_EXE" -NoProfile -ExecutionPolicy Bypass -File "$SCRIPT_WIN"
