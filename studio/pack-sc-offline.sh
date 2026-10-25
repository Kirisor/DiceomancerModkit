#!/usr/bin/env bash
# 绕开 NuGet 客户端：手动用 curl 下载运行时包，再从本地目录还原 + 自包含发布
set -u

. "$(cd "$(dirname "$0")/.." && pwd)/tools/env.sh"

LOCAL="$MODKIT_ROOT/nuget-local"
OUT="$MODKIT_ROOT/dist/stage/ModKit-Studio"
RESTORE_LOG="$MODKIT_ROOT/restore.log"
PUBLISH_LOG="$MODKIT_ROOT/publish-sc.log"
VER="10.0.12"
BASE="https://api.nuget.org/v3-flatcontainer"

mkdir -p "$LOCAL"

pkgs="
microsoft.netcore.app.runtime.win-x64
microsoft.windowsdesktop.app.runtime.win-x64
microsoft.net.illink.tasks
microsoft.netcore.app.host.win-x64
"

echo "=== 1) 手动下载运行时包（curl 跟随重定向）==="
ok=1
for p in $pkgs; do
  f="$LOCAL/$p.$VER.nupkg"
  if [ -s "$f" ]; then
    echo "  已存在: $p ($(du -h "$f" | cut -f1))"
    continue
  fi
  printf "  下载 %-46s " "$p"
  if curl -sL -m 180 -o "$f" "$BASE/$p/$VER/$p.$VER.nupkg"; then
    echo "$(du -h "$f" | cut -f1)"
  else
    echo "失败"; ok=0
  fi
done
[ "$ok" = "1" ] || { echo "✗ 有包下载失败"; exit 1; }
echo "  本地目录: $(du -sh "$LOCAL" | cut -f1)"

echo
echo "=== 2) 还原（本地目录 + 官方源）==="
cd "$MODKIT_ROOT/studio"
if ! "$DOTNET" restore ModKit.Studio.csproj -r win-x64 \
      -p:RestoreSources="$LOCAL" \
      -p:RestoreDisableParallel=true \
      > "$RESTORE_LOG" 2>&1; then
  echo "  ✗ 还原失败"
  grep -E "error|错误" "$RESTORE_LOG" | head -8 | sed 's/^/    /'
  exit 1
fi
echo "  ✓ 还原成功"

echo
echo "=== 3) 自包含发布（不再联网）==="
rm -rf "$OUT"
if ! "$DOTNET" publish ModKit.Studio.csproj -c Release -r win-x64 --self-contained true \
      --no-restore \
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:DebugType=none -o "$OUT" > "$PUBLISH_LOG" 2>&1; then
  echo "  ✗ 发布失败"
  tail -12 "$PUBLISH_LOG" | sed 's/^/    /'
  exit 1
fi

if [ -f "$OUT/ModKit.Studio.exe" ]; then
  echo "  ✓ 产出: ModKit.Studio.exe $(du -h "$OUT/ModKit.Studio.exe" | cut -f1)｜目录 $(du -sh "$OUT" | cut -f1)"
else
  echo "  ✗ 没有 exe"; exit 1
fi
