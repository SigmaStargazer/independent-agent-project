# 技术方案 — v0.21.0 Hotfix: 引入 DBConnectionService 拆分 Kuzu 连接管理

> **状态**：已实现
> **依据 PRD**：无单独 PRD（本次为 v0.21.0 引入 ActionSkill 后暴露的架构 bug 修复）
> **关联主方案**：`solution.md`（Action Skill 经验学习系统）
> **最后更新**：2026-06-17

---

## 1. 方案概述

将 Kuzu 数据库的连接生命周期（Database / AsyncConnection / FTS 扩展 / 冻结门 / 文件路径）从 `MemoryManager` 中剥离到一个新的底层单例 `DBConnectionService`。`MemoryManager` 与 `ActionSkillManager` 不再持有 `self.conn`，每次执行 Cypher 时通过 `DBConnectionService.get_conn()` 临时取连接，从根本上消除"close 时漏切断引用导致文件锁不释放"的问题。

**附带重构**（详见 §10）：把 `memory_system/safe_batch_*.py` 移到新顶级模块 `embedder/`，并新增 `EmbedderService` 单例，使 `MemoryManager` 与 `ActionSkillManager` 平级共享 embedder/reranker，不再相互依赖。

## 2. 背景与动机

### 2.1 故障现象

NewGameFlow 执行到 `BackupMemoryStep(0)` 时报错：

```
[MemoryManager][记忆备份开始] slot=0
[MemoryManager] checkpoint开始
🛑 [1/4] ... ✅ [4/4] 垃圾回收完成，数据库锁应已释放
[MemoryManager][覆盖已有备份] slot=0
[MemoryManager][记忆备份失败] slot=0: [Errno 13] Permission denied
⚠️ [MemorySystem] 数据库打开失败，尝试清理 WAL: 'utf-8' codec can't decode byte 0xb6 in position 61
❌ [MemorySystem] clean start 仍失败: 'utf-8' codec can't decode byte 0xb6 in position 61
```

### 2.2 根因

v0.21.0 引入 `ActionSkillManager` 后，它通过 `initialize(kuzu_conn=...)` 持有了 `MemoryManager.conn` 的强引用：

```
MemoryManager.conn ──┐
                     ├──► AsyncConnection ──► kuzu.Database（持有 mmap 文件锁）
ActionSkillManager._conn ──┘
```

`MemoryManager._close()` 只切断自己手里的引用，没通知 `ActionSkillManager`。Python GC 因为 `ActionSkillManager._conn` 仍存在，无法回收 `Database` 对象，**Windows 上 mmap 文件锁未释放**，`shutil.copy2` 立即报 `Permission denied`。

进而 `finally` 里的 `initialize()` 自动恢复又遇上未完整 close 的数据库残留状态，触发 utf-8 解码错（WAL 文件被异常截断）。

### 2.3 为何不用最小补丁

最小补丁是在 `_close()` 里加一行 `ActionSkillManager().reset_for_reinitialize()`。但：

- 未来还会引入更多 `XxxManager` 共享 Kuzu 连接（如 v0.22+ 计划中的实体管理、事件管理等）
- 每加一个就要改 `MemoryManager._close()`，违反开闭原则
- `MemoryManager` 现在身兼"记忆业务 + 数据库连接持有者"两职责，与 `ActionSkillManager` 之间存在反向引用（`_memory_manager`），耦合方向也别扭

因此本次重构是**治本性架构调整**，而不是 hotfix。

## 3. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python 新增 | `Src/PythonServer/db_conn/__init__.py` | 新文件 |
| Python 新增 | `Src/PythonServer/db_conn/db_connection_service.py` | 新文件 |
| Python 新增 | `Src/PythonServer/embedder/__init__.py` | 新文件 |
| Python 新增 | `Src/PythonServer/embedder/safe_batch_embedder.py` | 从 `memory_system/` 移过来 |
| Python 新增 | `Src/PythonServer/embedder/safe_batch_reranker.py` | 从 `memory_system/` 移过来 |
| Python 新增 | `Src/PythonServer/embedder/embedder_service.py` | 新文件 |
| Python 删除 | `Src/PythonServer/memory_system/safe_batch_embedder.py` | 删除（已移走） |
| Python 删除 | `Src/PythonServer/memory_system/safe_batch_reranker.py` | 删除（已移走） |
| Python 改造 | `Src/PythonServer/memory_system/memory_manager.py` | 删除连接管理代码、改用 service；不再 new embedder/reranker；不再 init ASM |
| Python 改造 | `Src/PythonServer/action_skill_system/action_skill_manager.py` | 删除 `_conn`/`_memory_manager`、改用 service |
| Python 改造 | `Src/PythonServer/main.py` | 启动顺序：`DBConnectionService` → `EmbedderService` → `MemoryManager` & `ActionSkillManager`（gather） |
| 文档 | `AGENTS.md` | 第三节「memory_manager.py」与第六节「关键文件索引」补充 DBConnectionService、EmbedderService |
| 文档 | `DevDocs/v0.21.0/solution.md` | 实现记录加一行 hotfix 引用 |
| 协议 | `Tools/message.proto` | 无变更 |

## 4. 详细设计

### 4.1 DBConnectionService 职责

**应该管**：

- `kuzu.Database` 与 `kuzu.AsyncConnection` 生命周期（`initialize()` / `close()`）
- FTS 扩展加载（`_ensure_fts_loaded`）
- 冻结门（`memory_access` / `_freeze` / `_active_ops` / `_active_cond`）
- WAL 清理（open 失败时 fallback）
- 数据库文件路径配置（`db_root` / `db_name`）

**不应该管**：

- Graphiti 实例（属于记忆业务）
- embedder / reranker 配置（属于记忆业务）
- 各业务 Schema 创建（各业务自行调 `get_conn()` 自建表）
- 备份 / 恢复（由 `MemoryManager` 编排，但调用本 service 的 close/open）

### 4.2 单例位置与命名

- 文件：`Src/PythonServer/db_conn/db_connection_service.py`
- 类名：`DBConnectionService`
- 装饰器：复用 `agent_framwork.base.singleton`

### 4.3 公开接口（最小集）

```python
class DBConnectionService:
    # 生命周期
    async def initialize(self) -> "DBConnectionService"
    async def close(self) -> None
    @property
    def is_initialized(self) -> bool

    # 连接获取（D4.A：每次取，不存）
    def get_conn(self) -> kuzu.AsyncConnection      # 业务执行 Cypher 用
    def get_db(self) -> kuzu.Database               # Graphiti 等需要 Database 实例

    # 是否新库（业务用来决定是否首次建 schema）
    @property
    def is_new_db(self) -> bool

    # 冻结门（从 MemoryManager 迁移）
    @asynccontextmanager
    async def access(self): ...                     # 取代 memory_access()
    async def freeze(self): ...                     # backup 期间禁写
    async def unfreeze(self): ...
    async def wait_idle(self): ...                  # 等待 active_ops 归零

    # 文件路径（让 MemoryManager 备份时用）
    @property
    def db_path(self) -> str
    @property
    def wal_path(self) -> str
```

### 4.4 close() 内部流程（治本关键）

```
1. self._initialized = False
2. （可选）让所有持有者通过 access() 自然结束 → wait_idle()
3. self.conn.close()    # 关闭 AsyncConnection 后台线程
4. self.conn = None     # 切断 service 自己的引用
5. self._kuzu_db = None # 切断 Database 引用
6. gc.collect() + sleep(0.5) + gc.collect()
```

注意：MemoryManager 与 ActionSkillManager **不再持有 self.conn**，所以**不需要任何外部回调**。引用只剩 service 一处，gc.collect() 必然回收 Database 对象，文件锁立即释放——这就是 D4.A 选择"每次 get_conn() 临时取"而不是"业务存 self._conn"的核心原因。

### 4.5 MemoryManager 改造

**保留的字段**：

- `graphiti`、`_kuzu_driver`
- `_memory_queue`、`_worker_task`、`_graph_write_lock`
- 备份相关：`_backup_root`、`_max_backup_slots`、`_backup_lock`

**移除的字段**：

- `self._kuzu_db`、`self.conn`（改为 `_dbsvc.get_db()` / `_dbsvc.get_conn()`）
- `self._embedder`、`self._reranker`（改为 `EmbedderService().get_embedder()` / `get_reranker()`）
- `self._freeze`、`self._active_ops`、`self._active_cond`（迁到 service）
- `_begin_memory_op` / `_end_memory_op` / `memory_access`（改为 `_dbsvc.access()`）

**保留的对外 API**（不变）：

- `initialize()`、`close()`、`save_memory()`、`load_agent_summary()`、`init_agent_summary()`
- `search_fact_memory()`、`search_episode_memory()`
- `backup_memory()`、`restore_memory()`、`delete_current_memory()`、`delete_backup_memory()`、`list_used_slots()`
- `wait_memory_flush()`、`memory_access()`（变为 service.access() 的薄转发，保持向后兼容）

**initialize() 新流程**：

```
1. 确保 DBConnectionService 已初始化
2. 确保 EmbedderService 已初始化（拿 embedder/reranker）
3. 用 dbsvc.get_db() / get_conn() 组装 _SharedKuzuDriver 与 Graphiti（embedder/reranker 来自 EmbedderService）
4. ensure_fts_indexes（业务级索引）
5. 启动 _memory_worker
（不再 init ActionSkillManager — 由 main.py 平级编排）
```

**close() 新流程**：

```
1. 停 _memory_worker（队列 drain + cancel）
2. 释放 graphiti、_kuzu_driver 引用（设 None）
3. （不调用 dbsvc.close() — DB 关闭由 main.py 或 backup_memory 编排，因为 ASM 也共享 dbsvc）
```

**backup_memory() 新流程**：

```
1. dbsvc.freeze() + wait_idle()
2. 在记忆侧 flush 队列（drain _memory_queue）
3. await dbsvc.get_conn().execute("CHECKPOINT")
4. 自身 close()  →  dbsvc.close()  # 通过 dbsvc 真正释放文件锁
5. shutil.copy2(dbsvc.db_path → slot_path) + wal
6. finally: dbsvc.initialize() → 自身 initialize() 重新组装 graphiti
```

> 注意：backup 期间需要把 ActionSkillManager 也"重置"。由于 ASM 不再持 conn，只需在 backup 末尾确保 ASM 仍可工作即可（它的 `_conn()` helper 每次现取，dbsvc 重 init 后自然恢复）。但 ASM 内部缓存（如 `_initialized`）需在 dbsvc 关闭前后保持自洽 — 详见 §4.6。

### 4.6 ActionSkillManager 改造

**移除字段**：

- `self._conn`：改为每次执行 Cypher 用 `DBConnectionService().get_conn()`
- `self._embedder`：改为 `EmbedderService().get_embedder()`
- `self._memory_manager`：改用 `DBConnectionService().access()` 上下文，不再反向引用 MemoryManager

**改造点**：

- `initialize()` 签名简化为 `async def initialize(self) -> ActionSkillManager`，不再接收任何参数（dbsvc 与 EmbedderService 都是单例自取）
- 内部所有 `await self._conn.execute(...)` 改为 `await self._conn().execute(...)`（提一个 helper `def _conn(self): return DBConnectionService().get_conn()`）
- `_memory_access()` 删除，改用 `DBConnectionService().access()`
- `reset_for_reinitialize()` 保留但仅清 `_initialized = False`（schema 检测在 `initialize` 内部已是幂等的）

### 4.7 main.py 启动顺序

```python
async def main():
    print("正在初始化基础设施...")
    await DBConnectionService().initialize()
    await EmbedderService().initialize()

    print("正在初始化业务模块...")
    await asyncio.gather(
        MemoryManager().initialize(),
        ActionSkillManager().initialize(),
    )
    print("初始化完成。")

    await TimeSystem().aset_time(year=2016, month=1, day=1)
    ...
```

## 5. 实现步骤

1. **新建 `db_conn` 包** — `__init__.py` 导出 `DBConnectionService`
2. **实现 `DBConnectionService`**
   - 把 `MemoryManager.initialize()` 中"打开 db / 创建 conn / 加 FTS / WAL fallback"等代码迁移过来
   - 把冻结门相关字段与方法迁移过来
   - 实现 `close()`，复制 `MemoryManager._close()` 中"关 conn / 切引用 / gc"逻辑
3. **新建 `embedder` 包**（详见 §10）
   - 把 `memory_system/safe_batch_embedder.py`、`safe_batch_reranker.py` 移到 `embedder/`
   - 删除 `memory_system/` 下的旧文件
   - 新增 `embedder/embedder_service.py`：`EmbedderService` 单例
   - `__init__.py` 导出 `EmbedderService`、`SafeBatchOpenAIEmbedder`、`SafeBatchOpenAIReranker`
4. **改造 `MemoryManager`**
   - 删除连接管理字段与方法，改为读 `DBConnectionService`
   - 删除 embedder/reranker 字段与 new 调用，改为读 `EmbedderService`
   - `initialize()` 头部加 `await DBConnectionService().initialize()` + `await EmbedderService().initialize()`
   - 删除末尾的 `ActionSkillManager().initialize(...)` 调用
   - `_close()` 改为只负责自己（不再 close dbsvc）
   - `backup_memory()` / `restore_memory()` / `delete_current_memory()` 中所有 db 文件路径改为读 `dbsvc.db_path`；底层 close/open 改为 `dbsvc.close()` / `dbsvc.initialize()`
5. **改造 `ActionSkillManager`**
   - 删除 `_conn`、`_embedder`、`_memory_manager` 字段
   - 加 helper `def _conn(self): return DBConnectionService().get_conn()`，`self._conn.execute` 全局替换为 `self._conn().execute`
   - `_embed()` 改读 `EmbedderService().get_embedder()`
   - `_memory_access()` 改为返回 `DBConnectionService().access()`
   - `initialize()` 签名收窄为无参数
6. **改造 `main.py`** 启动顺序（`DBConnectionService` → `EmbedderService` → MM/ASM gather）
7. **跑自测**（见 §7）
8. **更新文档** — `AGENTS.md`、`DevDocs/v0.21.0/solution.md` 实现记录

## 6. 风险与回退

| 风险 | 缓解 |
|------|------|
| `ActionSkillManager` 内 `self._conn.execute` 替换为 `self._conn().execute` 漏改 | 完成后用 grep 检查 `self\._conn[^(]` 必须无残留 |
| Graphiti 依赖 `_SharedKuzuDriver(db, conn)` 在生命周期内有效，重新 initialize 后 driver 引用旧 db | `MemoryManager.initialize()` 每次都重新组装 driver；`close()` 显式置空 |
| `DBConnectionService` 单例与现有进程多次 init 兼容性 | `initialize()` 内部用 `_init_lock` 与 `_initialized` 守门（沿用 MemoryManager 现有模式） |
| backup 流程中"先 close 再 copy"的窗口期 Agent 可能仍试图写记忆 | 已通过 `freeze + wait_idle` 阻塞写入；本次重构不改变该语义 |
| 回退方案 | 全部代码集中在 git 一次提交内，必要时 `git revert` |

## 7. 自测计划（按 D9 整理）

### 7.1 GameFlow 触发顺序梳理（结合代码核对）

**NewGameFlow**（`Src/IndependentAgentProject/.../Flows/NewGameFlow.cs`）：

```
StopAgent          → SceneStopRequest      → aremove_all
DeleteMemory       → MemoryDeleteCurrentRequest → delete_current_memory（close + 删 db + initialize）
CreateAgent        → AgentCreateRequest    → acreate_agent + 默认技能注入（写 ActionSkill 表）
BackupMemory(0)    → MemoryBackupRequest   → backup_memory（freeze + checkpoint + close + copy + initialize）★ 旧版在此报错
SaveData           → 纯 Unity
LoadAgent          → AgentLoadRequest      → aload_agent_all
LoadScene          → 纯 Unity
（无 StartAgent）
```

**ContinueGameFlow**：

```
StopAgent          → SceneStop
RestoreMemory(0)   → MemoryRestoreRequest  → restore_memory（close + 删 db + 复制备份 + initialize）
LoadAgent          → AgentLoadRequest
LoadScene          → 纯 Unity
StartAgent(1)      → SceneStartRequest     → astart_all
```

**NextMapFlow**：

```
InterruptAgent     → AgentInterruptRequest → ainterrupt_all
BackupMemory(0)    → backup_memory ★
SaveData           → 纯 Unity
LoadScene          → 纯 Unity
SaveData
BroadcastMessage   → UserSendMessageAllRequest → asend_message_all
StartAgent(0)      → astart_all
```

### 7.2 自测项（Agent 自验证）

| 编号 | 测试场景 | 期望结果 |
|------|----------|----------|
| T1 | 启动 server，看到日志顺序：`DBConnectionService initialized` → `EmbedderService initialized` → `MemoryManager initialized` 与 `ActionSkillManager initialized`（gather，顺序不固定） | 不报错 |
| T2 | 模拟 NewGameFlow：依次手动调用 `delete_current_memory → acreate_agent → 默认技能注入 → backup_memory(0)` | backup_memory(0) 不报 Permission denied |
| T3 | T2 完成后立即 `restore_memory(0)` | 成功，能 search 到刚创建的 Agent 简介 |
| T4 | NextMapFlow 模拟：连续 3 次 `backup_memory(0)` 覆盖 | 每次都成功，无文件锁残留 |
| T5 | grep `self\._conn[^(]` 在 action_skill_manager.py 中无残留 | 0 匹配 |
| T6 | grep `self\._memory_manager` 与 `self\._embedder` 在 action_skill_manager.py 中无残留 | 0 匹配 |
| T7 | grep `self\.conn` / `self\._kuzu_db` / `self\._embedder` / `self\._reranker` 在 memory_manager.py 中无残留（除 `_kuzu_driver` 外） | 0 匹配 |
| T8 | grep `from memory_system.safe_batch_` 在全仓库无残留 | 0 匹配 |
| T9 | grep `memory_system/safe_batch_embedder.py` / `memory_system/safe_batch_reranker.py` 文件不存在 | 文件已删除 |

T2~T4 可写一个一次性 `_smoke_db_conn_service.py` 脚本跑（不进 git）。

### 7.3 用户验收（Unity 端）

由用户在 Unity 中实际触发：

- [ ] NewGame 一次成功
- [ ] 在场景中与 Agent 对话若干轮后切关（NextMap）成功
- [ ] 退出后 ContinueGame 进入正确关卡，记忆保留

## 8. 不在本次范围

- `MemoryManager` 拆分为"记忆业务" + "Graphiti 编排"两层（更大的重构，待后续版本）
- 数据库支持多实例 / 多路径
- 备份压缩 / 自动清理过期 slot

---

## 9. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-16 | 完成 db_conn / embedder 两个新包；MemoryManager 与 ActionSkillManager 全面改用 service 单例；main.py 启动顺序调整为 dbsvc → embedder → MM&ASM gather；删除 memory_system/safe_batch_*.py；agent_manager.py 中残留的 `MemoryManager().conn.execute` 同步改为 `DBConnectionService().get_conn().execute`。 |
| 2026-06-16 | 一次性 smoke 脚本 `_smoke_db_conn_service.py` T1~T4 全通过：基础设施初始化顺序正确、NewGameFlow 模拟（DeleteMemory + CreateAgent + 默认技能注入 + BackupMemory(0)）不再报 Permission denied、RestoreMemory(0) 后能读到 Agent 简介、连续 3 次 backup_memory(0) 覆盖均成功；脚本运行后已删除。 |
| 2026-06-16 | 修复一处 close 后 `_freeze` 残留导致重新 initialize 后 `access()` 死等的边界情况（`DBConnectionService.close()` 末尾重置 freeze/active_ops）。 |
| 2026-06-17 | Unity 端联调验收通过：NewGameFlow / ContinueGameFlow / NextMapFlow 全流程不再复现 backup 相关错误。本方案标记为「已实现」。 |

---

## 10. 附带：embedder/reranker 重组

### 10.1 现状

`memory_system/safe_batch_embedder.py` 和 `safe_batch_reranker.py` 实际是 Graphiti OpenAI 客户端的 batch-size 适配层（绕过 DashScope 的单次请求条数限制），**与"记忆"业务无关**。放在 `memory_system/` 是历史原因。

v0.21.0 引入 ActionSkill RAG 后，`ActionSkillManager` 也需要 embedder，目前是从 `MemoryManager._embedder` 借的——这造成了与本次主重构相同的反向耦合问题。

### 10.2 调整方案

**目录结构**：

```
Src/PythonServer/embedder/
├── __init__.py                  # 导出 EmbedderService、SafeBatchOpenAIEmbedder、SafeBatchOpenAIReranker
├── safe_batch_embedder.py       # 从 memory_system/ 移过来，内容不变
├── safe_batch_reranker.py       # 从 memory_system/ 移过来，内容不变
└── embedder_service.py          # 新增
```

`memory_system/safe_batch_embedder.py` 和 `safe_batch_reranker.py` **直接删除**，不保留向后兼容（按 D8 约定）。

### 10.3 EmbedderService 设计

```python
@singleton
class EmbedderService:
    def __init__(self):
        self._embedder: Optional[SafeBatchOpenAIEmbedder] = None
        self._reranker: Optional[SafeBatchOpenAIReranker] = None
        self._initialized = False
        self._init_lock: Optional[asyncio.Lock] = None

    async def initialize(self) -> "EmbedderService":
        # 从 .env 读 EMBEDDING_* / RERANKER_* 配置
        # 实例化 SafeBatchOpenAIEmbedder + SafeBatchOpenAIReranker
        # 幂等（_initialized 守门）
        ...

    def get_embedder(self) -> SafeBatchOpenAIEmbedder: ...
    def get_reranker(self) -> SafeBatchOpenAIReranker: ...
```

**职责**：
- 持有共享的 embedder/reranker 实例
- 从 `.env` 读取 `EMBEDDING_API_BASE` / `EMBEDDING_API_KEY` / `EMBEDDING_MODEL` / `RERANKER_*` 配置
- max_batch_size 配置（暂保留硬编码 10，未来可移到 .env）

**不管**：
- 业务调用（embed 单条 / batch / rerank 由 MM、ASM 各自决定怎么用）
- 模型选择策略（一个进程一组模型）

### 10.4 调用方迁移

| 原代码 | 新代码 |
|--------|--------|
| `from memory_system.safe_batch_embedder import SafeBatchOpenAIEmbedder` | （删除，改用 `EmbedderService`） |
| `MemoryManager()._embedder` | `EmbedderService().get_embedder()` |
| `MemoryManager()._reranker` | `EmbedderService().get_reranker()` |
| `ActionSkillManager()._embedder` | `EmbedderService().get_embedder()` |

### 10.5 风险

| 风险 | 缓解 |
|------|------|
| 漏改 `memory_system/safe_batch_*.py` 的引用 | T8、T9 自测项 grep 验证；删文件后跑 `python main.py` 启动期能立即报 ImportError |
| EmbedderService 被 MM/ASM 在 initialize 阶段并发调用 | `_init_lock` + `_initialized` 双重检查（沿用现有模式） |

---

*本文档由 Cursor Agent 生成；**用户确认后** Agent 方可按本方案修改代码。*
