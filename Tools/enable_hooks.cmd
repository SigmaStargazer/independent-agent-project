@echo off
REM 启用本仓库 pre-commit 编码检查（v0.22.0）。
pushd "%~dp0\.."
git config core.hooksPath Tools/hooks
if errorlevel 1 (
    echo [ERR] git config core.hooksPath 失败，请确认当前目录在仓库内。
    popd
    exit /b 1
)
echo [OK] core.hooksPath -^> Tools/hooks
echo [OK] pre-commit 编码检查已启用。可用 "git config --unset core.hooksPath" 关闭。
popd
