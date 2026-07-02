#!/usr/bin/env python3
"""文本文件编码检查工具（v0.22.0）。

策略：先 UTF-8（兼容 BOM）解码，失败再 GBK 解码；两次都失败标 UNKNOWN。
仅在 `--fix` 显式传入时把 GBK 嫌疑文件以 GBK 读 UTF-8 写回，保留原换行符、不写 BOM。

退出码：扫描结束若存在 GBK 嫌疑或 UNKNOWN 则非零；`--fix` 后复扫通过则 0。

设计要点见 `DevDocs/v0.22.0/solution.md` §3.1。
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path
from typing import Iterable

UTF8_BOM = b"\xef\xbb\xbf"

# 本期默认扫描目录（参见 PRD §2.1、solution §3.1）。
DEFAULT_PATHS: tuple[str, ...] = (
    "Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject",
    "Tools",
    "DevDocs",
    "Doc",
    "AGENTS.md",
    "README.md",
)

# 本期默认扩展名：暂不含 .py（Python 业务源码下一版再讨论）。
DEFAULT_EXTS: tuple[str, ...] = (
    ".cs",
    ".md",
    ".proto",
    ".json",
    ".yml",
    ".yaml",
    ".txt",
)

# 默认排除目录（路径前缀匹配，相对仓库根）。
DEFAULT_EXCLUDES: tuple[str, ...] = (
    ".git",
    "Src/IndependentAgentProject/Library",
    "Src/IndependentAgentProject/Temp",
    "Src/IndependentAgentProject/obj",
    "Src/IndependentAgentProject/Build",
    "Src/IndependentAgentProject/Assets/Scripts/ShootingEditor2D",
    "Src/PythonServer/.venv",
    "Src/PythonServer/db",
    "Src/PythonServer/logs",
    "Src/PythonServer",  # 本期 Python 业务源码暂不治理
)

STATUS_OK = "OK"
STATUS_BOM = "UTF8-BOM"
STATUS_GBK = "GBK"
STATUS_UNKNOWN = "UNKNOWN"


def classify(data: bytes) -> str:
    """三段式判定文件编码状态。"""
    if data.startswith(UTF8_BOM):
        try:
            data[len(UTF8_BOM):].decode("utf-8", "strict")
            return STATUS_BOM
        except UnicodeDecodeError:
            pass
    try:
        data.decode("utf-8", "strict")
        return STATUS_OK
    except UnicodeDecodeError:
        pass
    try:
        data.decode("gbk", "strict")
        return STATUS_GBK
    except UnicodeDecodeError:
        return STATUS_UNKNOWN


def fix_gbk_to_utf8(path: Path) -> None:
    """把一个 GBK 嫌疑文件以 GBK 解码后 UTF-8 写回，保留原换行符、不写 BOM。"""
    data = path.read_bytes()
    text = data.decode("gbk", "strict")
    path.write_bytes(text.encode("utf-8"))


def _norm(p: str) -> str:
    return p.replace("\\", "/").rstrip("/")


def _is_excluded(rel_path: str, excludes: tuple[str, ...]) -> bool:
    rp = _norm(rel_path)
    for ex in excludes:
        ex = _norm(ex)
        if rp == ex or rp.startswith(ex + "/"):
            return True
    return False


def _match_ext(name: str, exts: tuple[str, ...]) -> bool:
    name_lower = name.lower()
    return any(name_lower.endswith(e.lower()) for e in exts)


def iter_files(
    roots: Iterable[Path],
    repo_root: Path,
    exts: tuple[str, ...],
    excludes: tuple[str, ...],
) -> Iterable[Path]:
    """遍历 roots 下匹配扩展名且未被 excludes 命中的文件。"""
    for root in roots:
        if not root.exists():
            continue
        if root.is_file():
            if not _match_ext(root.name, exts):
                continue
            rel = root.resolve().relative_to(repo_root).as_posix()
            if _is_excluded(rel, excludes):
                continue
            yield root
            continue
        for dirpath, dirnames, filenames in os.walk(root):
            dp = Path(dirpath).resolve()
            try:
                rel_dir = dp.relative_to(repo_root).as_posix()
            except ValueError:
                rel_dir = dp.as_posix()
            # 原地裁剪 dirnames，避免下钻被排除目录
            pruned: list[str] = []
            for d in list(dirnames):
                rel_child = (rel_dir + "/" + d).lstrip("./") if rel_dir not in (".", "") else d
                if _is_excluded(rel_child, excludes):
                    continue
                pruned.append(d)
            dirnames[:] = pruned
            if _is_excluded(rel_dir, excludes):
                continue
            for fn in filenames:
                if not _match_ext(fn, exts):
                    continue
                fp = Path(dirpath) / fn
                rel = fp.resolve().relative_to(repo_root).as_posix()
                if _is_excluded(rel, excludes):
                    continue
                yield fp


def scan(
    targets: Iterable[Path],
    repo_root: Path,
) -> list[tuple[str, Path, int]]:
    """返回 (status, path, size) 列表。"""
    results: list[tuple[str, Path, int]] = []
    for f in targets:
        try:
            data = f.read_bytes()
        except OSError as e:
            print(f"[ERROR] 无法读取 {f}: {e}", file=sys.stderr)
            continue
        status = classify(data)
        results.append((status, f, len(data)))
    return results


def _fmt_relative(p: Path, repo_root: Path) -> str:
    try:
        return p.resolve().relative_to(repo_root).as_posix()
    except ValueError:
        return str(p)


def _preview_bytes(path: Path, n: int = 200) -> str:
    try:
        data = path.read_bytes()[:n]
    except OSError:
        return ""
    return data.decode("utf-8", errors="replace")


def _print_results(
    results: list[tuple[str, Path, int]],
    repo_root: Path,
    verbose: bool,
) -> tuple[int, int, int, int]:
    ok = bom = gbk = unknown = 0
    gbk_items: list[tuple[Path, int]] = []
    unknown_items: list[tuple[Path, int]] = []
    for status, path, size in results:
        rel = _fmt_relative(path, repo_root)
        if status == STATUS_OK:
            ok += 1
            if verbose:
                print(f"[OK]       {rel} ({size} bytes)")
        elif status == STATUS_BOM:
            bom += 1
            if verbose:
                print(f"[UTF8-BOM] {rel} ({size} bytes)")
        elif status == STATUS_GBK:
            gbk += 1
            gbk_items.append((path, size))
        else:
            unknown += 1
            unknown_items.append((path, size))
    if gbk_items:
        print("--- GBK 嫌疑文件（可用 --fix 自动转换） ---")
        for path, size in gbk_items:
            rel = _fmt_relative(path, repo_root)
            print(f"[GBK]      {rel} ({size} bytes)")
            preview = _preview_bytes(path)
            if preview:
                print(f"           预览(前200字节, UTF-8 替换): {preview!r}")
    if unknown_items:
        print("--- 未知编码文件（需人工确认，--fix 不会改） ---")
        for path, size in unknown_items:
            rel = _fmt_relative(path, repo_root)
            print(f"[UNKNOWN]  {rel} ({size} bytes)")
    print(f"汇总: OK={ok} UTF8-BOM={bom} GBK嫌疑={gbk} 未知={unknown}")
    return ok, bom, gbk, unknown


def _resolve_repo_root() -> Path:
    """优先用 git 检测当前仓库根；失败回退到脚本所在目录的父目录。

    这样脚本可以从任意位置运行（包括被 hooks 调用、被外部仓库引用时）。
    用 bytes + utf-8 解码避免 Windows GBK 控制台下因路径含中文报 UnicodeDecodeError。
    """
    try:
        out_bytes = subprocess.check_output(
            ["git", "rev-parse", "--show-toplevel"],
            stderr=subprocess.DEVNULL,
        )
        out = out_bytes.decode("utf-8", errors="replace").strip()
        if out:
            return Path(out).resolve()
    except (subprocess.CalledProcessError, FileNotFoundError):
        pass
    return Path(__file__).resolve().parent.parent


def _staged_files(repo_root: Path) -> list[Path]:
    try:
        out_bytes = subprocess.check_output(
            ["git", "diff", "--cached", "--name-only", "--diff-filter=ACMRT"],
            cwd=repo_root,
        )
    except (subprocess.CalledProcessError, FileNotFoundError):
        return []
    out = out_bytes.decode("utf-8", errors="replace")
    paths: list[Path] = []
    for line in out.splitlines():
        line = line.strip()
        if not line:
            continue
        p = (repo_root / line).resolve()
        if p.is_file():
            paths.append(p)
    return paths


def _force_utf8_console() -> None:
    """在 Windows GBK 控制台下也用 UTF-8 输出，避免脚本自身的中文与 U+FFFD 编码失败。"""
    for stream_name in ("stdout", "stderr"):
        stream = getattr(sys, stream_name, None)
        if stream is None:
            continue
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is None:
            continue
        try:
            reconfigure(encoding="utf-8", errors="replace")
        except Exception:  # noqa: BLE001
            pass


def main(argv: list[str] | None = None) -> int:
    _force_utf8_console()
    parser = argparse.ArgumentParser(
        description="检查并修复仓库内文本文件的字符编码（v0.22.0）。",
    )
    parser.add_argument(
        "paths",
        nargs="*",
        help="要扫描的文件或目录（默认见脚本 DEFAULT_PATHS）。",
    )
    parser.add_argument(
        "--ext",
        default=",".join(DEFAULT_EXTS),
        help="扩展名列表（逗号分隔，默认见 DEFAULT_EXTS）。",
    )
    parser.add_argument(
        "--exclude",
        action="append",
        default=None,
        help="追加排除路径前缀（可多次指定）。默认值见 DEFAULT_EXCLUDES。",
    )
    parser.add_argument(
        "--no-default-exclude",
        action="store_true",
        help="不使用 DEFAULT_EXCLUDES，仅使用 --exclude 显式给出的。",
    )
    parser.add_argument(
        "--fix",
        action="store_true",
        help="把 GBK 嫌疑文件以 UTF-8 写回（不改 UNKNOWN）。",
    )
    parser.add_argument(
        "--staged",
        action="store_true",
        help="只校验 git 当前 staged 文件，并尊重 --ext / --exclude。",
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="打印 OK / UTF8-BOM 文件清单。",
    )
    args = parser.parse_args(argv)

    repo_root = _resolve_repo_root()
    exts = tuple(e.strip() for e in args.ext.split(",") if e.strip())
    extra_excludes = tuple(args.exclude or ())
    if args.no_default_exclude:
        excludes = extra_excludes
    else:
        excludes = DEFAULT_EXCLUDES + extra_excludes

    if args.staged:
        targets = []
        for p in _staged_files(repo_root):
            if not _match_ext(p.name, exts):
                continue
            try:
                rel = p.resolve().relative_to(repo_root).as_posix()
            except ValueError:
                continue
            if _is_excluded(rel, excludes):
                continue
            targets.append(p)
    else:
        if args.paths:
            roots = [Path(p) for p in args.paths]
        else:
            roots = [repo_root / p for p in DEFAULT_PATHS]
        targets = list(iter_files(roots, repo_root, exts, excludes))

    if not targets:
        print("（没有匹配的文件需要检查）")
        return 0

    results = scan(targets, repo_root)

    if args.fix:
        to_fix = [path for status, path, _ in results if status == STATUS_GBK]
        for path in to_fix:
            try:
                fix_gbk_to_utf8(path)
                print(f"[FIXED]    {_fmt_relative(path, repo_root)}")
            except (UnicodeDecodeError, OSError) as e:
                print(
                    f"[ERROR] 修复失败 {_fmt_relative(path, repo_root)}: {e}",
                    file=sys.stderr,
                )
        # 复扫
        results = scan(targets, repo_root)

    ok, bom, gbk, unknown = _print_results(results, repo_root, args.verbose)
    if gbk + unknown > 0:
        if not args.fix:
            print(
                "提示: 运行 `python Tools/check_file_encoding.py --fix <path>` 自动修复 GBK 嫌疑文件。",
                file=sys.stderr,
            )
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
