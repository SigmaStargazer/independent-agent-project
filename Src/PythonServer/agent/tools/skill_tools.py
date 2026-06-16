# -*- coding: utf-8 -*-
"""Action Skill 系统的 7 个 LangChain 工具。

设计原则（来自 PRD/AGENTS.md）：
- 工具描述面向"游戏世界中的角色"，文风自然、生活化
- 参数使用 skill_name / template_name，不暴露 uuid
- 失败时返回描述性错误字符串（不抛异常），便于 LLM 自主决策
- 写操作内部从 TimeSystem 取虚拟时间作为 created_at / updated_at
"""
from __future__ import annotations

import json
from typing import Annotated, List, Optional

from langchain_core.tools import tool
from langgraph.prebuilt import InjectedState

from action_skill_system.skill_model import ActionSkill, ActionSequenceTemplate
from action_skill_system.action_skill_manager import ActionSkillManager
from agent_framwork.systems.time_system import TimeSystem


# ----------------------------------------------------------------------
# 内部工具：参数解析
# ----------------------------------------------------------------------
def _name_to_group_id(name: str) -> str:
    """与 MemoryManager 一致：name.utf-8.hex"""
    return (name or "").encode("utf-8").hex()


def _parse_action_sequence(raw: str) -> List[dict]:
    """把 Agent 传入的 JSON 字符串解析为 List[dict]；失败抛 ValueError。"""
    if not raw or not raw.strip():
        return []
    try:
        data = json.loads(raw)
    except Exception as e:
        raise ValueError(
            f"action_sequence_template 不是合法 JSON：{e}。"
            f"应为形如 [{{\"action\":\"Move\",\"params\":{{...}}}}, ...] 的数组"
        )
    if not isinstance(data, list):
        raise ValueError("action_sequence_template 必须是 JSON 数组")
    return data


async def _curtime_str() -> str:
    """虚拟时间字符串，与 MemoryManager.save_memory 对齐。"""
    return await TimeSystem().aget_current_time(to_str=True)


# ----------------------------------------------------------------------
# 1. create_action_skill
# ----------------------------------------------------------------------
@tool
async def create_action_skill(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str,
    description: str,
    content: str,
    template_name: str,
    template_description: str,
    action_sequence_template: str,
    usage_notes: str,
) -> str:
    """将一种新的行为模式总结为技能，同时记录下第一个使用场景的动作序列模板。

    当你完成或中止了一次值得复用的动作序列后，可以用这个工具把它沉淀为可日后调用的技能。
    技能下至少需要一个动作序列模板。

    Args:
        skill_name(str): 技能的名称，例如"乘坐移动平台"
        description(str): 技能的简短描述（用于日后识别这个技能适用的场景）
        content(str): 技能的详细说明（介绍该技能的核心思路、关键步骤）
        template_name(str): 首个动作序列模板的名称，例如"近岸上浮板"
        template_description(str): 首个模板的描述（说明该模板适用的具体场景）
        action_sequence_template(str): 首个模板的动作序列模板（JSON 数组字符串，参数用 {占位符} 表示）
        usage_notes(str): 使用注意事项（场合、填参注意、过往经验总结）

    Returns:
        str: 创建结果说明
    """
    try:
        seq = _parse_action_sequence(action_sequence_template)
    except ValueError as e:
        return f"创建技能失败：{e}"

    group_id = _name_to_group_id(agent)
    curtime = await _curtime_str()

    tmpl = ActionSequenceTemplate(
        name=template_name,
        description=template_description,
        action_sequence_template=seq,
        usage_notes=usage_notes,
    )
    skill = ActionSkill(
        name=skill_name,
        description=description,
        content=content,
        source="learned",
        templates=[tmpl],
    )
    try:
        await ActionSkillManager().create_skill(group_id, skill, curtime)
    except ValueError as e:
        return f"创建技能失败：{e}"
    except Exception as e:
        return f"创建技能时发生异常：{e}"

    return (
        f"已将'{skill_name}'记入你的动作技能记忆，并保存了首个模板'{template_name}'。"
        f"日后遇到相似场景时，你会在动作技能记忆中看到它。"
    )


# ----------------------------------------------------------------------
# 2. add_action_skill_template
# ----------------------------------------------------------------------
@tool
async def add_action_skill_template(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str,
    template_name: str,
    template_description: str,
    action_sequence_template: str,
    usage_notes: str,
) -> str:
    """为你已经掌握的某个技能，添加一个新的使用场景下的动作序列模板。

    当你发现已有技能在某个新场景下需要不同的动作序列时使用。

    Args:
        skill_name(str): 已经掌握的技能名称
        template_name(str): 新模板的名称（同一技能下不可重复）
        template_description(str): 新模板的描述
        action_sequence_template(str): 新模板的动作序列模板（JSON 数组字符串）
        usage_notes(str): 使用注意事项

    Returns:
        str: 操作结果说明
    """
    try:
        seq = _parse_action_sequence(action_sequence_template)
    except ValueError as e:
        return f"添加模板失败：{e}"

    group_id = _name_to_group_id(agent)
    curtime = await _curtime_str()
    tmpl = ActionSequenceTemplate(
        name=template_name,
        description=template_description,
        action_sequence_template=seq,
        usage_notes=usage_notes,
    )
    try:
        await ActionSkillManager().add_template(group_id, skill_name, tmpl, curtime)
    except ValueError as e:
        return f"添加模板失败：{e}"
    except Exception as e:
        return f"添加模板时发生异常：{e}"
    return f"已为技能'{skill_name}'添加新模板'{template_name}'。"


# ----------------------------------------------------------------------
# 3. load_action_skill
# ----------------------------------------------------------------------
@tool
async def load_action_skill(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str,
) -> str:
    """回想某个技能的完整细节，包括所有使用场景下的动作序列模板。

    当你在动作技能记忆中看到匹配场景的技能名称后，调用此工具拉出完整内容，
    再选择最合适的模板替换占位参数后通过 plan_action_sequence 执行。

    Args:
        skill_name(str): 要回想的技能名称

    Returns:
        str: 技能详情（含 content、所有模板的 description / 动作序列 / usage_notes）
    """
    group_id = _name_to_group_id(agent)
    try:
        skill = await ActionSkillManager().get_skill(group_id, skill_name)
    except Exception as e:
        return f"回想技能时发生异常：{e}"
    if skill is None:
        return f"你并不记得名为'{skill_name}'的技能。"

    lines = [
        f"【技能】{skill.name}",
        f"描述：{skill.description}",
        f"详细说明：{skill.content}",
        f"精进次数：{skill.version - 1}",
        f"模板数：{len(skill.templates)}",
        "",
    ]
    for i, t in enumerate(skill.templates, 1):
        lines.append(f"--- 模板 {i}: {t.name} ---")
        lines.append(f"  适用场景：{t.description}")
        lines.append(
            "  动作序列模板：" + json.dumps(t.action_sequence_template, ensure_ascii=False)
        )
        lines.append(f"  使用注意：{t.usage_notes}")
    return "\n".join(lines)


# ----------------------------------------------------------------------
# 4. list_action_skills
# ----------------------------------------------------------------------
@tool
async def list_action_skills(
    agent: Annotated[str, InjectedState("name")],
) -> str:
    """回顾自己掌握的所有技能概况（每个技能下含所有模板的名称与描述）。

    当系统提示词中的动作技能记忆里没有匹配项，但你怀疑自己可能掌握过相关技能时，
    用此工具回顾完整清单。

    Returns:
        str: 全部技能的概况文本
    """
    group_id = _name_to_group_id(agent)
    try:
        text = await ActionSkillManager().get_skill_list(group_id)
    except Exception as e:
        return f"回顾技能列表时发生异常：{e}"
    return text


# ----------------------------------------------------------------------
# 5. refine_action_skill
# ----------------------------------------------------------------------
@tool
async def refine_action_skill(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str,
    reason: str,
    template_name: str = "",
    new_content: str = "",
    new_template_description: str = "",
    new_template: str = "",
    new_usage_notes: str = "",
) -> str:
    """根据实践经验精进已有技能的某个方面。

    template_name 留空：仅精进技能层 content；
    template_name 非空：精进该模板的描述 / 动作序列 / 使用注意事项（按非空字段更新）。
    任何字段被精进，技能 version 都会 +1，新动作序列直接覆盖旧的（不保留历史版本）。

    Args:
        skill_name(str): 要精进的技能名称
        reason(str): 精进原因（写在自己的记忆里供日后回看）
        template_name(str): 要精进的模板名称（留空表示仅精进技能层）
        new_content(str): 更新后的技能详细说明（可选）
        new_template_description(str): 更新后的模板描述（可选）
        new_template(str): 精进后的动作序列模板（JSON 数组字符串，可选）
        new_usage_notes(str): 更新后的使用注意事项（可选）

    Returns:
        str: 精进结果说明
    """
    new_seq: Optional[List[dict]] = None
    if new_template:
        try:
            new_seq = _parse_action_sequence(new_template)
        except ValueError as e:
            return f"精进技能失败：{e}"

    group_id = _name_to_group_id(agent)
    curtime = await _curtime_str()
    try:
        await ActionSkillManager().refine_skill(
            group_id=group_id,
            skill_name=skill_name,
            curtime=curtime,
            template_name=template_name,
            new_content=new_content,
            new_template_description=new_template_description,
            new_template=new_seq,
            new_usage_notes=new_usage_notes,
        )
    except ValueError as e:
        return f"精进技能失败：{e}"
    except Exception as e:
        return f"精进技能时发生异常：{e}"

    target = (
        f"模板'{template_name}'" if template_name else "技能整体"
    )
    return f"已精进技能'{skill_name}'的{target}。原因：{reason}"


# ----------------------------------------------------------------------
# 6. delete_action_skill
# ----------------------------------------------------------------------
@tool
async def delete_action_skill(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str,
    reason: str,
) -> str:
    """遗忘某个不再需要的技能及其所有模板。

    当你确认某个技能完全过时、或被其他技能取代时调用。

    Args:
        skill_name(str): 要遗忘的技能名称
        reason(str): 遗忘原因（写在自己的记忆里供日后回看）

    Returns:
        str: 遗忘结果说明
    """
    group_id = _name_to_group_id(agent)
    try:
        await ActionSkillManager().delete_skill(group_id, skill_name)
    except ValueError as e:
        return f"遗忘技能失败：{e}"
    except Exception as e:
        return f"遗忘技能时发生异常：{e}"
    return f"已遗忘技能'{skill_name}'。原因：{reason}"


# ----------------------------------------------------------------------
# 7. delete_action_skill_template
# ----------------------------------------------------------------------
@tool
async def delete_action_skill_template(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str,
    template_name: str,
    reason: str,
) -> str:
    """遗忘某个技能中特定场景下的模板。

    当你发现某个模板已经不适用、或被新模板替代时调用。
    若删除后该技能下已无任何模板，会提示你考虑遗忘整个技能。

    Args:
        skill_name(str): 技能名称
        template_name(str): 要遗忘的模板名称
        reason(str): 遗忘原因

    Returns:
        str: 遗忘结果说明
    """
    group_id = _name_to_group_id(agent)
    try:
        result = await ActionSkillManager().delete_template(
            group_id, skill_name, template_name
        )
    except ValueError as e:
        return f"遗忘模板失败：{e}"
    except Exception as e:
        return f"遗忘模板时发生异常：{e}"

    msg = f"已从技能'{skill_name}'中遗忘模板'{template_name}'。原因：{reason}"
    if result.get("is_last"):
        msg += (
            f"\n该技能下已无任何模板，是否考虑使用 delete_action_skill 遗忘整个技能？"
        )
    return msg


# ----------------------------------------------------------------------
# 工具列表（供 agent_interuptible.tools 直接拼接）
# ----------------------------------------------------------------------
SKILL_TOOLS = [
    create_action_skill,
    add_action_skill_template,
    load_action_skill,
    list_action_skills,
    refine_action_skill,
    delete_action_skill,
    delete_action_skill_template,
]
