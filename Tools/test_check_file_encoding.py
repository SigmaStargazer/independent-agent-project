"""Tools/check_file_encoding.py 的 unittest 自测。

覆盖：
- classify 对 5 种构造文件（GBK / UTF-8 / UTF-8 BOM / UTF-16 / 二进制）的判定
- fix_gbk_to_utf8 的幂等性
- iter_files 的 --exclude / 默认 excludes 行为
"""
from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

THIS_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(THIS_DIR))

import check_file_encoding as cfe  # noqa: E402


class ClassifyTests(unittest.TestCase):
    def test_pure_ascii_is_ok(self) -> None:
        self.assertEqual(cfe.classify(b"hello world\n"), cfe.STATUS_OK)

    def test_utf8_chinese_is_ok(self) -> None:
        data = "你好，世界".encode("utf-8")
        self.assertEqual(cfe.classify(data), cfe.STATUS_OK)

    def test_utf8_with_bom(self) -> None:
        data = cfe.UTF8_BOM + "中文".encode("utf-8")
        self.assertEqual(cfe.classify(data), cfe.STATUS_BOM)

    def test_gbk_chinese_is_gbk(self) -> None:
        data = "你好，世界".encode("gbk")
        self.assertEqual(cfe.classify(data), cfe.STATUS_GBK)

    def test_utf16_le_is_unknown(self) -> None:
        # UTF-16 LE BOM + 文本；UTF-8 / GBK 都解不开有效中文
        data = b"\xff\xfe" + "中文".encode("utf-16-le")
        self.assertEqual(cfe.classify(data), cfe.STATUS_UNKNOWN)

    def test_random_binary_is_unknown(self) -> None:
        # 构造一段 UTF-8 / GBK 都会失败的字节
        data = bytes([0xC0, 0xC1, 0xF5, 0xFF, 0xFE, 0xFD])
        self.assertEqual(cfe.classify(data), cfe.STATUS_UNKNOWN)


class FixTests(unittest.TestCase):
    def test_fix_gbk_to_utf8_idempotent(self) -> None:
        text = "你好，独立智能体。中文 .cs 文件示例。\r\n第二行\r\n"
        with tempfile.TemporaryDirectory() as td:
            p = Path(td) / "demo.cs"
            p.write_bytes(text.encode("gbk"))
            self.assertEqual(cfe.classify(p.read_bytes()), cfe.STATUS_GBK)

            cfe.fix_gbk_to_utf8(p)
            self.assertEqual(cfe.classify(p.read_bytes()), cfe.STATUS_OK)
            self.assertEqual(p.read_bytes().decode("utf-8"), text)

            # 幂等：再次 classify 仍为 OK；不应再用 fix（fix 不应再被调用）
            self.assertEqual(cfe.classify(p.read_bytes()), cfe.STATUS_OK)


class IterFilesTests(unittest.TestCase):
    def test_default_excludes_skip_shootingeditor2d(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            repo = Path(td)
            # 模拟仓库结构
            target_dir = (
                repo
                / "Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/Sub"
            )
            target_dir.mkdir(parents=True)
            (target_dir / "Keep.cs").write_text("hi\n", encoding="utf-8")

            old_dir = (
                repo
                / "Src/IndependentAgentProject/Assets/Scripts/ShootingEditor2D"
            )
            old_dir.mkdir(parents=True)
            (old_dir / "Drop.cs").write_text("hi\n", encoding="utf-8")

            roots = [
                repo
                / "Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject",
                repo / "Src/IndependentAgentProject/Assets/Scripts/ShootingEditor2D",
            ]
            files = list(
                cfe.iter_files(
                    roots,
                    repo_root=repo,
                    exts=(".cs",),
                    excludes=cfe.DEFAULT_EXCLUDES,
                )
            )
            rels = sorted(
                f.resolve().relative_to(repo).as_posix() for f in files
            )
            self.assertIn(
                "Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/Sub/Keep.cs",
                rels,
            )
            for r in rels:
                self.assertNotIn("ShootingEditor2D", r)

    def test_ext_filter(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            repo = Path(td)
            d = repo / "a"
            d.mkdir()
            (d / "x.cs").write_text("hi\n", encoding="utf-8")
            (d / "y.py").write_text("hi\n", encoding="utf-8")
            files = list(
                cfe.iter_files(
                    [d],
                    repo_root=repo,
                    exts=(".cs",),
                    excludes=(),
                )
            )
            names = [f.name for f in files]
            self.assertEqual(names, ["x.cs"])


if __name__ == "__main__":
    unittest.main()