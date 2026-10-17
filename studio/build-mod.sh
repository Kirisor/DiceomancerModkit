#!/usr/bin/env bash
# 编译一个 mod 的 C# 部分：<modsRoot>/<modId>/src/*.cs  →  <modsRoot>/<modId>/<modId>.dll
#
# 用法：
#   bash build-mod.sh                 # 默认编译 manual_demo
#   bash build-mod.sh <modId>          # 编译指定 mod
#   bash build-mod.sh <modId> <modsRoot>
#
# 说明：
#   · 只引用平台 DLL（ModKit.Api）与游戏程序集，不引用任何游戏源码；
#   · 编译前把旧 dll 备份成 <modId>.dll.<时间戳>.bak；
#   · 游戏正在运行时不会覆盖 dll（文件被占用会失败），脚本会提前提示。

set -u

GAME="/g/deepseek space/DICEOMANCER"
MODS_ROOT="${2:-$GAME/BepInEx/DiceomancerMods}"
MOD_ID="${1:-manual_demo}"
MOD_DIR="$MODS_ROOT/$MOD_ID"

DOTNET="/g/deepseek space/dotnet-sdk/dotnet.exe"
CSC="/g/deepseek space/dotnet-sdk/sdk/10.0.401/Roslyn/bincore/csc.dll"
MG="$GAME/Diceomancer_Data/Managed"
PLUGINS="$GAME/BepInEx/plugins"
OUT="$MOD_DIR/$MOD_ID.dll"

echo "mod      : $MOD_ID"
echo "目录     : $MOD_DIR"
echo "输出     : $OUT"

if [ ! -d "$MOD_DIR/src" ]; then
  echo "✗ 找不到 $MOD_DIR/src" >&2
  exit 1
fi

SRC_FILES=("$MOD_DIR"/src/*.cs)
if [ ! -e "${SRC_FILES[0]}" ]; then
  echo "✗ $MOD_DIR/src 下没有 .cs 文件" >&2
  exit 1
fi

REFS=()
for f in mscorlib.dll System.dll System.Core.dll System.Xml.dll netstandard.dll \
         Assembly-CSharp.dll UnityEngine.dll UnityEngine.CoreModule.dll UnityEngine.UI.dll UnityEngine.UIModule.dll \
         UnityEngine.TextRenderingModule.dll Unity.TextMeshPro.dll UnityEngine.LocalizationModule.dll Unity.Localization.dll \
         Unity.ResourceManager.dll UnityEngine.ImageConversionModule.dll UnityEngine.AudioModule.dll FMODUnity.dll \
         UnityEngine.Physics2DModule.dll UnityEngine.PhysicsModule.dll \
         UnityEngine.InputLegacyModule.dll Newtonsoft.Json.dll UniTask.dll UniTask.Linq.dll; do
  if [ -f "$MG/$f" ]; then REFS+=("-r:$MG/$f"); fi
done
for f in ModKit.Api.dll ModKit.Core.dll; do
  if [ -f "$PLUGINS/$f" ]; then REFS+=("-r:$PLUGINS/$f"); fi
done
# BepInEx / Harmony：mod 里可能有 HarmonyPatch 补丁
if [ -f "$GAME/BepInEx/core/BepInEx.dll" ]; then REFS+=("-r:$GAME/BepInEx/core/BepInEx.dll"); fi
if [ -f "$GAME/BepInEx/core/0Harmony.dll" ]; then REFS+=("-r:$GAME/BepInEx/core/0Harmony.dll"); fi

if [ -f "$OUT" ]; then
  BAK="$OUT.$(date +%Y%m%d-%H%M%S).bak"
  cp -f "$OUT" "$BAK" && echo "旧 dll 已备份 → $(basename "$BAK")"
fi

TMP="$OUT.tmp"
"$DOTNET" exec "$CSC" -nologo -target:library -nostdlib+ -langversion:9 -optimize+ \
  -out:"$TMP" "${REFS[@]}" "${SRC_FILES[@]}"
RC=$?

if [ $RC -ne 0 ]; then
  echo "✗ 编译失败（exit $RC）" >&2
  rm -f "$TMP"
  exit $RC
fi

mv -f "$TMP" "$OUT"
echo "✓ 编译成功：$OUT"
echo "  $(stat -c%s "$OUT") bytes  md5=$(md5sum "$OUT" | cut -c1-8)"
