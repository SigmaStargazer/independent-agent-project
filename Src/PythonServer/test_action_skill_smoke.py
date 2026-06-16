# -*- coding: utf-8 -*-
"""ActionSkillManager 快速 smoke 测试。

不依赖 MemoryManager 完整初始化（embedder 也跳过），
直接用一个临时 Kuzu 数据库验证 schema、create、add_template、refine、delete 全流程。
"""
import asyncio
import os
import shutil
import gc
import sys

# 让脚本可独立运行
sys.path.insert(0, os.path.dirname(__file__))

import kuzu

from action_skill_system.action_skill_manager import ActionSkillManager
from action_skill_system.skill_model import ActionSkill, ActionSequenceTemplate

DB_PATH = "db/_smoke_skill"


async def main():
    if os.path.exists(DB_PATH):
        # 强制删除旧的 db；尝试 3 次以应对 Windows 文件锁
        for _ in range(3):
            try:
                shutil.rmtree(DB_PATH, ignore_errors=False)
                break
            except Exception:
                gc.collect()
                await asyncio.sleep(0.3)
        else:
            shutil.rmtree(DB_PATH, ignore_errors=True)
    os.makedirs("db", exist_ok=True)

    db = kuzu.Database(DB_PATH)
    conn = kuzu.AsyncConnection(db)

    mgr = ActionSkillManager()
    # 强制重置（避免之前测试残留）
    mgr.reset_for_reinitialize()
    await mgr.initialize(kuzu_conn=conn, embedder=None, memory_manager=None)

    group_id = "test_agent"
    curtime = "2026-06-13 14:00"

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
    await mgr.create_skill(group_id, skill, curtime)
    print("✓ 创建成功")

    print("\n=== T02 重名创建应失败 ===")
    try:
        skill2 = ActionSkill(
            name="坐浮板过河",
            description="重复",
            templates=[ActionSequenceTemplate(name="x", description="y")],
        )
        await mgr.create_skill(group_id, skill2, curtime)
        print("✗ 没报错")
    except ValueError as e:
        print(f"✓ 报错符合预期：{e}")

    print("\n=== T03 追加模板 ===")
    await mgr.add_template(
        group_id,
        skill_name="坐浮板过河",
        template=ActionSequenceTemplate(
            name="远岸上浮板",
            description="平台停在远岸时使用",
            action_sequence_template=[{"action": "WaitTrigger"}],
            usage_notes="耐心等平台靠岸",
        ),
        curtime=curtime,
    )
    print("✓ 追加成功")

    print("\n=== T04 同名模板应失败 ===")
    try:
        await mgr.add_template(
            group_id,
            skill_name="坐浮板过河",
            template=ActionSequenceTemplate(
                name="近岸上浮板", description="dup"),
            curtime=curtime,
        )
        print("✗ 没报错")
    except ValueError as e:
        print(f"✓ 报错符合预期：{e}")

    print("\n=== T05 追加到不存在的技能 ===")
    try:
        await mgr.add_template(
            group_id, "不存在的技能",
            ActionSequenceTemplate(name="t", description="d"),
            curtime,
        )
        print("✗ 没报错")
    except ValueError as e:
        print(f"✓ 报错符合预期：{e}")

    print("\n=== T06 加载技能 ===")
    sk = await mgr.get_skill(group_id, "坐浮板过河")
    assert sk is not None
    print(f"✓ {sk.name}: {sk.description}, version={sk.version}, "
          f"模板数={len(sk.templates)}")
    for t in sk.templates:
        print(f"   - {t.name}: {t.description}")

    print("\n=== T07 加载不存在的技能 ===")
    sk_none = await mgr.get_skill(group_id, "xxx")
    print(f"✓ 返回 None: {sk_none is None}")

    print("\n=== T08 列出所有技能 ===")
    print(await mgr.get_skill_list(group_id))

    print("\n=== T10 精进技能说明 ===")
    await mgr.refine_skill(
        group_id, "坐浮板过河", "2026-06-13 15:00",
        new_content="更新后的说明",
    )
    sk = await mgr.get_skill(group_id, "坐浮板过河")
    print(f"✓ content='{sk.content}', version={sk.version}, source={sk.source}")
    assert sk.version == 2
    assert sk.content == "更新后的说明"

    print("\n=== T11 精进模板 ===")
    await mgr.refine_skill(
        group_id, "坐浮板过河", "2026-06-13 16:00",
        template_name="近岸上浮板",
        new_template=[{"action": "MoveV2", "params": {"target": "{p}"}}],
        new_usage_notes="更新后的注意",
    )
    sk = await mgr.get_skill(group_id, "坐浮板过河")
    print(f"✓ version={sk.version}")
    assert sk.version == 3
    for t in sk.templates:
        if t.name == "近岸上浮板":
            print(f"   action_sequence_template={t.action_sequence_template}")
            print(f"   usage_notes={t.usage_notes}")
            assert "MoveV2" in t.action_sequence_template[0]["action"]
            assert t.usage_notes == "更新后的注意"

    print("\n=== T12 删除模板（不是最后一个） ===")
    res = await mgr.delete_template(group_id, "坐浮板过河", "远岸上浮板")
    print(f"✓ {res}")
    assert res["deleted"] and not res["is_last"]

    print("\n=== T13 删除最后一个模板 ===")
    res = await mgr.delete_template(group_id, "坐浮板过河", "近岸上浮板")
    print(f"✓ {res}")
    assert res["deleted"] and res["is_last"]

    print("\n=== T14 删除整个技能 ===")
    await mgr.delete_skill(group_id, "坐浮板过河")
    sk = await mgr.get_skill(group_id, "坐浮板过河")
    assert sk is None
    print("✓ 删除成功")

    print("\n=== T15 从字典创建（默认技能注入） ===")
    await mgr.create_skill_from_dict(
        group_id,
        {
            "name": "走到目标交互",
            "description": "走到可交互物体旁并交互",
            "content": "详细流程",
            "source": "learned",  # 应该被强制改为 default
            "templates": [
                {
                    "name": "平地接近",
                    "description": "目标在平地",
                    "action_sequence_template": [{"action": "Move"}],
                    "usage_notes": "直接走过去",
                },
            ],
        },
        curtime="2026-06-13 17:00",
    )
    sk = await mgr.get_skill(group_id, "走到目标交互")
    print(f"✓ source={sk.source}（应为 default）")
    assert sk.source == "default"

    print("\n=== T20/T21 嵌入向量字段 ===")
    # 因为没接 embedder，应为空列表
    for t in sk.templates:
        print(f"   {t.name}: emb_len={len(t.description_embedding)}")

    print("\n=== T22 工具失败应抛 ValueError ===")
    try:
        await mgr.delete_skill(group_id, "已经不存在")
    except ValueError as e:
        print(f"✓ {e}")

    # =================================================================
    # 扩充用例 T09 / T16 / T17 / T18 / T22b / T23 / T24 / T25 / T26
    # （T19/T27 涉及 MemoryManager 的 backup / freeze，留待集成测试覆盖）
    # =================================================================

    print("\n=== T09 无技能时 list 为空 ===")
    empty_gid = "empty_agent"
    text = await mgr.get_skill_list(empty_gid)
    print(f"  空 agent 输出: {text}")
    assert "没有掌握任何技能" in text or text == "（你目前还没有掌握任何技能）"
    print("✓ 空列表提示符合预期")

    print("\n=== T16 导出 YAML（保留原 source） ===")
    export_gid = "export_agent"
    await mgr.create_skill(export_gid, ActionSkill(
        name="技能A", description="A", content="A详情", source="learned",
        templates=[ActionSequenceTemplate(name="t1", description="d1")],
    ), curtime)
    await mgr.refine_skill(export_gid, "技能A", curtime, new_content="再精进一下")  # 变 refined
    await mgr.create_skill_from_dict(export_gid, {
        "name": "技能B", "description": "B", "content": "B详情",
        "templates": [{"name": "t2", "description": "d2"}],
    }, curtime)
    yaml_text = await mgr.export_skills_yaml(export_gid)
    print(yaml_text)
    assert "source: refined" in yaml_text
    assert "source: default" in yaml_text
    print("✓ 导出保留原 source 值")

    print("\n=== T17 少量技能（≤ top_n）全量注入 ===")
    idx_text = await mgr.get_skill_index(export_gid, query="任何query", top_n=10)
    print(idx_text)
    # 两个技能都应在
    assert "技能A" in idx_text and "技能B" in idx_text
    print("✓ 全量注入")

    print("\n=== T18 技能数 > top_n，限制返回数量 ===")
    many_gid = "many_agent"
    for i in range(5):
        await mgr.create_skill(many_gid, ActionSkill(
            name=f"技能{i}", description=f"描述{i}", content=f"内容{i}",
            templates=[ActionSequenceTemplate(name=f"t{i}", description=f"d{i}")],
        ), curtime)
    idx_text = await mgr.get_skill_index(many_gid, query="测试", top_n=2)
    line_count = sum(1 for line in idx_text.split("\n") if line and line[0].isdigit() and line[1] == ".")
    print(f"  返回顶层技能数={line_count}（top_n=2）")
    print(idx_text)
    assert line_count <= 2
    print("✓ top_n 生效（无 embedder 时退化为前 N 个）")

    print("\n=== T22b add_template 不存在的技能 ===")
    try:
        await mgr.add_template(group_id, "不存在的技能X",
            ActionSequenceTemplate(name="t", description="d"), curtime)
        print("✗ 没报错")
    except ValueError as e:
        print(f"✓ {e}")

    print("\n=== T23 默认技能注入失败（坏 yaml） ===")
    from action_skill_system.default_skill_loader import load_default_skills
    bad_yaml = os.path.join(os.path.dirname(__file__), "_bad_default_skills.yaml")
    with open(bad_yaml, "w", encoding="utf-8") as f:
        f.write("skills: not_a_list\n")
    try:
        load_default_skills(bad_yaml)
        print("✗ 没报错")
    except ValueError as e:
        print(f"✓ 解析失败被抛出，main 中会记录日志：{e}")
    finally:
        os.remove(bad_yaml)

    print("\n=== T24 source 导入归一为 default ===")
    src_gid = "src_agent"
    await mgr.create_skill_from_dict(src_gid, {
        "name": "测试技能", "description": "x", "content": "y",
        "source": "learned",
        "templates": [{"name": "tt", "description": "dd"}],
    }, curtime)
    sk = await mgr.get_skill(src_gid, "测试技能")
    print(f"  source={sk.source}")
    assert sk.source == "default"
    print("✓ 导入时 source 强制为 default")

    print("\n=== T25/T26 RAG 召回与去重打分（模拟 embedder） ===")
    rag_gid = "rag_agent"
    # 注入一个假的 embedder：根据文本里关键词决定向量
    class FakeEmbedder:
        async def create(self, input: str):
            text = input or ""
            v = [
                1.0 if "浮板" in text else 0.0,
                1.0 if "敌人" in text else 0.0,
                1.0 if "宝箱" in text else 0.0,
                0.1,
            ]
            return v
    mgr._embedder = FakeEmbedder()
    # 创建 3 个技能，技能 A 下 2 个模板：1 高匹配 + 1 低匹配
    await mgr.create_skill(rag_gid, ActionSkill(
        name="过河",
        description="坐浮板过河",
        templates=[
            ActionSequenceTemplate(name="近岸", description="浮板停在近岸"),
            ActionSequenceTemplate(name="远岸", description="宝箱在远岸"),
        ],
    ), curtime)
    await mgr.create_skill(rag_gid, ActionSkill(
        name="打怪",
        description="打败敌人",
        templates=[ActionSequenceTemplate(name="近战", description="攻击敌人")],
    ), curtime)
    await mgr.create_skill(rag_gid, ActionSkill(
        name="开宝箱",
        description="打开宝箱获取奖励",
        templates=[ActionSequenceTemplate(name="标准", description="走到宝箱旁")],
    ), curtime)
    # 用 query 偏向"浮板"
    idx_text = await mgr.get_skill_index(rag_gid, query="我看到浮板", top_n=2)
    print(idx_text)
    # 第一名应是"过河"
    first_line = next((l for l in idx_text.split("\n") if l.startswith("1.")), "")
    assert "过河" in first_line, f"过河没排在第 1：{first_line}"
    print("✓ RAG 召回正确（浮板 → 过河 排第 1）")
    # 顶层技能数 ≤ 2
    line_count = sum(1 for l in idx_text.split("\n") if l and l[:2] in ("1.", "2.", "3."))
    assert line_count <= 2
    print(f"✓ top_n=2 生效（顶层技能数={line_count}）")
    mgr._embedder = None  # 还原

    print("\n\n[全部 smoke 测试通过] ✓")

    # 清理
    del conn
    del db
    gc.collect()
    if os.path.exists(DB_PATH):
        try:
            shutil.rmtree(DB_PATH)
        except Exception:
            pass


if __name__ == "__main__":
    asyncio.run(main())
