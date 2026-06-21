from __future__ import annotations

import re
from pathlib import Path

from pydantic import ValidationError

from agent_framwork.tools.action_sequence_model.model.action import MoveAction, WaitAction
from agent_framwork.tools.base_tools import follow_target_cmd

PYTHON_SERVER_DIR = Path(__file__).resolve().parent
SRC_DIR = PYTHON_SERVER_DIR.parent
UNITY_ROOT = SRC_DIR / "IndependentAgentProject"

EXPR_VIEW_FACTORY = UNITY_ROOT / "Assets" / "Scripts" / "IndependentAgentProject" / "ViewController" / "Gameplay" / "Action" / "ActionSequence" / "ConditionEvaluator" / "ExprViewFactory.cs"
CONDITION_EVALUATOR = UNITY_ROOT / "Assets" / "Scripts" / "IndependentAgentProject" / "ViewController" / "Gameplay" / "Action" / "ActionSequence" / "ConditionEvaluator" / "ConditionEvaluator.cs"
SCENE_OBJ_INFO_RENDERER = UNITY_ROOT / "Assets" / "Scripts" / "IndependentAgentProject" / "ViewController" / "Gameplay" / "SceneObj" / "Base" / "SceneObjInfo" / "SceneObjInfoRenderer.cs"


def assert_contains(text: str, expected: str, label: str) -> None:
    if expected not in text:
        raise AssertionError(f"{label}: missing {expected!r}")


def read_utf8(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    unicode_escape = re.search(r"\\u[0-9a-fA-F]{4}", text)
    if unicode_escape:
        raise AssertionError(f"{path.name}: contains escaped unicode {unicode_escape.group(0)!r}")
    return text


def test_py_001_boundary_fields_are_allowed() -> None:
    move_condition = "objects[3].RightPosition.x - objects[2].RightPosition.x > 0.3"
    move = MoveAction(
        action="move",
        direction="right",
        condition=move_condition,
        allowed_contact_obj_ids=[],
    )
    assert move.condition == move_condition

    wait_condition = "objects[3].LeftPosition.x < objects[2].RightPosition.x"
    wait = WaitAction(action="wait", condition=wait_condition)
    assert wait.condition == wait_condition


def test_py_002_unknown_object_field_is_rejected() -> None:
    try:
        MoveAction(
            action="move",
            direction="right",
            condition="objects[3].UnknownPosition.x > 0",
            allowed_contact_obj_ids=[],
        )
    except ValidationError as exc:
        message = str(exc)
        if "UnknownPosition" not in message and "不允许访问成员" not in message:
            raise AssertionError(f"unexpected validation message: {message}") from exc
    else:
        raise AssertionError("objects[3].UnknownPosition should be rejected")


def test_py_003_position_is_not_rejected_by_python_schema() -> None:
    condition = "objects[3].Position.x < 7"
    wait = WaitAction(action="wait", condition=condition)
    assert wait.condition == condition


def test_py_004_follow_target_docstring_is_semantic_only() -> None:
    doc = follow_target_cmd.description or ""
    assert_contains(doc, "持续跟随状态", "follow_target_cmd docstring")
    assert_contains(doc, "min_distance", "follow_target_cmd docstring")
    assert_contains(doc, "max_distance", "follow_target_cmd docstring")
    assert_contains(doc, "不是一次性移动", "follow_target_cmd docstring")
    assert_contains(doc, "不是由多个阶段组成", "follow_target_cmd docstring")

    forbidden_phrases = [
        "浮板过陷阱禁止使用",
        "禁止用于浮板",
        "禁止用于过陷阱",
    ]
    for phrase in forbidden_phrases:
        if phrase in doc:
            raise AssertionError(f"follow_target_cmd docstring contains hard-coded ban: {phrase}")


def test_src_001_unity_source_contains_key_static_points() -> None:
    expr_view = read_utf8(EXPR_VIEW_FACTORY)
    for unexpected in ["public bool IsRange", "view.IsRange"]:
        if unexpected in expr_view:
            raise AssertionError(f"ExprViewFactory.cs should not contain {unexpected!r}")

    for expected in [
        "LeftPosition",
        "RightPosition",
        "bounds.min.x",
        "bounds.max.x",
        "bounds.center.y",
    ]:
        assert_contains(expr_view, expected, "ExprViewFactory.cs")

    condition_evaluator = read_utf8(CONDITION_EVALUATOR)
    for expected in [
        "ValidateRangeObjectPositionReference",
        "范围物体",
        "不能使用 Position",
        "LeftPosition",
        "RightPosition",
    ]:
        assert_contains(condition_evaluator, expected, "ConditionEvaluator.cs")

    renderer = read_utf8(SCENE_OBJ_INFO_RENDERER)
    for expected in [
        "左边界",
        "右边界",
        ":F2",
    ]:
        assert_contains(renderer, expected, "SceneObjInfoRenderer.cs")


def main() -> None:
    tests = [
        ("PY-001", test_py_001_boundary_fields_are_allowed),
        ("PY-002", test_py_002_unknown_object_field_is_rejected),
        ("PY-003", test_py_003_position_is_not_rejected_by_python_schema),
        ("PY-004", test_py_004_follow_target_docstring_is_semantic_only),
        ("SRC-001", test_src_001_unity_source_contains_key_static_points),
    ]

    for test_id, test_func in tests:
        test_func()
        print(f"{test_id} passed")

    print("v0.21.4 当前环境可自测用例全部通过")


if __name__ == "__main__":
    main()
