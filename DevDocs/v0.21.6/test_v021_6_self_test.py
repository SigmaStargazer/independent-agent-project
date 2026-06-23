# -*- coding: utf-8 -*-
"""v0.21.6 自测脚本：ActionSkill 内联占位符模板 + 持续观察术语统一。

运行：
    cd Src/PythonServer
    uv run python ../../DevDocs/v0.21.6/test_v021_6_self_test.py
"""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PYTHONSERVER = ROOT / "Src" / "PythonServer"
sys.path.insert(0, str(PYTHONSERVER))


PASS = "[PASS]"
FAIL = "[FAIL]"
failures: list[str] = []


def check(name: str, ok: bool, detail: str = "") -> None:
    if ok:
        print(f"{PASS} {name}")
    else:
        msg = f"{FAIL} {name}"
        if detail:
            msg += f" -- {detail}"
        print(msg)
        failures.append(name)


def section(title: str) -> None:
    print()
    print(f"=== {title} ===")


# ----------------------------------------------------------------------
section("TEMPLATE: skill_tools 占位符校验")

from agent.tools import skill_tools  # noqa: E402

valid_template = json.dumps([
    {
        "action": "move",
        "direction": "{direction}",
        "condition": "myself.Position.x > {exit_threshold}",
        "allowed_contact_obj_ids": ["{trap_index}"],
    }
])
try:
    parsed = skill_tools._parse_action_sequence_template(valid_template)
    check("TEMPLATE-001 合法占位符通过", isinstance(parsed, list) and len(parsed) == 1)
except Exception as e:
    check("TEMPLATE-001 合法占位符通过", False, repr(e))


illegal_cases = [
    ("Direction 大写", json.dumps([{"action": "move", "direction": "{Direction}"}])),
    ("中文占位符", json.dumps([{"action": "move", "direction": "{方向}"}])),
    ("空格", json.dumps([{"action": "move", "direction": "{bad name}"}])),
    ("空占位符", json.dumps([{"action": "move", "direction": "{}"}])),
    ("action 占位符", json.dumps([{"action": "{action}"}])),
]
for name, raw in illegal_cases:
    try:
        skill_tools._parse_action_sequence_template(raw)
        check(f"TEMPLATE-002 非法占位符拒绝({name})", False, "未抛错")
    except ValueError as e:
        check(f"TEMPLATE-002 非法占位符拒绝({name})", True)


bad_json_raw = '[{"action":"move","direction":{direction}}]'
try:
    skill_tools._parse_action_sequence_template(bad_json_raw)
    check("TEMPLATE-003 裸占位符 JSON 解析失败友好提示", False)
except ValueError as e:
    msg = str(e)
    check(
        "TEMPLATE-003 裸占位符 JSON 解析失败友好提示",
        "占位符" in msg and "字符串" in msg,
        msg,
    )


# ----------------------------------------------------------------------
section("EXEC: plan_action_sequence_cmd 拒绝未替换占位符")

from agent_framwork.tools import base_tools  # noqa: E402

unresolved = base_tools._find_unresolved_placeholders([
    {"action": "move", "direction": "{direction}"},
    {"action": "wait", "condition": "objects[{platform_index}].State == 'Idle'"},
])
check(
    "TEMPLATE-004a 含占位符 -> 报告占位符",
    "{direction}" in unresolved and "{platform_index}" in unresolved,
    str(unresolved),
)

unresolved2 = base_tools._find_unresolved_placeholders([
    {"action": "move", "direction": "left", "condition": "myself.Position.x > 10"},
])
check(
    "TEMPLATE-004b 真实序列 -> 无占位符",
    unresolved2 == [],
    str(unresolved2),
)


# ----------------------------------------------------------------------
section("ACTION TYPE: get_known_action_names 来源于 Union")

from agent_framwork.tools.action_sequence_model.model.action_sequence import (  # noqa: E402
    get_known_action_names,
)

known = get_known_action_names()
expected = {"wait", "move", "interact", "select", "input"}
check(
    "ACTION-001 自动派生 action 集合",
    expected.issubset(known),
    f"expected⊆got? got={sorted(known)}",
)


# ----------------------------------------------------------------------
section("YAML: 默认技能格式")

import yaml  # noqa: E402

yaml_path = PYTHONSERVER / "db" / "default_skills" / "default.yaml"
data = yaml.safe_load(yaml_path.read_text(encoding="utf-8"))
text = yaml_path.read_text(encoding="utf-8")

check("YAML-001a 不存在 'action: Move'", "action: Move" not in text)
check("YAML-001b 不存在 'action: Interact'", "action: Interact" not in text)
check("YAML-001c 不存在旧式 params.target", "params:" not in text)
check("YAML-001d 至少包含一个 {direction}", "{direction}" in text)
check("YAML-001e 不存在 template_parameters", "template_parameters" not in text)

all_have_step_explanations = True
for skill in data.get("skills", []):
    for tmpl in skill.get("templates", []):
        if not tmpl.get("step_explanations"):
            all_have_step_explanations = False
check("YAML-001f 所有模板都有 step_explanations", all_have_step_explanations)


# ----------------------------------------------------------------------
section("TOOL: skill_tools 工具描述包含分离规则")

skill_tools_src = Path(skill_tools.__file__).read_text(encoding="utf-8")
check(
    "TOOL-001a 提到 action_sequence_template 支持 {placeholder}",
    "{snake_case}" in skill_tools_src or "{placeholder}" in skill_tools_src,
)
check(
    "TOOL-001b 提到执行前必须替换",
    "plan_action_sequence_cmd" in skill_tools_src and "替换" in skill_tools_src,
)
check(
    "TOOL-001c 不再以入参形式要求 template_parameters",
    "template_parameters: str" not in skill_tools_src
    and "template_parameters=" not in skill_tools_src,
)


# ----------------------------------------------------------------------
section("PROTO: AgentGetMonitorRecordsRequest 字段重命名")

proto_path = ROOT / "Tools" / "message.proto"
proto_text = proto_path.read_text(encoding="utf-8")
m = re.search(
    r"message\s+AgentGetMonitorRecordsRequest\s*\{([^}]*)\}",
    proto_text,
    re.S,
)
proto_body = m.group(1) if m else ""
check("PROTO-001a 字段 monitor_target_index 存在", "monitor_target_index" in proto_body, proto_body)
check("PROTO-001b 字段号 3 沿用", re.search(r"monitor_target_index\s*=\s*3", proto_body) is not None)
check("PROTO-001c 不存在旧字段 monitor_index", "monitor_index" not in proto_body)
check("PROTO-001d 不引入 object_index / object_name", "object_index" not in proto_body and "object_name" not in proto_body)


# ----------------------------------------------------------------------
section("UNITY: AIPlayer / RuntimeInfoRenderer / AgentService")

unity_root = ROOT / "Src" / "IndependentAgentProject" / "Assets" / "Scripts" / "IndependentAgentProject"
aiplayer = (unity_root / "ViewController" / "Gameplay" / "SceneObj" / "Chara" / "AIPlayer.cs").read_text(encoding="utf-8")
renderer = (unity_root / "ViewController" / "Gameplay" / "Action" / "RuntimeInfoRenderer" / "RuntimeInfoRenderer.cs").read_text(encoding="utf-8")
service = (unity_root / "Services" / "AgentService.cs").read_text(encoding="utf-8")
manager = (unity_root / "ViewController" / "Gameplay" / "SceneObj" / "Chara" / "AgentManager.cs").read_text(encoding="utf-8")

check("UNITY-001a MonitorTarget 成功返回包含 '第'/'个持续观察目标'", "个持续观察目标" in aiplayer)
check("UNITY-001b MonitorTarget 不暴露字面量 monitor_index", "monitor_index" not in aiplayer)
check("UNITY-001c MonitorTarget 不暴露字面量 monitor_target_index", "monitor_target_index" not in aiplayer)

check("UNITY-002a GetMonitorRecords 入参 monitorTargetIndex", "int monitorTargetIndex" in aiplayer)
check("UNITY-002b GetMonitorRecords 错误使用「持续观察目标」", "持续观察目标[" in aiplayer)
check("UNITY-002c 不存在 monitor[0]不存在 旧文案", "monitor[" not in aiplayer)

check("UNITY-003a 摘要 '持续观察目标[' 出现", "持续观察目标[" in renderer)
check("UNITY-003b 摘要不再以 '观察目标[' 单独出现", re.search(r"(?<!持续)观察目标\[", renderer) is None)
check("UNITY-003c 摘要不展示字段名 monitor_index", "monitor_index" not in renderer)

check("UNITY-004 AgentService 使用 MonitorTargetIndex", "MonitorTargetIndex" in service and "MonitorIndex" not in service)
check("UNITY-005 AgentManager 使用 monitorTargetIndex", "monitorTargetIndex" in manager and "monitorIndex" not in manager)


# ----------------------------------------------------------------------
print()
if failures:
    print(f"FAILED: {len(failures)} test(s) failed:")
    for f in failures:
        print(f"  - {f}")
    sys.exit(1)
else:
    print("ALL TESTS PASSED")
