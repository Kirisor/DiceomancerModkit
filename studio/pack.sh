#!/usr/bin/env bash
# 打包发布物：ModKit 平台包 / ModKit Studio 包 / 示例 mod
#
# 用法：bash studio/pack.sh [版本号]
# 产出：<仓库根>/dist/
#
# 说明：
#   · 平台包只含运行必需文件（4 个 dll + 配置 + 许可），不含任何游戏素材；
#   · Studio 包为 win-x64 自包含（玩家无需安装 .NET），附带字典类文档；
#   · 所有 zip 附带 SHA256SUMS.txt。

set -eu

. "$(cd "$(dirname "$0")/.." && pwd)/tools/env.sh"

VERSION="${1:-0.1.0}"
SELF_CONTAINED="${SELF_CONTAINED:-0}"   # 1 = 自包含（需下载 .NET 运行时包）；0 = 框架依赖
WITH_BEPINEX="${WITH_BEPINEX:-0}"
EXAMPLE_SRC="${EXAMPLE_SRC:-}"    # 示例 mod 源目录；留空则用游戏目录里的 naifrog       # 1 = 把 BepInEx 也打进 Studio 的 payload（供一键部署安装）
ROOT="$MODKIT_ROOT"
GAME="$DICEOMANCER_DIR"
DIST="$ROOT/dist"
STAGE="$DIST/stage"

rm -rf "$DIST"
mkdir -p "$DIST" "$STAGE"

# 打包用 Python（需要 zipfile）；没有 python 时提示显式指定 PY=<python 路径>
if [ -z "${PY:-}" ]; then
  for cand in python python3 "$HOME/.dsh/dsh-runtimes/dsh-primary-runtime/dependencies/python/python.exe"; do
    if "$cand" -c "import sys" >/dev/null 2>&1; then
      PY="$cand"
      break
    fi
  done
fi
if [ -z "${PY:-}" ]; then
  echo "✗ 找不到 python：设 PY=<python 可执行文件路径> 后重试" >&2
  exit 1
fi

zip_dir () {  # zip_dir <输出zip> <源目录>
  "$PY" - "$1" "$2" <<'PY'
import os, sys, zipfile
out, src = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for dirpath, _dirs, files in os.walk(src):
        for f in files:
            full = os.path.join(dirpath, f)
            rel = os.path.relpath(full, src)
            z.write(full, rel)
print("  zip: %s (%.1f MB)" % (os.path.basename(out), os.path.getsize(out) / 1048576))
PY
}

# ============================================================
echo "=== 1/3 ModKit 平台包 ==="
P="$STAGE/ModKit-Platform"
mkdir -p "$P/BepInEx/plugins" "$P/BepInEx/config"

for f in ModKit.Api.dll ModKit.Core.dll ModKit.Game.dll ModKit.Plugin.dll; do
  if [ ! -f "$GAME/BepInEx/plugins/$f" ]; then
    echo "  ✗ 缺少 $f（请先安装平台 dll 到 $GAME/BepInEx/plugins）" >&2
    exit 1
  fi
  cp -f "$GAME/BepInEx/plugins/$f" "$P/BepInEx/plugins/"
  printf "  + %-20s %s\n" "$f" "$(du -h "$GAME/BepInEx/plugins/$f" | cut -f1)"
done

cp -f "$GAME/BepInEx/config/diceomancer.modkit.cfg" "$P/BepInEx/config/"
cp -f "$GAME/BepInEx/LICENSE" "$GAME/BepInEx/NOTICE" "$P/BepInEx/"

cat > "$P/安装说明.txt" <<'TXT'
ModKit 平台包（DICEOMANCER）
============================

前置：先安装 BepInEx 5.4.23.2 x64（解压到游戏根目录，与 Diceomancer.exe 同级）。

安装：
  1. 把本包里的 BepInEx\plugins\ModKit.*.dll 四个文件
     复制到  <游戏根>\BepInEx\plugins\
  2. （可选）把 BepInEx\config\diceomancer.modkit.cfg 复制到 <游戏根>\BepInEx\config\
  3. mod 内容放到  <游戏根>\BepInEx\DiceomancerMods\<modid>\
  4. 启动游戏

验证：<游戏根>\BepInEx\LogOutput.log 里搜 [ModKit]，应出现
      "mod 已加载：<modid>：卡 N / 遗物 M / 关键词 K"

注意：四个 dll 必须一起装，少一个平台不工作。
TXT

zip_dir "$DIST/ModKit-Platform-$VERSION.zip" "$P"

# ============================================================
echo
echo "=== 2/3 ModKit Studio 包（win-x64 自包含）==="
S="$STAGE/ModKit-Studio"
mkdir -p "$S"
cd "$ROOT/studio"
echo "  发布中（首次会下载运行时包，可能较慢）…"
if [ "$SELF_CONTAINED" = "1" ]; then
  echo "  自包含模式（会下载 .NET 运行时包，首次较慢）…"
  # 自包含：走离线脚本（curl 直下运行时包，绕开 NuGet 客户端）
  if ! bash "$ROOT/studio/pack-sc-offline.sh" > "$DIST/publish.log" 2>&1; then
    echo "  ✗ 自包含发布失败，详情见 dist/publish.log" >&2
    tail -12 "$DIST/publish.log" | sed 's/^/    /' >&2
    exit 1
  fi
  tail -3 "$DIST/publish.log" | sed 's/^/    /'
else
  echo "  框架依赖模式（玩家需安装 .NET 10 Desktop Runtime；要自包含用 SELF_CONTAINED=1）…"
  if ! "$DOTNET" publish ModKit.Studio.csproj -c Release --self-contained false \
    -p:DebugType=none -o "$S" > "$DIST/publish.log" 2>&1; then
    echo "  ✗ 发布失败，详情见 dist/publish.log" >&2
    tail -12 "$DIST/publish.log" | sed 's/^/    /' >&2
    exit 1
  fi
  tail -3 "$DIST/publish.log" | sed 's/^/    /'
fi

if [ ! -f "$S/ModKit.Studio.exe" ]; then echo "  ✗ 没产出 ModKit.Studio.exe" >&2; exit 1; fi
# 校验 Studio 产物
echo "  产物: ModKit.Studio.exe $(du -h "$S/ModKit.Studio.exe" | cut -f1)｜目录 $(du -sh "$S" | cut -f1)"

mkdir -p "$S/docs"
for d in "ModKit-作者手册.md" "ModKit-Studio使用手册.md" "ModKit-效果字典.md"; do
  [ -f "$ROOT/docs/$d" ] && cp -f "$ROOT/docs/$d" "$S/docs/"
done

cat > "$S/使用说明.txt" <<'TXT'
ModKit Studio（DICEOMANCER mod 编辑器）
=======================================

本包为 Windows x64 自包含版本，无需安装 .NET。

首次启动：
  1. 双击 ModKit.Studio.exe
  2. 若自动探测的游戏目录不对，点顶栏「浏览…」选游戏根目录（含 Diceomancer_Data 的那层）
  3. 点「重新加载」，左侧出现你的 mod

需要先安装：BepInEx + ModKit 平台包（否则做的 mod 没地方生效）。
编辑 C# 行为类时需要另行安装 .NET SDK。

文档在 docs\ 目录：作者手册、Studio 使用手册、效果字典。
TXT

# ---- 一键部署用的 payload：平台四件套 + 配置（可选 BepInEx）----
PAY="$S/payload"
mkdir -p "$PAY/plugins" "$PAY/config"
for f in ModKit.Api.dll ModKit.Core.dll ModKit.Game.dll ModKit.Plugin.dll; do
  cp -f "$P/BepInEx/plugins/$f" "$PAY/plugins/"
done
cp -f "$P/BepInEx/config/diceomancer.modkit.cfg" "$PAY/config/" 2>/dev/null || true
if [ "$WITH_BEPINEX" = "1" ]; then
  if [ -f "$GAME/BepInEx/core/BepInEx.dll" ]; then
    # 镜像安装布局：root/ → 游戏根；BepInEx/ → 游戏根\BepInEx
    mkdir -p "$PAY/bepinex/root" "$PAY/bepinex/BepInEx/core"
    cp -f "$GAME/winhttp.dll" "$GAME/doorstop_config.ini" "$PAY/bepinex/root/" 2>/dev/null || true
    cp -f "$GAME/BepInEx/core/"*.dll "$PAY/bepinex/BepInEx/core/"
    cp -f "$GAME/BepInEx/LICENSE" "$GAME/BepInEx/NOTICE" "$PAY/bepinex/BepInEx/" 2>/dev/null || true
    echo "  payload: 含 BepInEx（$(du -sh "$PAY/bepinex" | cut -f1)）"
  else
    echo "  ! 本机没有 BepInEx，payload 不带 BepInEx"
  fi
else
  echo "  payload: 仅平台四件套（要带 BepInEx 用 WITH_BEPINEX=1）"
fi
echo "  payload 大小: $(du -sh "$PAY" | cut -f1)"

zip_dir "$DIST/ModKit-Studio-$VERSION-win-x64.zip" "$S"

# ============================================================
echo
echo "=== 3/3 奶蛙 mod ==="
E="$STAGE/example-mod"
MOD="${EXAMPLE_SRC:-$GAME/BepInEx/DiceomancerMods/naifrog}"
if [ -d "$MOD" ]; then
  mkdir -p "$E/naifrog"
  # 清单里声明的每一类文件都必须在这里，少一类就会「包能装、内容缺、游戏里 NRE」：
  # abilities/ 漏掉时，引用能力的卡（如以母之名）实例化直接空引用、图鉴只剩缩略图。
  for item in mod.json cards relics keywords abilities i18n colors.json art sfx src naifrog.dll; do
    [ -e "$MOD/$item" ] && cp -r "$MOD/$item" "$E/naifrog/" 2>/dev/null || true
  done
  # 只剔除备份与笔记；dll/src 是职业与关键词行为的实现，必须保留（否则没有职业球）
  find "$E" -name "*.bak" -delete 2>/dev/null || true
  find "$E" -name "*.pdb" -delete 2>/dev/null || true
  # 清单完整性：mod.json 声明了但包内不存在的文件，直接判打包失败，避免发布残包
  if ! "$PY" - "$E/naifrog" <<'PY'
import json, os, sys
root = sys.argv[1]
with open(os.path.join(root, "mod.json"), encoding="utf-8") as fh:
    manifest = json.load(fh)
missing, total = [], 0
for key in ("cards", "relics", "keywords", "i18n", "abilities"):
    for rel in manifest.get(key, []):
        total += 1
        if not os.path.exists(os.path.join(root, rel)):
            missing.append(rel)
if missing:
    print("  [FAIL] missing declared files: " + ", ".join(missing))
    sys.exit(1)
print("  [OK] manifest: %d declared files all present" % total)
PY
  then
    exit 1
  fi
  if [ ! -f "$E/naifrog/naifrog.dll" ]; then
    echo "  ! mod 缺少 naifrog.dll —— 职业球不会出现"
  fi
  # mod 版本号取 mod.json 自己的 version（与平台版本无关）
  MOD_VER="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$MOD/mod.json" | head -1)"
  [ -n "$MOD_VER" ] || MOD_VER="0.0.0"
  if [ "$(find "$E" -type f | wc -l)" -eq 0 ]; then
    echo "  ! mod 源目录为空，跳过（用 EXAMPLE_SRC=<目录> 指定）"
  else
    zip_dir "$DIST/Naifrog-Mod-$MOD_VER.zip" "$E"
  fi
else
  echo "  （没有 naifrog mod 可打包）"
fi

# ============================================================
echo
echo "=== 校验和 ==="
cd "$DIST"
for z in *.zip; do
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$z"
  else
    "$PY" -c "import sys,hashlib;d=open(sys.argv[1],'rb').read();print(hashlib.sha256(d).hexdigest()+'  '+sys.argv[1])" "$z"
  fi
done | tee SHA256SUMS.txt | sed 's/^/  /'

rm -rf "$STAGE"
echo
echo "=== 产物 ==="
ls -lh "$DIST" | awk 'NR>1{printf "  %-42s %s\n", $9, $5}'
