#!/usr/bin/env bash
# 只编译平台入口程序集 ModKit.Plugin.dll（其余三个平台 dll 直接用已安装的 0.4.0 产物）。
#
# 为什么单独一个脚本：仓库里没有平台层的构建脚本（平台 dll 一直是发布产物），
# 而 ModKit.Plugin 是唯一需要在本地改动的项目；用 csc 直接编，避免为它引入完整工程。
#
# 用法：bash tools/build-plugin.sh [输出目录]
#   默认输出到 dist/platform/
set -eu

. "$(cd "$(dirname "$0")" && pwd)/env.sh"

GAME="$DICEOMANCER_DIR"
MANAGED="$GAME/Diceomancer_Data/Managed"
CORE="$GAME/BepInEx/core"
PLUGINS="$GAME/BepInEx/plugins"
OUT_DIR="${1:-$MODKIT_ROOT/dist/platform}"

mkdir -p "$OUT_DIR"

"$DOTNET" exec "$CSC" \
  -nologo -target:library -nostdlib+ -langversion:9 -optimize+ -deterministic \
  -out:"$OUT_DIR/ModKit.Plugin.dll" \
  -r:"$MANAGED/mscorlib.dll" \
  -r:"$MANAGED/System.dll" \
  -r:"$MANAGED/System.Core.dll" \
  -r:"$MANAGED/System.Xml.dll" \
  -r:"$MANAGED/netstandard.dll" \
  -r:"$MANAGED/UnityEngine.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$CORE/BepInEx.dll" \
  -r:"$CORE/0Harmony.dll" \
  -r:"$PLUGINS/ModKit.Api.dll" \
  -r:"$PLUGINS/ModKit.Core.dll" \
  -r:"$PLUGINS/ModKit.Game.dll" \
  "$MODKIT_ROOT"/src/ModKit.Plugin/*.cs

echo "built: $OUT_DIR/ModKit.Plugin.dll ($(stat -c%s "$OUT_DIR/ModKit.Plugin.dll") bytes)"
