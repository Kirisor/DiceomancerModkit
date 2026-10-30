#!/usr/bin/env bash
# 编译平台层 ModKit.Game.dll。
#
# 引用来源（顺序重要）：**先用 dist/platform 里刚编出来的** ModKit.Api / ModKit.Core，
# 没有才退回游戏目录里已安装的那份。为什么：
#   Api/Core 一旦加了新字段（如遗物的 rarity / cardRequirement / poolColors），
#   Game 层就要用它们；对着旧 dll 编会直接 CS0234（类型不存在），
#   而"对着旧 dll 编过了"更糟 —— 运行期会 MissingMethod/字段缺失。
#   先用 bash tools/build-api-core.sh 产出版本正确的 Api/Core。
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

API_DLL="$OUT_DIR/ModKit.Api.dll"
CORE_DLL="$OUT_DIR/ModKit.Core.dll"
[ -f "$API_DLL" ] || API_DLL="$PLUGINS/ModKit.Api.dll"
[ -f "$CORE_DLL" ] || CORE_DLL="$PLUGINS/ModKit.Core.dll"

REFS=()
for f in "$MANAGED"/*.dll; do REFS+=("-r:$f"); done
for f in "$CORE/BepInEx.dll" "$CORE/0Harmony.dll" "$API_DLL" "$CORE_DLL" "$MODKIT_ROOT/lib/Newtonsoft.Json.dll"; do
  [ -f "$f" ] && REFS+=("-r:$f")
done

"$DOTNET" exec "$CSC" \
  -nologo -target:library -nostdlib+ -langversion:9 -optimize+ -deterministic \
  -out:"$OUT_DIR/ModKit.Game.dll" \
  "${REFS[@]}" \
  "$MODKIT_ROOT"/src/ModKit.Game/*.cs

echo "built: $OUT_DIR/ModKit.Game.dll ($(stat -c%s "$OUT_DIR/ModKit.Game.dll") bytes)"
