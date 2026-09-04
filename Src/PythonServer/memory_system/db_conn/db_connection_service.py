# -*- coding: utf-8 -*-
"""DBConnectionService: Kuzu 数据库连接的底层单例。

职责：
    - 持有唯一的 kuzu.Database / kuzu.AsyncConnection 实例
    - 负责打开/关闭数据库（含 WAL fallback、gc 释放文件锁）
    - 加载 FTS 扩展
    - 提供"冻结门"（freeze）让 backup 等关键操作能阻塞业务写入
    - 暴露 db_path / wal_path / is_new_db 等元信息

使用方式：
    await DBConnectionService().initialize()
    conn = DBConnectionService().get_conn()
    db = DBConnectionService().get_db()
    async with DBConnectionService().access():
        await conn.execute("...")
    await DBConnectionService().close()

注意：业务模块**不要**自己存 self.conn / self._kuzu_db 引用，每次需要时从 service 取。
否则 close 时可能因外部强引用导致 Database 无法被 GC，文件锁不释放。
"""
from __future__ import annotations

import asyncio
import gc
import os
from contextlib import asynccontextmanager
from typing import Optional

import kuzu

from agent_framwork.base.singleton import singleton

from runtime.path_config import get_data_dir

# 数据库默认配置（与历史 memory_manager 保持一致）
# v0.23.3b：改为绝对路径（运行时数据根），消除对进程工作目录的隐式依赖。
# 开发态 = PythonServer/db，打包态 = exe 同级 db，两种形态下 db/graphiti.kuzu 位置一致。
DB_ROOT = get_data_dir()
DB_NAME = "graphiti"


# ---------------------------------------------------------------------------
# Windows 中文路径规避（Kuzu C++ 底层用 CreateFileA/ANSI，非 ASCII 路径在中文
# Windows 上被按 GBK 误解，新建库报 Error 3 / UnicodeDecodeError。业界解法：
# 8.3 短路径（多数机器未启用）或 NTFS junction。本项目用 junction，已实测可行，
# 且 mklink /J 不需管理员权限）。
# 纯 ASCII 路径零开销直通；仅含非 ASCII 时建 junction。
# ---------------------------------------------------------------------------


def _path_is_ascii(p: str) -> bool:
    try:
        p.encode("ascii")
        return True
    except UnicodeEncodeError:
        return False


_JUNCTION_CACHE: dict = {}
_JUNCTION_CLEANUP_REGISTERED = False


def _cleanup_junction() -> None:
    """进程退出时尽力删除本进程创建的全部 junction（junction 是目录链接，用 os.rmdir）。"""
    global _JUNCTION_CACHE
    for link in list(_JUNCTION_CACHE.values()):
        try:
            import pathlib

            if os.path.isdir(link) and pathlib.Path(link).is_junction():
                os.rmdir(link)
        except Exception:
            pass
    _JUNCTION_CACHE.clear()


def _kuzu_safe_path(db_path: str) -> str:
    """把 Kuzu 要打开的路径转成纯 ASCII 等价路径（仅 Windows 且含非 ASCII 时）。

    junction 名按 db 目录绝对路径的 hash 隔离：不同运行根（开发版 / 打包版 /
    不同游戏根）各用各的 junction，避免共享同一固定 junction 导致 Kuzu
    文件锁冲突（Could not set lock）。同路径复用缓存，不重复创建。
    """
    global _JUNCTION_CACHE, _JUNCTION_CLEANUP_REGISTERED

    if os.name != "nt":
        return db_path
    if _path_is_ascii(db_path):
        return db_path

    db_dir = os.path.abspath(os.path.dirname(db_path))
    import hashlib

    tag = hashlib.sha1(db_dir.encode("utf-8")).hexdigest()[:12]
    if tag in _JUNCTION_CACHE:
        return os.path.join(_JUNCTION_CACHE[tag], os.path.basename(db_path))

    import atexit
    import subprocess
    import tempfile

    junction_base = os.path.join(tempfile.gettempdir(), "pskuzu")
    link_dir = os.path.join(junction_base, tag, "db")
    try:
        os.makedirs(os.path.dirname(link_dir), exist_ok=True)
        if not os.path.exists(link_dir):
            r = subprocess.run(
                ["cmd", "/c", "mklink", "/J", link_dir, db_dir],
                capture_output=True,
            )
            if r.returncode != 0:
                return db_path
        _JUNCTION_CACHE[tag] = link_dir
        if not _JUNCTION_CLEANUP_REGISTERED:
            atexit.register(_cleanup_junction)
            _JUNCTION_CLEANUP_REGISTERED = True
    except Exception:
        return db_path

    return os.path.join(link_dir, os.path.basename(db_path))


@singleton
class DBConnectionService:
    def __init__(self):
        self._kuzu_db: Optional[kuzu.Database] = None
        self._conn: Optional[kuzu.AsyncConnection] = None

        self._initialized: bool = False
        self._is_new_db: bool = False
        self._init_lock: Optional[asyncio.Lock] = None

        self._db_root: str = DB_ROOT
        self._db_name: str = DB_NAME

        # 冻结门：backup / restore 期间禁止业务写入
        self._freeze: bool = False
        self._active_ops: int = 0
        self._active_cond: Optional[asyncio.Condition] = None

    @property
    def is_initialized(self) -> bool:
        return self._initialized

    @property
    def is_new_db(self) -> bool:
        """initialize() 这次打开时数据库是否为空（无任何 schema）。"""
        return self._is_new_db

    @property
    def db_path(self) -> str:
        return os.path.join(self._db_root, f"{self._db_name}.kuzu")

    @property
    def wal_path(self) -> str:
        return os.path.join(self._db_root, f"{self._db_name}.wal")

    def _open_database(self, db_path: str) -> kuzu.Database:
        """打开/新建 Kuzu 数据库，规避 Windows 中文路径 bug。

        Kuzu C++ 底层用 CreateFileA（ANSI），非 ASCII 路径在中文 Windows 上被
        按 GBK 误解：新建库报 Error 3，或 UnicodeDecodeError。这里把路径转成
        纯 ASCII 等价路径（NTFS junction，见模块级 _kuzu_safe_path）再打开。
        纯 ASCII 路径（如 CI / 英文目录）零开销直通。
        """
        return kuzu.Database(_kuzu_safe_path(db_path))

    def get_conn(self) -> kuzu.AsyncConnection:
        if self._conn is None:
            raise RuntimeError("DBConnectionService 尚未初始化，先调用 await initialize()")
        return self._conn

    def get_db(self) -> kuzu.Database:
        if self._kuzu_db is None:
            raise RuntimeError("DBConnectionService 尚未初始化，先调用 await initialize()")
        return self._kuzu_db

    async def initialize(self) -> "DBConnectionService":
        if self._initialized:
            return self
        if self._init_lock is None:
            self._init_lock = asyncio.Lock()
        if self._active_cond is None:
            self._active_cond = asyncio.Condition()

        async with self._init_lock:
            if self._initialized:
                return self

            print("💾 [DBConn] 正在挂载全局数据库...")
            db_path = self.db_path
            wal_path = self.wal_path
            db_exists_before = os.path.exists(db_path)

            opened = False
            try:
                self._kuzu_db = self._open_database(db_path)
                opened = True
            except Exception as e:
                print("⚠️ [DBConn] 数据库打开失败，尝试清理 WAL:", e)
                if os.path.exists(wal_path):
                    try:
                        os.remove(wal_path)
                        print(f"🧹 [DBConn] 已删除 WAL 文件: {wal_path}")
                    except Exception as wal_err:
                        print(f"⚠️ [DBConn] 删除 WAL 失败: {wal_err}")
                        raise RuntimeError(
                            f"WAL 删除失败，数据库可能处于脏状态: {wal_path}"
                        )

            if not opened:
                try:
                    self._kuzu_db = self._open_database(db_path)
                    print("✅ [DBConn] 数据库已通过 clean start 打开")
                except Exception as retry_err:
                    print("❌ [DBConn] clean start 仍失败:", retry_err)
                    raise

            self._conn = kuzu.AsyncConnection(self._kuzu_db)

            # 判断 schema 是否存在
            schema_initialized = False
            if db_exists_before:
                try:
                    result = await self._conn.execute("CALL show_tables() RETURN *")
                    schema_initialized = any(True for _ in result.rows_as_dict())
                except Exception:
                    schema_initialized = False
            self._is_new_db = not schema_initialized

            # 加载 FTS 扩展
            await self._ensure_fts_loaded()

            self._initialized = True
            print("✅ [DBConn] DBConnectionService initialized")
        return self

    async def close(self) -> None:
        """完全关闭数据库；释放文件锁。"""
        self._initialized = False
        # 重置冻结状态，防止重新 initialize 后 access() 死等
        self._freeze = False
        self._active_ops = 0

        print("🛑 [DBConn] 正在关闭连接池...")
        try:
            if self._conn is not None:
                close_fn = getattr(self._conn, "close", None)
                if callable(close_fn):
                    result = close_fn()
                    if asyncio.iscoroutine(result):
                        await result
            self._conn = None
            self._kuzu_db = None

            # gc + sleep + gc：让 C++ 层 mmap 真正释放（Windows 文件锁敏感）
            gc.collect()
            await asyncio.sleep(0.5)
            gc.collect()
            print("✅ [DBConn] Database 已关闭，文件锁已释放")
        except Exception as e:
            print(f"⚠️ [DBConn] 关闭资源时出错: {e}")

    async def _ensure_fts_loaded(self) -> None:
        """幂等加载 FTS 扩展。"""
        try:
            result = await self._conn.execute("CALL SHOW_LOADED_EXTENSIONS() RETURN *")
            rows = result.rows_as_dict()
            loaded: set[str] = set()
            for row in rows:
                for v in row.values():
                    if isinstance(v, str):
                        loaded.add(v.upper())

            if "FTS" not in loaded:
                print("[DBConn] installing FTS...")
                try:
                    await self._conn.execute("INSTALL FTS")
                except Exception as e:
                    print("[DBConn] INSTALL FTS warning:", e)
                await self._conn.execute("LOAD EXTENSION FTS")
                print("✅ [DBConn] FTS 扩展已加载")
            else:
                print("ℹ️ [DBConn] FTS 已存在，跳过加载")
        except Exception as e:
            print("❌ [DBConn] FTS 检查/加载失败:", e)
            raise

    # ----------------------------------------------------------
    # 冻结门：backup 期间禁止业务写入
    # ----------------------------------------------------------
    @asynccontextmanager
    async def access(self):
        """排队访问，业务模块访问数据库时使用。"""
        await self._begin_op()
        try:
            yield
        finally:
            await self._end_op()

    async def _begin_op(self) -> None:
        if self._active_cond is None:
            self._active_cond = asyncio.Condition()
        async with self._active_cond:
            while self._freeze:
                await self._active_cond.wait()
            self._active_ops += 1

    async def _begin_op_internal(self) -> None:
        """供 worker 在已通过其他锁串行的场合占位（不再次 wait freeze）。"""
        if self._active_cond is None:
            self._active_cond = asyncio.Condition()
        async with self._active_cond:
            self._active_ops += 1

    async def _end_op(self) -> None:
        async with self._active_cond:
            self._active_ops -= 1
            if self._active_ops == 0:
                self._active_cond.notify_all()

    async def freeze(self) -> None:
        """阻塞新业务进入；已进入的允许执行完。"""
        if self._active_cond is None:
            self._active_cond = asyncio.Condition()
        async with self._active_cond:
            self._freeze = True

    async def unfreeze(self) -> None:
        async with self._active_cond:
            self._freeze = False
            self._active_cond.notify_all()

    async def wait_idle(self) -> None:
        """等待所有进行中的 access() 退出。"""
        if self._active_cond is None:
            self._active_cond = asyncio.Condition()
        async with self._active_cond:
            while self._active_ops > 0:
                await self._active_cond.wait()

    # 提供给 worker 内部用（与 access 不同：worker 已经通过其他锁串行）
    async def begin_op_internal(self) -> None:
        await self._begin_op_internal()

    async def end_op(self) -> None:
        await self._end_op()
