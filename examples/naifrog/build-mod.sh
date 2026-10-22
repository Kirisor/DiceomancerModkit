#!/usr/bin/env bash
# =============================================================================
#  手动编译 naifrog（mod）—— 改了 src/*.cs 后跑这一个脚本即可
#
#  用法：
#    bash build-mod.sh
#
#  产物：同目录 naifrog.dll（覆盖）。若游戏正在运行，dll 被占用会失败：
#        先关游戏，或让脚本编译到 _build/ 再手动复制。
# =============================================================================
set -e

MOD_DIR="$(cd "$(dirname "$0")" && pwd)"
GAME_ROOT="${DICEOMANCER_DIR:-G:/deepseek space/DICEOMANCER}"
MANAGED="$GAME_ROOT/Diceomancer_Data/Managed"
DOTNET="${DOTNET:-G:/deepseek space/dotnet-sdk/dotnet.exe}"
CSC="${CSC:-G:/deepseek space/dotnet-sdk/sdk/10.0.401/Roslyn/bincore/csc.dll}"

OUT="$MOD_DIR/_build/naifrog.dll"
mkdir -p "$MOD_DIR/_build"

echo "== 编译 $MOD_DIR/src/*.cs"
"$DOTNET" exec "$CSC" -nologo -target:library -nostdlib+ -langversion:9 -optimize+ \
  -out:"$OUT" \
  -r:"$MANAGED/mscorlib.dll" \
  -r:"$MANAGED/System.dll" \
  -r:"$MANAGED/System.Core.dll" \
  -r:"$MANAGED/netstandard.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$MANAGED/UniTask.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$GAME_ROOT/BepInEx/plugins/ModKit.Api.dll" \
  -r:"$GAME_ROOT/BepInEx/core/0Harmony.dll" \
  "$MOD_DIR"/src/*.cs

echo "== 编译完成：$OUT"

if tasklist 2>/dev/null | grep -qi Diceomancer; then
  echo "!! 游戏正在运行，dll 被占用，未覆盖。关掉游戏后重跑，或手动复制："
  echo "   cp \"$OUT\" \"$MOD_DIR/naifrog.dll\""
  exit 0
fi

cp -f "$OUT" "$MOD_DIR/naifrog.dll"
echo "== 已安装：$MOD_DIR/naifrog.dll（重启游戏生效）"
