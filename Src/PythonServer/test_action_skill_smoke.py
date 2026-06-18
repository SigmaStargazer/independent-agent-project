# -*- coding: utf-8 -*-
"""ActionSkillManager 快速 smoke 测试。

可直接运行：
    uv run python test_action_skill_smoke.py
"""
from __future__ import annotations

import asyncio
import os
import shutil
import sys
import tempfile
from types import MethodType

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

from memory_system import MemoryManager
from memory_system.action_skill_system import load_default_skills
from memory_system.action_skill_system.skill_model import ActionSkill, ActionSequenceTemplate
from memory_system.db_conn import DBConnectionService

TEST_ROOT = os.path.join(tempfile.gettempdir(), "action_skill_smoke_db")
GROUP_ID = "test_agent"
CURTIME = "2026-06-13 14:00"


def _configure_temp_db() -> None:
    if os.path.exists(TEST_ROOT):
        shutil.rmtree(TEST_ROOT, ignore_errors=True)
    os.makedirs(TEST_ROOT, exist_ok=True)
    dbsvc = DBConnectionService()
    dbsvc._db_root = TEST_ROOT
    dbsvc._db_name = "graphiti"
    MemoryManager()._backup_root = os.path.join(TEST_ROOT, "backups")


async def _patch_embedder() -> None:
    async def fake_embed(self, text: str) -> list[float]:
        if any(word in (text or "") for word in ("浮板", "过河", "平台")):
            return [1.0, 0.0]
        return [0.0, 1.0]

    MemoryManager().action_skill._embed = MethodType(fake_embed, MemoryManager().action_skill)


async def main():
    _configure_temp_db()
    try:
        await MemoryManager().initialize()
        await _patch_embedder()
        mgr = MemoryManager().action_skill

        print("\n=== T01 创建技能（含首个模板） ===")
        skill = ActionSkill(
            name="坐浮板过河",
            description="乘坐移动浮板越过深渊",
            content="详细的过河流程说明",
            templates=[ActionSequenceTemplate(
                name="近岸上浮板",
                description="平台停在近岸时使用",
                action_sequence_template=[{"action": "Move", "params": {"target": "{platform}"}}],
                usage_notes="务必等平台稳定再上",
            )],
        )
        await mgr.create_skill(GROUP_ID, skill, CURTIME)
        print("✓ 创建成功")

        print("\n=== T02 重名创建应失败 ===")
        try:
            skill2 = ActionSkill(
                name="坐浮板过河",
                description="重复",
                templates=[ActionSequenceTemplate(name="x", description="y")],
            )
            await mgr.create_skill(GROUP_ID, skill2, CURTIME)
            raise AssertionError("重名创建没有报错")
        except ValueError as e:
            print(f"✓ 报错符合预期：{e}")

        print("\n=== T03 追加模板 ===")
        await mgr.add_template(
            GROUP_ID,
            skill_name="坐浮板过河",
            template=ActionSequenceTemplate(
                name="远岸上浮板",
                description="平台停在远岸时使用",
                action_sequence_template=[{"action": "WaitTrigger"}],
                usage_notes="耐心等平台靠岸",
            ),
            curtime=CURTIME,
        )
        print("✓ 追加成功")

        print("\n=== T04 加载技能 ===")
        sk = await mgr.get_skill(GROUP_ID, "坐浮板过河")
        assert sk is not None
        assert len(sk.templates) == 2
        print(f"✓ {sk.name}: 模板数={len(sk.templates)}")

        print("\n=== T05 RAG 模板索引 ===")
        idx = await mgr.get_skill_index(GROUP_ID, query="看到浮板想过河", top_n=5)
        print(idx)
        assert idx.splitlines()[0].startswith("1. 模板：")
        assert "所属技能：[坐浮板过河]" in idx
        print("✓ 模板索引格式正确")

        print("\n=== T06 默认技能加载器 ===")
        default_path = os.path.join(os.path.dirname(__file__), "db", "default_skills", "default_2.yaml")
        skills = load_default_skills(path=default_path)
        assert skills
        print(f"✓ 默认技能数={len(skills)}")

        print("\n=== 清理 ===")
        await mgr.delete_skill(GROUP_ID, "坐浮板过河")
        print("✓ 删除成功")
    finally:
        await MemoryManager().close()
        await DBConnectionService().close()
        if os.path.exists(TEST_ROOT):
            shutil.rmtree(TEST_ROOT, ignore_errors=True)


if __name__ == "__main__":
    asyncio.run(main())
