# -*- coding: utf-8 -*-
"""db_conn 包：底层数据库连接管理。

向外暴露 DBConnectionService 单例，负责 Kuzu Database / AsyncConnection 的生命周期、
FTS 扩展加载、文件路径配置以及业务模块共用的"冻结门"（freeze gate）。
业务模块（MemoryManager、ActionSkillManager 等）通过 service 取连接，不再各自持有引用，
从根本上避免文件锁残留。
"""
from db_conn.db_connection_service import DBConnectionService

__all__ = ["DBConnectionService"]
