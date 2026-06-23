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
from dataclasses import asdict, dataclass, field
from typing import Any, List
import json
import uuid as _uuid


def _new_uuid() -> str:
    return _uuid.uuid4().hex


@dataclass
class ActionSequenceStepExplanation:
    """动作序列中单个 step 的逐步解释。"""

    step_index: int = 0
    action_reason: str = ""
    parameter_reason: str = ""
    condition_reason: str = ""
    adjustment_hint: str = ""

    def to_dict(self) -> dict:
        return asdict(self)


def _parse_step_explanations_raw(raw: Any) -> list:
    if raw is None or raw == "":
        return []
    if isinstance(raw, str):
        try:
            raw = json.loads(raw)
        except Exception as e:
            raise ValueError(f"step_explanations 不是合法 JSON：{e}") from e
    if not isinstance(raw, list):
        raise ValueError("step_explanations 必须是数组")
    return raw


def normalize_step_explanations(
    raw: Any,
    step_count: int,
    require_complete: bool = False,
) -> List[ActionSequenceStepExplanation]:
    """把 I/O 边界的 dict / JSON 归一化为强类型解释列表。"""
    data = _parse_step_explanations_raw(raw)
    if not data:
        if require_complete and step_count > 0:
            raise ValueError("step_explanations 不能为空，且必须与动作序列步骤一一对应")
        return []

    explanations: List[ActionSequenceStepExplanation] = []
    seen_indices = set()
    for item in data:
        if isinstance(item, ActionSequenceStepExplanation):
            explanation = item
        elif isinstance(item, dict):
            explanation = ActionSequenceStepExplanation(
                step_index=int(item.get("step_index", 0)),
                action_reason=str(item.get("action_reason", "") or ""),
                parameter_reason=str(item.get("parameter_reason", "") or ""),
                condition_reason=str(item.get("condition_reason", "") or ""),
                adjustment_hint=str(item.get("adjustment_hint", "") or ""),
            )
        else:
            raise ValueError("step_explanations 的每一项必须是对象")

        if explanation.step_index < 0 or explanation.step_index >= step_count:
            raise ValueError(
                f"step_explanations[{explanation.step_index}] 超出动作序列范围 0..{step_count - 1}"
            )
        if explanation.step_index in seen_indices:
            raise ValueError(f"step_explanations 中 step_index={explanation.step_index} 重复")
        seen_indices.add(explanation.step_index)
        explanations.append(explanation)

    explanations.sort(key=lambda x: x.step_index)
    if require_complete:
        expected = set(range(step_count))
        if seen_indices != expected:
            missing = sorted(expected - seen_indices)
            raise ValueError(
                "step_explanations 必须与 action_sequence_template 长度完全一致；"
                f"缺少 step_index={missing}"
            )
    return explanations


def step_explanations_to_dicts(
    explanations: List[ActionSequenceStepExplanation],
) -> List[dict]:
    return [item.to_dict() for item in explanations]


@dataclass
class ActionSequenceTemplate:
    """某个 ActionSkill 下的一个具体动作序列模板。

    一个 ActionSkill 可对应多个 ActionSequenceTemplate（1:N），
    每个模板代表一个特定使用场景下的动作序列模板。

    v0.21.6：`action_sequence_template` 是「参数化动作序列模板蓝图」，
    允许在字符串字段中内联 `{snake_case}` 占位符（如 `"{direction}"`、`"{platform_index}"`）。
    模板保存时使用宽松校验（参见 agent.tools.skill_tools._parse_action_sequence_template），
    真正执行 `plan_action_sequence_cmd` 时会强校验，拒绝未替换的占位符。
    占位符的语义解释写在 `step_explanations.parameter_reason` 与 `usage_notes` 中，
    不再单独维护 `template_parameters` 字段。
    """
    uuid: str = field(default_factory=_new_uuid)
    skill_uuid: str = ""
    name: str = ""                                # Agent 可读的模板名称（同一技能下唯一）
    group_id: str = ""
    description: str = ""                         # 简短描述（用于 RAG 索引匹配）
    description_embedding: List[float] = field(default_factory=list)  # description 的向量
    action_sequence_template: List[dict] = field(default_factory=list)  # 含内联 {占位符} 的动作序列蓝图
    step_explanations: List[ActionSequenceStepExplanation] = field(default_factory=list)
    usage_notes: str = ""                         # 使用注意事项（场合、占位符填参经验等）
    created_at: str = ""                          # 创建时间（虚拟时间字符串）
    updated_at: str = ""                          # 最后修改时间（虚拟时间字符串）

    def __post_init__(self):
        self.step_explanations = normalize_step_explanations(
            self.step_explanations,
            len(self.action_sequence_template),
            require_complete=False,
        )

    def step_explanations_dicts(self) -> List[dict]:
        return step_explanations_to_dicts(self.step_explanations)

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
            "step_explanations": self.step_explanations_dicts(),
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
            "step_explanations": self.step_explanations_dicts(),
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
        """用于注入 system prompt 的技能索引。"""
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
