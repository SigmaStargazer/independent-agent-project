# -*- coding: utf-8 -*-
"""统一配置读取层：把 api_config.json 的 12 项配置注入 os.environ。

优先级：api_config.json（存在且字段非空）> .env（load_dotenv 已读入 os.environ）。

用法：
    from config.api_config_loader import load_api_config_into_env, api_config_path
    load_api_config_into_env()  # 在 main() 早期、任何 LLM 构造之前调用
"""
import os

from runtime.path_config import get_api_config_file

# 与打包方案 §4.2 / Unity ApiConfigStore 字段一致的 12 项配置键
API_CONFIG_KEYS = [
    "AGENT_API_BASE", "AGENT_API_KEY", "AGENT_MODEL",
    "MEMORY_API_BASE", "MEMORY_API_KEY", "MEMORY_MODEL",
    "EMBEDDING_API_BASE", "EMBEDDING_API_KEY", "EMBEDDING_MODEL",
    "RERANKER_API_BASE", "RERANKER_API_KEY", "RERANKER_MODEL",
]


def api_config_path() -> str:
    """返回 Data/Config/api_config.json 的绝对路径（统一走 runtime.path_config）。"""
    return get_api_config_file()


def load_api_config_into_env(force: bool = False) -> dict:
    """读取 api_config.json；对每个字段，若 json 中该字段非空，则注入 os.environ。

    - force=False（默认）：仅当 os.environ 中某键缺失或为空时，才用 json 覆盖，
      保证 .env 已显式设置的键优先（开发期兼容）。
    - force=True：无条件用 json 覆盖所有键。
    - json 缺失/解析失败/字段为空：该键不注入，保持 .env / 环境原值。

    返回实际注入的键值 dict（幂等、可重复调用）。
    """
    path = api_config_path()
    if not os.path.exists(path):
        print(f"[api_config] 配置文件不存在，跳过注入: {path}")
        return {}

    try:
        import json
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
    except Exception as e:
        print(f"[api_config] 读取/解析失败，跳过注入: {e}")
        return {}

    if not isinstance(data, dict):
        print(f"[api_config] 配置内容不是 JSON 对象，跳过注入: {path}")
        return {}

    injected = {}
    for key in API_CONFIG_KEYS:
        value = data.get(key)
        if not value or not isinstance(value, str):
            continue
        if force or not os.environ.get(key):
            os.environ[key] = value.strip()
            injected[key] = value.strip()

    if injected:
        print(f"[api_config] 已注入 {len(injected)} 项配置: {sorted(injected.keys())}")
    else:
        print("[api_config] 无新配置注入（json 为空或 env 已全部有值）")
    return injected
