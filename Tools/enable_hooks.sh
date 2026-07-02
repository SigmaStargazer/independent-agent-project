#!/usr/bin/env bash
# 启用本仓库 pre-commit 编码检查（v0.22.0）。
set -e
cd "$(dirname "$0")/.."
git config core.hooksPath Tools/hooks
chmod +x Tools/hooks/pre-commit 2>/dev/null || true
echo "[OK] core.hooksPath -> Tools/hooks"
echo "[OK] pre-commit 编码检查已启用。可用 'git config --unset core.hooksPath' 关闭。"
