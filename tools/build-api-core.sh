#!/usr/bin/env bash
# 编译平台底层两个程序集：ModKit.Api / ModKit.Core。
#
# 为什么需要它：这两个 dll 一直是"发布产物"，仓库里没有构建脚本，于是
# 给 Api/Core 加字段（如遗物的 rarity / cardRequirement / poolColors）后
# 只能靠改 dll 二进制 —— 不可能。既然源码在仓库里，就必须能重编。
#
# 依赖方向（与源码一致）：Core → Api → BCL（Core 另需 Newtonsoft.Json）。
# 用法：bash tools/build-api-core.sh [输出目录]（默认 dist/platform）
set -eu

. "$(cd "$(dirname "$0")" && pwd)/env.sh"

MANAGED="$DICEOMANCER_DIR/Diceomancer_Data/Managed"
OUT_DIR="${1:-$MODKIT_ROOT/dist/platform}"
mkdir -p "$OUT_DIR"

"$DOTNET" exec "$CSC" \
  -nologo -target:library -nostdlib+ -langversion:9 -optimize+ -deterministic \
  -out:"$OUT_DIR/ModKit.Api.dll" \
  -r:"$MANAGED/mscorlib.dll" \
  -r:"$MANAGED/System.dll" \
  -r:"$MANAGED/System.Core.dll" \
  -r:"$MANAGED/netstandard.dll" \
  "$MODKIT_ROOT"/src/ModKit.Api/*.cs
echo "built: $OUT_DIR/ModKit.Api.dll ($(stat -c%s "$OUT_DIR/ModKit.Api.dll") bytes)"

"$DOTNET" exec "$CSC" \
  -nologo -target:library -nostdlib+ -langversion:9 -optimize+ -deterministic \
  -out:"$OUT_DIR/ModKit.Core.dll" \
  -r:"$MANAGED/mscorlib.dll" \
  -r:"$MANAGED/System.dll" \
  -r:"$MANAGED/System.Core.dll" \
  -r:"$MANAGED/netstandard.dll" \
  -r:"$OUT_DIR/ModKit.Api.dll" \
  -r:"$MODKIT_ROOT/lib/Newtonsoft.Json.dll" \
  "$MODKIT_ROOT"/src/ModKit.Core/*.cs
echo "built: $OUT_DIR/ModKit.Core.dll ($(stat -c%s "$OUT_DIR/ModKit.Core.dll") bytes)"
