# -*- coding: utf-8 -*-
"""默认技能加载器。

加载顺序：
1. ``Src/PythonServer/db/default_skills/<group_id>.yaml``（按 Agent 定制）
2. ``Src/PythonServer/db/default_skills/default.yaml``（兜底）

把 YAML 解析为 ``List[dict]``，供 ``main.handle_agent_create_request``
在 Agent 创建后注入到 Kuzu。
"""
from __future__ import annotations

import os
from typing import List


# ../../db/default_skills 相对于本文件 (memory_system/action_skill_system/default_skill_loader.py)
DEFAULT_SKILLS_DIR = os.path.normpath(os.path.join(
    os.path.dirname(__file__), "..", "..", "db", "default_skills"
))


def load_default_skills(
    group_id: str | None = None,
    path: str | None = None,
) -> List[dict]:
    """读取默认技能 YAML；文件不存在或解析失败抛异常给调用方处理。

    返回结构：
        [
            {"name": ..., "description": ..., "content": ..., "templates": [...]},
            ...
        ]

    路径优先级：
    - 显式 ``path`` 参数 > ``<group_id>.yaml`` > ``default.yaml``
    - 都不存在时返回空列表（调用方应当容忍：默认技能是可选的）。
    """
    target = _resolve_target(group_id=group_id, path=path)
    if target is None or not os.path.exists(target):
        return []

    try:
        import yaml  # PyYAML
    except ImportError as e:
        raise RuntimeError(
            "解析 default_skills.yaml 需要 PyYAML，请执行：uv add pyyaml"
        ) from e

    with open(target, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f) or {}

    skills = data.get("skills", []) or []
    if not isinstance(skills, list):
        raise ValueError(
            f"默认技能 YAML '{target}' 的 'skills' 字段应为列表"
        )
    return skills


def _resolve_target(group_id: str | None, path: str | None) -> str | None:
    if path:
        return path

    if group_id:
        per_agent = os.path.join(DEFAULT_SKILLS_DIR, f"{group_id}.yaml")
        if os.path.exists(per_agent):
            return per_agent

    fallback = os.path.join(DEFAULT_SKILLS_DIR, "default.yaml")
    return fallback
