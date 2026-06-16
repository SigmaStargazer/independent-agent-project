# -*- coding: utf-8 -*-
"""真实 embedding 端到端验证脚本（T20/T21 + 真实 RAG 召回）。

跑这个脚本前需要：
- .env 中 EMBEDDING_* 配置正确
- MEMORY_* 配置正确（MemoryManager 初始化需要）

脚本流程：
1. MemoryManager().initialize() 完整初始化（含 ActionSkillManager 注入真实 embedder）
2. 用一个临时 group_id 创建 3 个技能
3. 验证模板的 description_embedding 真的有值且维度合理
4. 验证 refine_skill 修改 description 后 embedding 重算
5. 用真实 query 触发 RAG，验证召回顺序
6. 清理：删除临时技能 + 关闭 MemoryManager

使用：
    uv run python test_action_skill_real_embed.py
"""
import asyncio
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))

from memory_system.memory_manager import MemoryManager
from action_skill_system.action_skill_manager import ActionSkillManager
from action_skill_system.skill_model import ActionSkill, ActionSequenceTemplate


GROUP_ID = "real_embed_test"
CURTIME = "2026-06-13 17:00"


async def main():
    print("===== 初始化 MemoryManager（含真实 embedder） =====")
    mm = MemoryManager()
    await mm.initialize()
    mgr = ActionSkillManager()

    # 防止重复运行残留：先清空我们自己的 group_id 下的技能
    print("\n=== 清理上轮残留 ===")
    existing = await mgr.get_all_skills(GROUP_ID)
    for sk in existing:
        try:
            await mgr.delete_skill(GROUP_ID, sk.name)
            print(f"  清理旧技能: {sk.name}")
        except Exception as e:
            print(f"  清理 {sk.name} 失败: {e}")

    print("\n=== 创建 3 个技能（每个含 1 个模板） ===")
    skills_data = [
        {
            "name": "过河",
            "description": "通过移动浮板越过深渊",
            "templates": [("近岸上浮板", "浮板停在近岸时跳上去")],
        },
        {
            "name": "打怪",
            "description": "击败敌方角色",
            "templates": [("近战攻击", "靠近敌人后挥舞武器进行近战")],
        },
        {
            "name": "开宝箱",
            "description": "打开宝箱获取奖励",
            "templates": [("标准流程", "走到宝箱旁按下交互键")],
        },
    ]
    for sd in skills_data:
        skill = ActionSkill(
            name=sd["name"],
            description=sd["description"],
            content=f"{sd['name']} 的详细说明",
            templates=[
                ActionSequenceTemplate(name=tn, description=td)
                for tn, td in sd["templates"]
            ],
        )
        await mgr.create_skill(GROUP_ID, skill, CURTIME)
        print(f"  ✓ 创建技能 '{sd['name']}'")

    print("\n=== T20 嵌入向量字段非空 ===")
    skills = await mgr.get_all_skills(GROUP_ID)
    embed_dim = None
    for sk in skills:
        for t in sk.templates:
            n = len(t.description_embedding or [])
            print(f"  [{sk.name}] {t.name}: emb_len={n}")
            assert n > 0, f"{sk.name}/{t.name} 的 embedding 是空的！"
            if embed_dim is None:
                embed_dim = n
            else:
                assert embed_dim == n, "不同模板 embedding 维度不一致"
    print(f"✓ 所有模板 embedding 非空，维度={embed_dim}")

    print("\n=== T21 修改 description 后 embedding 重算 ===")
    sk = await mgr.get_skill(GROUP_ID, "过河")
    old_emb = sk.templates[0].description_embedding[:]
    await mgr.refine_skill(
        GROUP_ID, "过河", CURTIME,
        template_name="近岸上浮板",
        new_template_description="浮板靠岸时立刻跳上去（更新版描述）",
    )
    sk = await mgr.get_skill(GROUP_ID, "过河")
    new_emb = sk.templates[0].description_embedding
    assert len(new_emb) == embed_dim, "维度变了"
    same = all(abs(a - b) < 1e-9 for a, b in zip(old_emb, new_emb))
    if same:
        print("✗ embedding 没变（可能 cache 命中或没重算）")
    else:
        diff_count = sum(1 for a, b in zip(old_emb, new_emb) if abs(a - b) > 1e-9)
        print(f"✓ embedding 已重算（{diff_count}/{embed_dim} 个分量不同）")

    print("\n=== T25 真实 RAG 召回（query='浮板'） ===")
    idx = await mgr.get_skill_index(GROUP_ID, query="我看到一块浮板", top_n=2)
    print(idx)
    first_line = next((l for l in idx.split("\n") if l.startswith("1.")), "")
    if "过河" in first_line:
        print("✓ '浮板' 查询召回 '过河' 排第 1")
    else:
        print(f"⚠ '过河' 没排第 1：{first_line}")

    print("\n=== T25 真实 RAG 召回（query='敌人'） ===")
    idx = await mgr.get_skill_index(GROUP_ID, query="前面有一个敌人", top_n=2)
    print(idx)
    first_line = next((l for l in idx.split("\n") if l.startswith("1.")), "")
    if "打怪" in first_line:
        print("✓ '敌人' 查询召回 '打怪' 排第 1")
    else:
        print(f"⚠ '打怪' 没排第 1：{first_line}")

    print("\n=== T25 真实 RAG 召回（query='宝箱'） ===")
    idx = await mgr.get_skill_index(GROUP_ID, query="眼前出现一个宝箱", top_n=2)
    print(idx)
    first_line = next((l for l in idx.split("\n") if l.startswith("1.")), "")
    if "开宝箱" in first_line:
        print("✓ '宝箱' 查询召回 '开宝箱' 排第 1")
    else:
        print(f"⚠ '开宝箱' 没排第 1：{first_line}")

    print("\n=== 清理测试技能 ===")
    for sd in skills_data:
        try:
            await mgr.delete_skill(GROUP_ID, sd["name"])
            print(f"  删除: {sd['name']}")
        except Exception as e:
            print(f"  删除失败 {sd['name']}: {e}")

    print("\n[真实 embedding 端到端测试完成] ✓")
    # 不主动 close MemoryManager，避免影响后续脚本/手动操作


if __name__ == "__main__":
    asyncio.run(main())
