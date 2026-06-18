# -*- coding: utf-8 -*-
"""记忆系统总入口。"""

__all__ = ["MemoryManager"]


def __getattr__(name):
    if name == "MemoryManager":
        from .memory_manager import MemoryManager
        return MemoryManager
    raise AttributeError(name)
