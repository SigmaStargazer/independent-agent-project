# -*- coding: utf-8 -*-
"""v0.21.1 记忆系统门面与动作技能模板 RAG 测试。

可直接运行：
    uv run python test_v021_1_memory_facade.py
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
from memory_system.action_skill_system import ActionSkillManager, load_default_skills
from memory_system.action_skill_system.skill_model import ActionSkill, ActionSequenceTemplate
from memory_system.db_conn import DBConnectionService

TEST_ROOT = os.path.join(tempfile.gettempdir(), "v021_1_memory_facade_db")
BACKUP_ROOT = os.path.join(TEST_ROOT, "backups")
GROUP = "test_v021_1_memory_facade".encode("utf-8").hex()
GROUP_SMALL = "test_v021_1_small".encode("utf-8").hex()
EMPTY_GROUP = "test_v021_1_empty".encode("utf-8").hex()
NOW = "2016-01-01 00:00:00"


def _remove_test_root() -> None:
    if os.path.exists(TEST_ROOT):
        shutil.rmtree(TEST_ROOT, ignore_errors=True)


def _configure_temp_db() -> None:
    os.makedirs(TEST_ROOT, exist_ok=True)
    dbsvc = DBConnectionService()
    dbsvc._db_root = TEST_ROOT
    dbsvc._db_name = "graphiti"
    MemoryManager()._backup_root = BACKUP_ROOT


def _fake_embed_vector(text: str) -> list[float]:
    text = text or ""
    if any(word in text for word in ("浮板", "平台", "过河", "陷阱", "对岸")):
        return [1.0, 0.0]
    return [0.0, 1.0]


async def _patch_embedder() -> None:
    async def fake_embed(self, text: str) -> list[float]:
        return _fake_embed_vector(text)

    MemoryManager().action_skill._embed = MethodType(fake_embed, MemoryManager().action_skill)


async def _create_default_skills(group_id: str) -> None:
    default_path = os.path.join(os.path.dirname(__file__), "db", "default_skills", "default_2.yaml")
    default_skills = load_default_skills(group_id=group_id, path=default_path)
    for skill_data in default_skills:
        await MemoryManager().action_skill.create_skill_from_dict(
            group_id=group_id,
            skill_data=skill_data,
            curtime=NOW,
        )


async def _create_noise_skills(group_id: str, count: int) -> None:
    for i in range(count):
        skill = ActionSkill(
            name=f"测试干扰技能{i}",
            description=f"用于测试排序的普通技能{i}",
            content="测试内容",
            templates=[
                ActionSequenceTemplate(
                    name=f"普通模板{i}",
                    description=f"普通平地场景{i}",
                    action_sequence_template=[{"action": "wait", "condition": f"flag_{i}"}],
                    usage_notes=f"普通注意事项{i}",
                )
            ],
        )
        await MemoryManager().action_skill.create_skill(group_id, skill, NOW)


def _assert_ordered_format(text: str) -> None:
    pos_template = text.find("模板：")
    pos_apply = text.find("适用：")
    pos_seq = text.find("动作序列：")
    pos_notes = text.find("使用注意：")
    pos_skill = text.find("所属技能：")
    assert 0 <= pos_template < pos_apply < pos_seq < pos_notes < pos_skill, text
    assert not text.splitlines()[0].startswith("[技能："), text


async def test_initialize_idempotent() -> None:
    mm1 = await MemoryManager().initialize()
    mm2 = await MemoryManager().initialize()
    assert mm1 is mm2
    assert MemoryManager().action_skill is ActionSkillManager()
    print("✓ TC-1 初始化幂等与门面单例")


async def test_facade_crud() -> None:
    skill = ActionSkill(
        name="测试技能",
        description="for-tests",
        content="测试内容",
        templates=[
            ActionSequenceTemplate(
                name="测试模板",
                description="测试场景",
                action_sequence_template=[{"action": "wait"}],
                usage_notes="无",
            )
        ],
    )
    await MemoryManager().action_skill.create_skill(GROUP, skill, NOW)
    got = await MemoryManager().action_skill.get_skill(GROUP, "测试技能")
    assert got is not None
    assert got.templates and got.templates[0].name == "测试模板"
    await MemoryManager().action_skill.delete_skill(GROUP, "测试技能")
    print("✓ TC-2 门面属性 CRUD")


async def test_skill_index_template_dimension() -> None:
    await _create_default_skills(GROUP_SMALL)
    small_text = await MemoryManager().action_skill.get_skill_index(
        GROUP_SMALL, query="任意输入", top_n=5
    )
    assert small_text.count("模板：") == 2, small_text
    _assert_ordered_format(small_text)

    await _create_default_skills(GROUP)
    await _create_noise_skills(GROUP, 6)
    text = await MemoryManager().action_skill.get_skill_index(
        GROUP, query="看到浮板想过河", top_n=5
    )
    assert text.count("模板：") == 5, text
    assert "单浮板陷阱场景" in text.splitlines()[0], text
    _assert_ordered_format(text)
    print("✓ TC-3/TC-4 模板维度 top5 与渲染顺序")


async def test_skill_index_empty() -> None:
    text = await MemoryManager().action_skill.get_skill_index(
        EMPTY_GROUP, query="anything", top_n=5
    )
    assert text == ""
    print("✓ TC-5 空技能库返回空字符串")


async def test_backup_restore_delete_facade_alive() -> None:
    await MemoryManager().backup_memory(0)
    await MemoryManager().restore_memory(0)
    assert MemoryManager().action_skill is ActionSkillManager()
    _ = await MemoryManager().action_skill.get_all_skills(GROUP)
    await MemoryManager().delete_current_memory()
    _ = await MemoryManager().action_skill.get_all_skills(GROUP)
    print("✓ TC-6 backup/restore/delete 后门面仍可用")


async def main() -> None:
    _remove_test_root()
    _configure_temp_db()
    try:
        await MemoryManager().initialize()
        await _patch_embedder()
        await test_initialize_idempotent()
        await test_facade_crud()
        await test_skill_index_template_dimension()
        await test_skill_index_empty()
        await test_backup_restore_delete_facade_alive()
    finally:
        await MemoryManager().close()
        await DBConnectionService().close()
        _remove_test_root()


if __name__ == "__main__":
    asyncio.run(main())
