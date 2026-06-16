# -*- coding: utf-8 -*-
"""
Action Skill 经验学习系统。

子模块：
- skill_model: 数据类 ActionSkill / ActionSequenceTemplate
- action_skill_manager: ActionSkillManager（@singleton）
"""
from action_skill_system.skill_model import ActionSkill, ActionSequenceTemplate
from action_skill_system.action_skill_manager import ActionSkillManager
from action_skill_system.default_skill_loader import load_default_skills

__all__ = [
    "ActionSkill",
    "ActionSequenceTemplate",
    "ActionSkillManager",
    "load_default_skills",
]
