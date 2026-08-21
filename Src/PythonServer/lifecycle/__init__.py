# -*- coding: utf-8 -*-
"""lifecycle 包：进程级生命周期编排（进游戏初始化 / 回 Title 关闭）。

设计依据见 `DevDocs/Architecture/生命周期架构.md`。
"""
from .lifecycle import AgentLifecycle

__all__ = ["AgentLifecycle"]
