#!/usr/bin/env bash
# 发布到 GitHub：推代码 + 推 tag（+ 有 gh CLI 时自动建 Release）
#
# 用法：bash /g/Modkit/studio/publish.sh
#
# 前置：
#   · 已登录 GitHub（HTTPS 用凭据管理器/Token，或 SSH 免密）
#   · 远端仓库默认分支为 main（本仓库本地分支叫 master，会推成 main）
#
# 注意：会用 --force 覆盖远端历史（0.1.0 是重写过的历史）。

set -eu

ROOT="/g/Modkit"
REMOTE_URL="${REMOTE_URL:-https://github.com/Kirisor/DiceomancerModkit.git}"
VERSION="v0.1.0"
DIST="$ROOT/dist"

cd "$ROOT"

echo "=== 1) 远端 ==="
if git remote | grep -qx origin; then
  git remote set-url origin "$REMOTE_URL"
else
  git remote add origin "$REMOTE_URL"
fi
git remote -v | sed 's/^/  /'

echo
echo "=== 2) 推送代码（master → main，强制覆盖）==="
git push -f origin master:main
echo "  ✓ 代码已推送"

echo
echo "=== 3) 推送 tag ==="
git push -f origin "$VERSION"
echo "  ✓ tag $VERSION 已推送"

echo
echo "=== 4) Release ==="
if command -v gh >/dev/null 2>&1; then
  if [ -f "$DIST/ModKit-Platform-0.1.0.zip" ]; then
    gh release delete "$VERSION" --yes >/dev/null 2>&1 || true
    gh release create "$VERSION" \
      "$DIST/ModKit-Platform-0.1.0.zip" \
      "$DIST/ModKit-Studio-0.1.0-win-x64.zip" \
      "$DIST/Example-Mod-0.1.0.zip" \
      "$DIST/SHA256SUMS.txt" \
      --title "v0.1.0 — 首个可发布版本" \
      --notes-file "$ROOT/CHANGELOG.md"
    echo "  ✓ Release 已创建"
  else
    echo "  ✗ dist/ 里没有发布包，先跑：SELF_CONTAINED=1 bash studio/pack.sh 0.1.0"
    exit 1
  fi
else
  echo "  未安装 gh CLI —— 请到网页手动建 Release："
  echo "    https://github.com/Kirisor/DiceomancerModkit/releases/new"
  echo "    tag 选 $VERSION，标题「v0.1.0 — 首个可发布版本」，描述粘 CHANGELOG.md 的 v0.1.0 段"
  echo "    附件上传 dist/ 下 4 个文件："
  ls -1 "$DIST"/*.zip "$DIST"/SHA256SUMS.txt 2>/dev/null | sed 's/^/      /'
fi

echo
echo "=== 完成 ==="
echo "  仓库: https://github.com/Kirisor/DiceomancerModkit"
