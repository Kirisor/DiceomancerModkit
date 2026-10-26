#!/usr/bin/env bash
# 编译独立插件「战绩（Run History）」→ dist/runhistory/RunHistory.dll
#
# 用法：bash tools/build-runhistory.sh [输出目录]
# 说明：这是独立的 BepInEx 插件（不是 ModKit 内容 mod），产物直接放进 BepInEx/plugins/。
set -eu

. "$(cd "$(dirname "$0")" && pwd)/env.sh"

GAME="$DICEOMANCER_DIR"
MANAGED="$GAME/Diceomancer_Data/Managed"
CORE="$GAME/BepInEx/core"
OUT_DIR="${1:-$MODKIT_ROOT/dist/runhistory}"

mkdir -p "$OUT_DIR"

"$DOTNET" exec "$CSC" \
  -nologo -target:library -nostdlib+ -langversion:9 -optimize+ -deterministic \
  -out:"$OUT_DIR/RunHistory.dll" \
  -r:"$MANAGED/mscorlib.dll" \
  -r:"$MANAGED/System.dll" \
  -r:"$MANAGED/System.Core.dll" \
  -r:"$MANAGED/netstandard.dll" \
  -r:"$MANAGED/UnityEngine.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$MANAGED/UnityEngine.UIModule.dll" \
  -r:"$MANAGED/UnityEngine.UI.dll" \
  -r:"$MANAGED/UnityEngine.PhysicsModule.dll" \
  -r:"$MANAGED/UnityEngine.Physics2DModule.dll" \
  -r:"$MANAGED/Unity.TextMeshPro.dll" \
  -r:"$MANAGED/Unity.Localization.dll" \
  -r:"$MANAGED/Newtonsoft.Json.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$CORE/BepInEx.dll" \
  -r:"$CORE/0Harmony.dll" \
  "$MODKIT_ROOT"/examples/runhistory/src/*.cs

echo "built: $OUT_DIR/RunHistory.dll ($(stat -c%s "$OUT_DIR/RunHistory.dll") bytes)"
