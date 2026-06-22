# -*- coding: utf-8 -*-
"""v0.21.5 可自测回归脚本。

覆盖范围：
1. ActionSequence condition 禁止单引号字符串，允许双引号字符串。
2. Vector2 坐标字段禁止大写 .X / .Y，允许小写 .x / .y。
3. ActionSkill step_explanations 强类型化、长度一致性、导出结构。
4. MemoryManager 情景日记兜底压缩保留开头和结尾而非简单截断。
5. Python / Unity 层关键校验代码已落位（静态源码检查）。
"""
from __future__ import annotations

import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
PY_SERVER = ROOT / "Src" / "PythonServer"
UNITY_CONDITION_EVALUATOR = ROOT / "Src" / "IndependentAgentProject" / "Assets" / "Scripts" / "IndependentAgentProject" / "ViewController" / "Gameplay" / "Action" / "ActionSequence" / "ConditionEvaluator" / "ConditionEvaluator.cs"

sys.path.insert(0, str(PY_SERVER))

from pydantic import ValidationError

from agent_framwork.tools.action_sequence_model.model.action import WaitAction
from memory_system.action_skill_system.skill_model import (
    ActionSequenceStepExplanation,
    ActionSequenceTemplate,
    normalize_step_explanations,
)
from memory_system.memory_manager import MemoryManager


def assert_raises_validation_error(fn, expected_text: str):
    try:
        fn()
    except ValidationError as exc:
        text = str(exc)
        assert expected_text in text, text
        return
    raise AssertionError("预期抛出 ValidationError，但实际未抛出")


def test_condition_single_quote_rejected():
    assert_raises_validation_error(
        lambda: WaitAction(condition="objects[1].State == 'Idle'"),
        "字符串必须使用双引号",
    )


def test_condition_double_quote_accepted():
    action = WaitAction(condition='objects[1].State == "Idle"')
    assert action.condition == 'objects[1].State == "Idle"'


def test_vector2_uppercase_field_rejected():
    assert_raises_validation_error(
        lambda: WaitAction(condition='objects[3].LeftPosition.X < 8'),
        "Vector2 坐标字段必须使用小写",
    )


def test_vector2_lowercase_field_accepted():
    action = WaitAction(condition='objects[3].LeftPosition.x < 8')
    assert action.condition == 'objects[3].LeftPosition.x < 8'


def test_step_explanations_require_complete_length():
    raw = [
        {
            "step_index": 0,
            "action_reason": "先等平台靠近，避免踩空。",
            "parameter_reason": "等待动作不需要方向参数。",
            "condition_reason": "平台到达近岸边界时再行动。",
            "adjustment_hint": "若平台速度变化，应改用边界距离条件。",
        }
    ]
    try:
        normalize_step_explanations(raw, step_count=2, require_complete=True)
    except ValueError as exc:
        assert "长度完全一致" in str(exc), exc
        return
    raise AssertionError("缺少 step_index=1 时应失败")


def test_step_explanations_dataclass_export():
    template = ActionSequenceTemplate(
        name="乘坐浮板上岸",
        description="浮板到岸后离开浮板",
        action_sequence_template=[
            {"action": "wait", "condition": "objects[1].State == \"Idle\""},
            {"action": "move", "direction": "right", "condition": "displacement >= 1", "allowed_contact_obj_ids": []},
        ],
        step_explanations=[
            ActionSequenceStepExplanation(
                step_index=0,
                action_reason="先等待浮板停稳。",
                parameter_reason="等待动作没有移动方向。",
                condition_reason="State 为 Idle 说明浮板阶段性停稳。",
                adjustment_hint="如果状态不可用，可换成边界距离条件。",
            ),
            ActionSequenceStepExplanation(
                step_index=1,
                action_reason="浮板停稳后向右上岸。",
                parameter_reason="目标在右侧，所以 direction 为 right。",
                condition_reason="位移足够后离开浮板范围。",
                adjustment_hint="距离应按实际岸边位置替换。",
            ),
        ],
        usage_notes="不要死记距离，应根据观察到的边界替换。",
    )
    exported = template.to_export_dict()
    assert len(exported["step_explanations"]) == len(exported["action_sequence_template"])
    assert exported["step_explanations"][0]["condition_reason"]


def test_memory_fallback_diary_keeps_timeline_edges():
    raw = "开头：我看到浮板在左岸。" + ("重复环境快照。" * 3000) + "结尾：我已经站在浮板上但还没有上岸。"
    compressed = MemoryManager()._fallback_diary_memory(raw)
    assert "这段时间的经历" in compressed
    assert "开头：我看到浮板在左岸。" in compressed
    assert "结尾：我已经站在浮板上但还没有上岸。" in compressed


def test_static_sources_have_v0215_guards():
    base_tools = (PY_SERVER / "agent_framwork" / "tools" / "base_tools.py").read_text(encoding="utf-8")
    condition_evaluator = UNITY_CONDITION_EVALUATOR.read_text(encoding="utf-8")
    assert "timer_repeat and delay_seconds < 120" in base_tools
    assert "ValidateSingleQuotedStringLiteral" in condition_evaluator
    assert "ValidateVector2UppercaseMember" in condition_evaluator
    assert "必须使用双引号" in condition_evaluator
    assert "Vector2 坐标字段必须使用小写" in condition_evaluator


def main():
    tests = [
        test_condition_single_quote_rejected,
        test_condition_double_quote_accepted,
        test_vector2_uppercase_field_rejected,
        test_vector2_lowercase_field_accepted,
        test_step_explanations_require_complete_length,
        test_step_explanations_dataclass_export,
        test_memory_fallback_diary_keeps_timeline_edges,
        test_static_sources_have_v0215_guards,
    ]
    for test in tests:
        test()
        print(f"PASS {test.__name__}")
    print("v0.21.5 self-test passed")


if __name__ == "__main__":
    main()
