#!/usr/bin/env bash
# 发布到 GitHub：推代码 + 推 tag（+ 有 gh CLI 时自动建 Release）
#
# 用法：bash studio/publish.sh <tag>
#
# 前置：
#   · 已登录 GitHub（HTTPS 用凭据管理器/Token，或 SSH 免密）
#   · 远端仓库默认分支为 main
#
# 注意：会用 --force 覆盖远端分支历史（发布版本是重写过的历史）。

set -eu

. "$(cd "$(dirname "$0")/.." && pwd)/tools/env.sh"

ROOT="$MODKIT_ROOT"
REMOTE_URL="${REMOTE_URL:-https://github.com/Kirisor/DiceomancerModkit.git}"
VERSION="${1:-v0.1.0}"
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
echo "=== 2) 推送当前分支到 main（强制覆盖）==="
BRANCH="$(git rev-parse --abbrev-ref HEAD)"
git push -f origin "$BRANCH:main"
echo "  ✓ 代码已推送（$BRANCH → main）"

echo
echo "=== 3) 推送 tag ==="
git push -f origin "$VERSION"
echo "  ✓ tag $VERSION 已推送"

echo
echo "=== 4) Release ==="
if command -v gh >/dev/null 2>&1; then
  if ls "$DIST"/*.zip >/dev/null 2>&1; then
    gh release delete "$VERSION" --yes >/dev/null 2>&1 || true
    gh release create "$VERSION" \
      "$DIST"/*.zip \
      "$DIST/SHA256SUMS.txt" \
      --title "$VERSION" \
      --notes-file "$ROOT/CHANGELOG.md"
    echo "  ✓ Release 已创建"
  else
    echo "  ✗ dist/ 里没有发布包，先跑：SELF_CONTAINED=1 bash studio/pack.sh <版本号>"
    exit 1
  fi
else
  echo "  未安装 gh CLI —— 请到网页手动建 Release："
  echo "    https://github.com/Kirisor/DiceomancerModkit/releases/new"
  echo "    tag 选 $VERSION，标题与描述参考 CHANGELOG.md"
  echo "    附件上传 dist/ 下的 zip 与 SHA256SUMS.txt："
  ls -1 "$DIST"/*.zip "$DIST"/SHA256SUMS.txt 2>/dev/null | sed 's/^/      /'
fi

echo
echo "=== 完成 ==="
echo "  仓库: https://github.com/Kirisor/DiceomancerModkit"
