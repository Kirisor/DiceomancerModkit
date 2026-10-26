#!/usr/bin/env bash
# 编译平台层 ModKit.Game.dll（其余平台 dll 用已安装的 0.4.0 产物作引用）。
#
# 背景：仓库里没有平台层构建脚本（平台 dll 一直是发布产物）。本地改动只有
# ModKit.Game（适配层兜底）与 ModKit.Plugin（预加载），所以用 csc 直接编这两个。
#
# 用法：bash tools/build-game.sh [输出目录]
set -eu

. "$(cd "$(dirname "$0")" && pwd)/env.sh"

GAME="$DICEOMANCER_DIR"
MANAGED="$GAME/Diceomancer_Data/Managed"
CORE="$GAME/BepInEx/core"
PLUGINS="$GAME/BepInEx/plugins"
OUT_DIR="${1:-$MODKIT_ROOT/dist/platform}"

mkdir -p "$OUT_DIR"

REFS=()
for f in "$MANAGED"/*.dll; do REFS+=("-r:$f"); done
for f in "$CORE/BepInEx.dll" "$CORE/0Harmony.dll" "$PLUGINS/ModKit.Api.dll" "$PLUGINS/ModKit.Core.dll" "$MODKIT_ROOT/lib/Newtonsoft.Json.dll"; do
  [ -f "$f" ] && REFS+=("-r:$f")
done

"$DOTNET" exec "$CSC" \
  -nologo -target:library -nostdlib+ -langversion:9 -optimize+ -deterministic \
  -out:"$OUT_DIR/ModKit.Game.dll" \
  "${REFS[@]}" \
  "$MODKIT_ROOT"/src/ModKit.Game/*.cs

echo "built: $OUT_DIR/ModKit.Game.dll ($(stat -c%s "$OUT_DIR/ModKit.Game.dll") bytes)"
