# -*- coding: utf-8 -*-
"""
Action Skill 经验学习系统的数据模型。

定义两类核心实体：
- ActionSkill：技能类别
- ActionSequenceTemplate：技能下的具体动作序列模板

主键 / 外键 / 业务唯一性：
- ActionSkill 主键 uuid，业务唯一 (name, group_id)
- ActionSequenceTemplate 主键 uuid，外键 skill_uuid，业务唯一 (name, skill_uuid)
"""
from dataclasses import dataclass, field
from typing import List
import uuid as _uuid


def _new_uuid() -> str:
    return _uuid.uuid4().hex


@dataclass
class ActionSequenceTemplate:
    """某个 ActionSkill 下的一个具体动作序列模板。

    一个 ActionSkill 可对应多个 ActionSequenceTemplate（1:N），
    每个模板代表一个特定使用场景下的动作序列模板（含参数占位符）。
    """
    uuid: str = field(default_factory=_new_uuid)
    skill_uuid: str = ""
    name: str = ""                                # Agent 可读的模板名称（同一技能下唯一）
    group_id: str = ""
    description: str = ""                         # 简短描述（用于 RAG 索引匹配）
    description_embedding: List[float] = field(default_factory=list)  # description 的向量
    action_sequence_template: List[dict] = field(default_factory=list)  # 含参数占位符的动作序列
    usage_notes: str = ""                         # 使用注意事项（场合、填参经验等）
    created_at: str = ""                          # 创建时间（虚拟时间字符串）
    updated_at: str = ""                          # 最后修改时间（虚拟时间字符串）

    def to_summary_dict(self) -> dict:
        """用于 list_action_skills 等需要摘要的场景。"""
        return {
            "name": self.name,
            "description": self.description,
        }

    def to_full_dict(self) -> dict:
        """用于 load_action_skill 等需要完整内容的场景。"""
        return {
            "name": self.name,
            "description": self.description,
            "action_sequence_template": self.action_sequence_template,
            "usage_notes": self.usage_notes,
            "created_at": self.created_at,
            "updated_at": self.updated_at,
        }

    def to_export_dict(self) -> dict:
        """用于导出 YAML（不含数据库内部字段如 uuid、embedding）。"""
        return {
            "name": self.name,
            "description": self.description,
            "action_sequence_template": self.action_sequence_template,
            "usage_notes": self.usage_notes,
        }


@dataclass
class ActionSkill:
    """一个完整的 Action Skill（含其所有 ActionSequenceTemplate）。"""
    uuid: str = field(default_factory=_new_uuid)
    name: str = ""                                # Agent 可读的技能名（同一 group_id 下唯一）
    group_id: str = ""
    description: str = ""                         # 简短描述（用于技能索引第一层）
    content: str = ""                             # 详细描述（load 时才展示给 Agent）
    version: int = 1                              # 精进版本号（任何精进操作都 +1）
    source: str = "learned"                       # "default" | "learned" | "refined"
    created_at: str = ""
    updated_at: str = ""
    templates: List[ActionSequenceTemplate] = field(default_factory=list)

    def to_index_dict(self) -> dict:
        """用于注入 system prompt 的技能索引（轻量）。"""
        return {
            "name": self.name,
            "description": self.description,
            "templates": [t.to_summary_dict() for t in self.templates],
        }

    def to_full_dict(self) -> dict:
        """用于 load_action_skill 等需要完整内容的场景。"""
        return {
            "name": self.name,
            "description": self.description,
            "content": self.content,
            "version": self.version,
            "source": self.source,
            "created_at": self.created_at,
            "updated_at": self.updated_at,
            "templates": [t.to_full_dict() for t in self.templates],
        }

    def to_export_dict(self) -> dict:
        """用于导出 YAML（保留 source 原值，不含 uuid 等内部字段）。"""
        return {
            "name": self.name,
            "description": self.description,
            "content": self.content,
            "source": self.source,
            "templates": [t.to_export_dict() for t in self.templates],
        }
