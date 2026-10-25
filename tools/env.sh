#!/usr/bin/env bash
# 构建脚本共用的外部路径解析。
#
# 脚本以前把 G 盘的绝对路径写死在文件里，换盘以后全部失效；改成在这里统一定位，
# 需要时用环境变量覆盖即可：
#   MODKIT_ROOT      本仓库根目录（默认由本文件位置推出）
#   DICEOMANCER_DIR  游戏根目录（含 Diceomancer.exe）
#   DOTNET           dotnet 可执行文件（必须带 SDK，仅运行时不算）
#   CSC              Roslyn csc.dll（配合 `dotnet exec` 使用）
#
# 用法：source "$(cd "$(dirname "$0")/.." && pwd)/tools/env.sh"

MODKIT_ROOT="${MODKIT_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"

if [ -z "${DICEOMANCER_DIR:-}" ]; then
  for cand in "F:/DICEOMANCER" "C:/DICEOMANCER" "G:/DICEOMANCER"; do
    if [ -d "$cand" ]; then
      DICEOMANCER_DIR="$cand"
      break
    fi
  done
  DICEOMANCER_DIR="${DICEOMANCER_DIR:-F:/DICEOMANCER}"
fi

# 默认优先用户级 SDK（%USERPROFILE%\.dotnet），系统目录里常常只有运行时。
if [ -z "${DOTNET:-}" ]; then
  for cand in "$HOME/.dotnet/dotnet.exe" "/c/Program Files/dotnet/dotnet.exe" "$(command -v dotnet 2>/dev/null)"; do
    [ -n "$cand" ] || continue
    [ -x "$cand" ] || continue
    if [ -n "$("$cand" --list-sdks 2>/dev/null)" ]; then
      DOTNET="$cand"
      break
    fi
  done
fi

if [ -z "${DOTNET:-}" ]; then
  echo "✗ 找不到带 SDK 的 dotnet：装 .NET 10 SDK，或设 DOTNET=<dotnet 可执行文件路径>" >&2
  return 1 2>/dev/null || exit 1
fi

if [ -z "${CSC:-}" ]; then
  sdk_line="$("$DOTNET" --list-sdks 2>/dev/null | tail -1)"
  if [ -n "$sdk_line" ]; then
    sdk_ver="${sdk_line%% *}"
    sdk_root="$(printf '%s' "$sdk_line" | sed -E 's/^[^[]*\[(.*)\]$/\1/')"
    if command -v cygpath >/dev/null 2>&1; then
      sdk_root="$(cygpath -u "$sdk_root")"
    fi
    CSC="$sdk_root/$sdk_ver/Roslyn/bincore/csc.dll"
  fi
fi

export MODKIT_ROOT DICEOMANCER_DIR DOTNET CSC
