# 技术方案 — v0.21.1 Action Skill 与记忆系统职责调整

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-06-18

---

## 1. 方案概述

把 `db_conn`、`embedder`、`action_skill_system` 三个目录物理迁移到 `memory_system/` 下，让 `MemoryManager` 重新成为记忆系统的统一入口；同步把 `ActionSkillManager.get_skill_index` 的 RAG 输出从「技能维度 top 10 摘要」改为「ActionSequenceTemplate 维度 top 5 完整模板」，使 Agent 在收到输入的当轮即可直接执行。

## 2. 影响范围

| 层级 | 模块 / 路径 | 变更类型 |
|------|------------|----------|
| Python | `Src/PythonServer/db_conn/` | **移动**到 `Src/PythonServer/memory_system/db_conn/` |
| Python | `Src/PythonServer/embedder/` | **移动**到 `Src/PythonServer/memory_system/embedder/` |
| Python | `Src/PythonServer/action_skill_system/` | **移动**到 `Src/PythonServer/memory_system/action_skill_system/` |
| Python | `Src/PythonServer/memory_system/__init__.py` | **新建**（导出 MemoryManager） |
| Python | `Src/PythonServer/memory_system/memory_manager.py` | 修改：内部 import 路径调整、`initialize()` 编排 service 与 ASM、新增 `action_skill` 门面属性 |
| Python | `Src/PythonServer/main.py` | 修改：删除 service / ASM 的显式 initialize，改为单一 `MemoryManager().initialize()`；`ActionSkillManager` import 改走 memory_system 或经 `MemoryManager().action_skill` |
| Python | `Src/PythonServer/agent_framwork/agents/agent_interuptible.py` | 修改：`from action_skill_system...` → `from memory_system.action_skill_system...`；system_template 中 `<动作技能记忆>` 文案；`search_memory._skill_index` 调用与变量语义改为 template 维度 |
| Python | `Src/PythonServer/agent_framwork/managers/agent_manager.py` | 修改：`from db_conn` → `from memory_system.db_conn` |
| Python | `Src/PythonServer/agent/tools/skill_tools.py` | 修改：import 路径调整 |
| Python | `Src/PythonServer/test_action_skill_smoke.py` 等测试脚本 | 修改：import 路径调整（仓库现有 4 个测试文件） |
| Python | `ActionSkillManager.get_skill_index` | 修改：算法从 skill 维度改为 template 维度，输出格式改为完整模板内容；默认 `top_n=5` |
| Python | `ActionSkillManager._format_index` | 修改：输出包含 action_sequence_template / usage_notes |
| 协议 | `Tools/message.proto` | **无变更** |
| Unity | — | **无变更** |

---

## 3. 详细设计

### 3.1 目录结构与 import 路径

迁移后目录：

```
Src/PythonServer/
├── memory_system/
│   ├── __init__.py                      ← 新建：from .memory_manager import MemoryManager
│   ├── memory_manager.py                ← 现状文件，调整 import
│   ├── db_conn/                         ← 由顶层 ./db_conn 整体迁入
│   │   ├── __init__.py
│   │   └── db_connection_service.py
│   ├── embedder/                        ← 由顶层 ./embedder 整体迁入
│   │   ├── __init__.py
│   │   ├── embedder_service.py
│   │   ├── safe_batch_embedder.py
│   │   └── safe_batch_reranker.py
│   └── action_skill_system/             ← 由顶层 ./action_skill_system 整体迁入
│       ├── __init__.py
│       ├── action_skill_manager.py
│       ├── skill_model.py
│       └── default_skill_loader.py
└── ...
```

import 路径替换表（一次性全仓替换，不保留 shim）：

| 旧 | 新 |
|----|----|
| `from db_conn import DBConnectionService` | `from memory_system.db_conn import DBConnectionService` |
| `from embedder import EmbedderService, SafeBatchOpenAIEmbedder, SafeBatchOpenAIReranker` | `from memory_system.embedder import ...` |
| `from action_skill_system import ActionSkillManager, load_default_skills` | `from memory_system.action_skill_system import ...` |
| `from action_skill_system.action_skill_manager import ActionSkillManager` | `from memory_system.action_skill_system.action_skill_manager import ActionSkillManager` |
| `from action_skill_system.skill_model import ActionSkill, ActionSequenceTemplate` | `from memory_system.action_skill_system.skill_model import ...` |

迁移后包内部相对 import（如 `action_skill_manager.py` 内 `from action_skill_system.skill_model import ...`）需同步改成 `from memory_system.action_skill_system.skill_model import ...`，或改为相对 import `from .skill_model import ...`（推荐相对 import，包内更稳）。

### 3.2 MemoryManager 编排与门面

`memory_system/memory_manager.py` 关键修改：

```python
class MemoryManager:
    def __init__(self):
        ...
        self._action_skill = None
    @property
    def action_skill(self):
        return self._action_skill

    async def initialize(self):
        if self._initialized:
            return self
        async with self._init_lock:
            if self._initialized:
                return self
            await DBConnectionService().initialize()
            await EmbedderService().initialize()
            dbsvc = DBConnectionService()
            embedder = EmbedderService().get_embedder()
            reranker = EmbedderService().get_reranker()
            ...  # graphiti / driver / FTS / worker
            from memory_system.action_skill_system import ActionSkillManager
            self._action_skill = await ActionSkillManager().initialize()
            self._initialized = True
        return self
```

要点：

- `initialize()` 整体仍然在 `_init_lock` 下幂等。
- `_action_skill` 仅持有 `ActionSkillManager` 单例引用（不破坏单例性），方便外部 `MemoryManager().action_skill` 访问。
- `backup_memory` / `restore_memory` / `delete_current_memory` 三处对 ASM 的访问统一改为 `self._action_skill`，删除原先的 `from action_skill_system.action_skill_manager import ActionSkillManager`（决策 D4）。

### 3.3 main.py 简化

删除：

```python
from action_skill_system import ActionSkillManager, load_default_skills
from db_conn import DBConnectionService
from embedder import EmbedderService

await DBConnectionService().initialize()
await EmbedderService().initialize()
await asyncio.gather(MemoryManager().initialize(), ActionSkillManager().initialize())
```

替换为：

```python
from memory_system import MemoryManager
from memory_system.action_skill_system import load_default_skills

await MemoryManager().initialize()
```

`handle_agent_create_request` 中默认技能注入处改为 `await MemoryManager().action_skill.create_skill_from_dict(...)`（决策 D3 + D4）。`handle_agent_export_skills_request` 中 `ActionSkillManager().export_skills_yaml / get_all_skills` 改为 `MemoryManager().action_skill.export_skills_yaml / get_all_skills`。

### 3.4 ActionSkillManager.get_skill_index 算法调整

```python
async def get_skill_index(
    self,
    group_id: str,
    query: str = "",
    top_n: int = 5,
) -> str:
    skills = await self.get_all_skills(group_id)
    if not skills:
        return ""
    # 摊平成 (skill, template) 列表
    pairs = [(sk, t) for sk in skills for t in sk.templates]
    if not pairs:
        return ""
    if len(pairs) <= top_n or not query:
        ranked = pairs
    else:
        query_emb = await self._embed(query)
        if not query_emb:
            ranked = pairs[:top_n]
        else:
            scored = []
            for sk, t in pairs:
                sc = 0.0
                if t.description_embedding:
                    sc = _cosine_similarity(query_emb, t.description_embedding)
                scored.append(((sk, t), sc))
            scored.sort(key=lambda x: x[1], reverse=True)
            ranked = [pair for pair, _ in scored[:top_n]]
    return self._format_template_index(ranked)
```

`_format_template_index(pairs)` 新方法（保留旧 `_format_index` 给 `get_skill_list` 用，技能列表场景仍按技能维度）：

```python
def _format_template_index(pairs: List[Tuple[ActionSkill, ActionSequenceTemplate]]) -> str:
    lines = []
    for i, (sk, t) in enumerate(pairs, 1):
        lines.append(f"{i}. 模板：{t.name}")
        lines.append(f"   适用：{t.description}")
        if t.action_sequence_template:
            lines.append("   动作序列：")
            for step in t.action_sequence_template:
                lines.append(f"     - {json.dumps(step, ensure_ascii=False)}")
        if t.usage_notes:
            lines.append(f"   使用注意：{t.usage_notes}")
        lines.append(f"   所属技能：[{sk.name}] {sk.description}")
        lines.append("")
    return "\n".join(lines).rstrip()
```

要点（按 PRD §FR-2.2 决策）：

- **顺序固定**：模板名 → 适用 → 动作序列 → 使用注意 → 所属技能。
- 同一技能下多个模板可能同时入选 top 5，重复展示「所属技能」也无妨。
- `action_sequence_template` 是 `List[dict]`，按行 dump 成 JSON 便于 Agent 直接复用。
- `usage_notes` 原样输出。
- `get_skill_list`（`list_action_skills` 工具用）仍使用旧 `_format_index`，按技能维度展示，不受影响。

### 3.5 agent_interuptible.py 调整

system_template 中 `<动作技能记忆>` 段落改为：

```
<动作技能记忆>
{mem_skill_index}

以上是当前可能用到的动作序列模板。如果其中某个模板与当前场景匹配，
把参数（{xxx} 占位符 / objects[N] 序号 / displacement 距离等）替换为当前场景的实际值，
直接调用 plan_action_sequence 一次性执行，无需先 load。
如果不太匹配或想知道你掌握的全部技能，可以用 list_action_skills 查阅完整列表，
用 load_action_skill 拉取某个技能的所有模板。
</动作技能记忆>
```

`search_memory._skill_index` 的内部调用改为走门面：

```python
skill_top_n = int(os.getenv("SKILL_INDEX_TOP_N", "5"))
r = await MemoryManager().action_skill.get_skill_index(
    group_id=group_id, query=query, top_n=skill_top_n
)
```

兜底文案 `"（暂无掌握的技能）"` 沿用。`mem_skill_index` 字段语义保持不变（仍是字符串），只是内容粒度变化。

### 3.6 兼容性与回滚

- 不保留旧顶层包 shim：迁移后旧 import 路径直接报错，强制全仓改齐，避免一边新一边旧。
- 回滚方式：保留迁移前的 git 提交点，必要时 revert。
- `db/`、`db/backups/`、`db/default_skills/` 路径不变，无数据迁移。

---

## 4. 实现步骤

1. **目录迁移**（一次性 git mv）：
   - `db_conn/` → `memory_system/db_conn/`
   - `embedder/` → `memory_system/embedder/`
   - `action_skill_system/` → `memory_system/action_skill_system/`
2. 新建 `memory_system/__init__.py` 暴露 `MemoryManager`。
3. 调整三个迁入包内部 import：把 `from action_skill_system.xxx` / `from db_conn` / `from embedder` 改为相对 import 或新绝对 import。
4. 全仓 grep + 替换以下 import：`from db_conn`、`from embedder`、`from action_skill_system`、`import db_conn`、`import embedder`、`import action_skill_system`。
5. `MemoryManager.initialize()` 内串行 await `DBConnectionService` / `EmbedderService` / Graphiti / `ActionSkillManager`；新增 `action_skill` 属性。
6. `main.py` 简化为只调 `MemoryManager().initialize()`；默认技能注入与导出路径统一改为 `MemoryManager().action_skill`（决策 D3 + D4），`main.py` 内不再 `import ActionSkillManager`。
7. 修改 `ActionSkillManager.get_skill_index` 算法与 `_format_template_index`；默认 `top_n=5`。
8. 修改 `agent_interuptible.py` 的 system_template 文案与 `_skill_index` 默认 top_n。
9. 同步修改 `agent/tools/skill_tools.py`、`agent_framwork/managers/agent_manager.py` 与所有测试脚本的 import。
10. 自测（按 PRD §8 与本方案 §6）：
    - `uv run python -c "import main"` 不报错。
    - 跑 `test_action_skill_smoke.py`、`test_action_skill_real_embed.py`、新 `test_v021_1_memory_facade.py`，全部通过。
    - 启动 `python main.py`，观测启动日志正常。
11. 联调（可选）：Unity 端发一句"我看到浮板想过河"，看 Agent 当轮 prompt 包含完整模板、Agent 直接发起 `plan_action_sequence` 调用。
12. 更新 PRD / solution 状态：开发完成提交后由「已确认」改为「已实现」（验收通过后）。

---

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 一次性大范围替换 import 路径，遗漏导致运行时 ImportError | 步骤 4 完成后用 `rg "from (db_conn\|embedder\|action_skill_system)"` 复查；步骤 10 用 `import main` 静态检测 + 跑现有 smoke 测试 |
| top 5 模板内容大幅扩张 system prompt token | `chatbot` 节点已有 `trim_messages_by_token`，会按 system_prompt + tools tokens 自动裁剪历史；监控 prompt 实际 token 数（`PROMPT_SAVE_ENABLED=1` 时输出文件） |
| 模板的 `action_sequence_template` 包含的中文字符 / 嵌套 dict 让 RAG 文本变难读 | 使用 `json.dumps(step, ensure_ascii=False)` 保中文；每步独立成行；必要时进一步格式化（如缩进） |
| `MemoryManager` 初始化内部串行调用增长，启动时间略增 | 现状已串行（gather 也只是与 ASM 并行），实测启动期 ASM 初始化只是 schema 检测，毫秒级，影响可忽略 |
| ASM RAG 输出格式变化导致旧的 prompt log 对比丢失 | 仅 dev 期日志对比，可接受；重新跑一次回归即可 |

---

## 6. 测试建议

详细用例已在 PRD §8 列出，本节给出实现指导。

### 6.1 新增自动化测试 `Src/PythonServer/test_v021_1_memory_facade.py`

按 PRD §8.2 的 TC-1 ~ TC-6 实现，组织建议：

```python
# test_v021_1_memory_facade.py
import asyncio, os, sys
from datetime import datetime

# pytest 友好；也可直接 `uv run python test_v021_1_memory_facade.py`
import pytest

from memory_system import MemoryManager
from memory_system.action_skill_system import ActionSkillManager, load_default_skills
from memory_system.action_skill_system.skill_model import ActionSkill, ActionSequenceTemplate
from memory_system.db_conn import DBConnectionService

GROUP = "test_v021_1".encode("utf-8").hex()
NOW = "2016-01-01 00:00:00"

@pytest.mark.asyncio
async def test_initialize_idempotent():
    mm1 = await MemoryManager().initialize()
    mm2 = await MemoryManager().initialize()
    assert mm1 is mm2
    assert MemoryManager().action_skill is ActionSkillManager()

@pytest.mark.asyncio
async def test_facade_crud():
    mm = await MemoryManager().initialize()
    skill = ActionSkill(
        name="测试技能",
        description="for-tests",
        content="测试内容",
        templates=[ActionSequenceTemplate(
            name="测试模板",
            description="测试场景",
            action_sequence_template=[{"action": "wait"}],
            usage_notes="无",
        )],
    )
    await mm.action_skill.create_skill(GROUP, skill, curtime=NOW)
    got = await mm.action_skill.get_skill(GROUP, "测试技能")
    assert got is not None and got.templates and got.templates[0].name == "测试模板"
    await mm.action_skill.delete_skill(GROUP, "测试技能")

@pytest.mark.asyncio
async def test_skill_index_template_format():
    mm = await MemoryManager().initialize()
    # 注入 default_2.yaml（含 2 个技能 共 2 个模板）
    skills = load_default_skills(group_id=GROUP, path=os.path.join(
        os.path.dirname(__file__), "db", "default_skills", "default_2.yaml"))
    for s in skills:
        try:
            await mm.action_skill.create_skill_from_dict(group_id=GROUP, skill_data=s, curtime=NOW)
        except Exception:
            pass
    text = await mm.action_skill.get_skill_index(GROUP, query="看到浮板想过河", top_n=5)
    # 顺序断言：模板信息在前，所属技能在后
    pos_template = text.find("模板：")
    pos_apply    = text.find("适用：")
    pos_seq      = text.find("动作序列：")
    pos_notes    = text.find("使用注意：")
    pos_skill    = text.find("所属技能：")
    assert 0 <= pos_template < pos_apply < pos_seq < pos_notes < pos_skill
    # 浮板模板应该出现且排第 1
    head = text.splitlines()[0]
    assert "单浮板陷阱场景" in head
    # 模板总数 ≤ 5：返回所有模板
    assert text.count("模板：") == len(_flatten_template_count(GROUP) or [1, 2])  # 数量校验由后续 cleanup 完成

@pytest.mark.asyncio
async def test_skill_index_empty():
    mm = await MemoryManager().initialize()
    EMPTY = "no_skill_group".encode("utf-8").hex()
    assert await mm.action_skill.get_skill_index(EMPTY, query="anything") == ""

@pytest.mark.asyncio
async def test_backup_restore_delete_facade_alive():
    mm = await MemoryManager().initialize()
    await mm.backup_memory(0)
    await mm.restore_memory(0)
    # restore 后 action_skill 应仍指向有效单例
    assert mm.action_skill is ActionSkillManager()
    _ = await mm.action_skill.get_all_skills(GROUP)
    await mm.delete_current_memory()
    _ = await mm.action_skill.get_all_skills(GROUP)
```

要点：

- 用 `pytest-asyncio`（项目已使用，参见 `test_action_skill_smoke.py`）。
- 模板数量超过 5 的 TC-3 第二段：可在测试内追加 6 个临时技能/模板（每个 1 个模板）后断言 `text.count("模板：") == 5` 且包含目标模板；细节交给实现阶段。
- `cleanup` 用例间用 `delete_skill` 或独立 group_id 隔离，避免互相污染。

### 6.2 现有测试回归

- `test_action_skill_smoke.py`、`test_action_skill_real_embed.py`、`test_backup.py` 改 import 后跑通。

### 6.3 静态导入烟测

```bash
uv run python -c "from memory_system import MemoryManager; from memory_system.action_skill_system import ActionSkillManager, load_default_skills; from memory_system.db_conn import DBConnectionService; from memory_system.embedder import EmbedderService; print('ok')"
```

### 6.4 联调（可选，非门槛）

- Unity 续玩浮板关卡 → 用户发"我想过河看看" → 终端 prompt 段 `<动作技能记忆>` 含完整模板 → Agent 当轮 `plan_action_sequence` tool_call。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-06-17 | 已完成目录迁移、`MemoryManager.action_skill` 门面、模板维度 top5 RAG、prompt 调整与 `test_v021_1_memory_facade.py` 自测脚本。 |
| 2026-06-18 | 用户验收通过，方案状态更新为「已实现」。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
