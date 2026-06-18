# PRD — v0.21.1 Action Skill 与记忆系统职责调整

> **状态**：已确认
> **对应需求**：`requirements/Action_Skill与记忆系统职责调整.md`
> **最后更新**：2026-06-17

---

## 1. 背景与目标

### 1.1 v0.21.0 解耦决定的回顾与修正

v0.21.0 hotfix 期间将 `DBConnectionService`、`EmbedderService`、`ActionSkillManager` 从 `MemoryManager` 中剥离，由 `main.py` 自行依次初始化，目的是解耦底层资源、消除文件锁残留风险。

经实际开发后发现该解耦考虑欠妥：

1. **实现侧**：`MemoryManager.restore_memory` 与 `delete_current_memory` 中仍然手动调用 `ActionSkillManager().reset_for_reinitialize()` + `initialize()`，因为 backup/restore/delete 都涉及关库重开，schema 必须重建。这部分耦合无法像启动期那样上移到 `main.py`，导致目前是「启动期解耦、运行期偷偷耦合」的不一致状态。
2. **领域侧**：参照认知科学，长期记忆体系包含语义记忆（事实）、情景记忆（情境）、程序性记忆（动作技能）等。`ActionSkillManager` 管理的是 Agent 习得的动作序列模板，本质上属于程序性记忆，应当作为记忆系统的子系统而非平级业务模块。

### 1.2 RAG 内容粒度的修正

当前 `agent_interuptible.search_memory` 节点会调用 `ActionSkillManager.get_skill_index()`，按 query 检索 top 10 技能，把"技能名 + 技能描述 + 模板名/描述"塞进 system prompt（mem_skill_index）。这与 Claude skill 的"渐进式披露"思路一致：先给 Agent 一份目录，再让 Agent 调 `load_action_skill` 拉详情。

但这与本项目用 RAG 的初衷有偏差：

- **渐进式披露**：适合"技能数量多、Agent 有充足思考时间"的场景。优点是 token 占用稳定；缺点是必须额外一轮 `load_action_skill`，慢、贵、容易断。
- **RAG 快反**：适合"角色驻留在虚拟世界、需要对环境快速反应"的场景。优点是收到输入的当轮就能拿到可用的解决方案直接执行；缺点是 prompt 略大。

本项目 Agent 更接近后者：动作技能数量有限、节奏要求高，应当尽量在 RAG 阶段就拿到"能直接填参数 → 一次 `plan_action_sequence` 执行"的完整模板，而不是只拿到模板名。

### 1.3 目标

- 把 `MemoryManager` 重新作为记忆系统的统一入口和门面，对外屏蔽底层 service 与子系统的初始化顺序。
- 把 `action_skill_system`、`db_conn`、`embedder` 在物理目录上归入 `memory_system/` 之下：`action_skill_system` 作为子系统，`db_conn` / `embedder` 作为基础设施。
- 调整动作技能 RAG 的输出粒度：从 "top 10 技能 + 模板名/描述" 改为 "top 5 ActionSequenceTemplate 完整模板 + 所属技能与简介"，让 Agent 在首轮就能直接执行。

---

## 2. 范围

### 2.1 本期包含

- 目录结构调整：`action_skill_system/`、`db_conn/`、`embedder/` 物理迁移到 `memory_system/` 之下；其余子系统沿用原 import 路径风格但根包改为 `memory_system.xxx`。
- `MemoryManager.initialize()` 重新成为记忆系统的统一入口：负责依次拉起 `DBConnectionService`、`EmbedderService`、自身（Graphiti），以及 `ActionSkillManager`。
- `main.py` 移除对 `DBConnectionService`、`EmbedderService`、`ActionSkillManager` 的显式 `initialize()` 调用，仅保留 `await MemoryManager().initialize()`。
- `MemoryManager` 暴露子系统访问入口（如 `MemoryManager().action_skill` 或 `get_action_skill_manager()`），其余业务模块通过 `MemoryManager` 取，不再直接 `from action_skill_system import ActionSkillManager`（保留向后兼容 import 即可，新增代码走门面）。
- 动作技能 RAG 输出从 "skill 维度 top N" 调整为 "template 维度 top 5"：返回 top 5 ActionSequenceTemplate 的完整定义（含动作序列模板、usage_notes），并附其归属技能的 name + description。
- 配套调整：`agent_interuptible.py` 的 system prompt 文案微调（不再强调"先 load 再用"，改为"匹配场景就直接 plan_action_sequence 填参执行；技能详情查阅仍可走 list/load 工具"）。

### 2.2 本期不包含

- 不修改 `Tools/message.proto`，不改 Unity 任何代码。
- 不调整 ActionSkill / ActionSequenceTemplate 的数据模型与 schema。
- 不修改 backup/restore/delete 的整体流程（仅由门面化带来的内部调用调整）。
- 不优化 RAG 算法本身（仍是 query embedding + 模板 description embedding 余弦相似度）。
- 不删除现有 `load_action_skill` / `list_action_skills` 工具（仍保留作为 Agent 兜底查阅手段）。
- 不调整 default_skills 的 YAML 数据格式与默认技能注入流程。
- 不拆分 `agent_interuptible.py`。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 开发者（启动 Python 服务器） | 阅读 main.py，希望快速理解记忆系统的初始化结构 | 仅看到 `await MemoryManager().initialize()` 一行，知道这就是记忆体系的入口 |
| 开发者（新增子系统） | 想加入新的记忆相关子系统（如 procedural memory 的某种扩展） | 在 `memory_system/` 下新增子目录，由 `MemoryManager` 统一初始化与管理 |
| Agent（驻留在虚拟世界，收到环境消息） | 输入与某个动作技能模板高度相关 | 当轮 system prompt 中已带完整可执行模板，无需先 `load_action_skill` 再 `plan_action_sequence`，可一次 tool call 直接执行 |
| Agent（输入与所有模板都不太匹配） | 看到 RAG 注入的是"对当前没什么用的"模板 | 模型仍可选择忽略并自主规划，必要时用 `list_action_skills` 检索完整列表 |
| Agent（同一技能下有多个模板） | 多个模板 description 都接近匹配 | RAG 给出 top 5 中可能包含同一技能的多个模板，Agent 有清晰对比，能挑最合适的 |

---

## 4. 功能需求

### 4.1 记忆系统门面化（需求一）

**FR-1.1 物理目录迁移**

把以下三个一级模块迁移为 `memory_system` 的子模块：

```
memory_system/
  __init__.py                    ← 暴露 MemoryManager
  memory_manager.py              ← 现状保留，import 路径相应调整
  db_conn/                       ← 由 ./db_conn 迁入
    __init__.py
    db_connection_service.py
  embedder/                      ← 由 ./embedder 迁入
    __init__.py
    embedder_service.py
    safe_batch_embedder.py
    safe_batch_reranker.py
  action_skill_system/           ← 由 ./action_skill_system 迁入
    __init__.py
    action_skill_manager.py
    skill_model.py
    default_skill_loader.py
```

**FR-1.2 import 路径迁移**

- 旧 `from db_conn import DBConnectionService` → 新 `from memory_system.db_conn import DBConnectionService`
- 旧 `from embedder import EmbedderService` → 新 `from memory_system.embedder import EmbedderService`
- 旧 `from action_skill_system import ActionSkillManager, load_default_skills` → 新 `from memory_system.action_skill_system import ActionSkillManager, load_default_skills`
- 全仓库（含 `agent_framwork`、`agent`、`main.py`、测试脚本）一次性替换；保留兼容 shim（顶层 `db_conn/__init__.py` 等）的优先级：默认**不保留** shim，直接全量替换 import；若工作量过大可保留临时 re-export 但不写在 PRD 范围内。

**FR-1.3 MemoryManager.initialize() 统一编排**

`main.main()` 中移除：

```python
await DBConnectionService().initialize()
await EmbedderService().initialize()
await asyncio.gather(MemoryManager().initialize(), ActionSkillManager().initialize())
```

替换为：

```python
await MemoryManager().initialize()
```

`MemoryManager.initialize()` 内部按以下顺序执行（保留幂等性）：

1. `await DBConnectionService().initialize()`
2. `await EmbedderService().initialize()`
3. 现有 Graphiti / driver / FTS / worker 启动逻辑
4. `await ActionSkillManager().initialize()`

**FR-1.4 子系统访问门面**

- `MemoryManager` 提供属性 `action_skill`（懒加载/直接持有 `ActionSkillManager` 单例引用），让外部以 `MemoryManager().action_skill.create_skill(...)` 方式调用，避免新代码再到处 `from ... import ActionSkillManager`。
- 现有直接 import `ActionSkillManager` 的位置（`agent_interuptible.search_memory`、`agent/tools/skill_tools.py`、`main.py` 默认技能注入、测试脚本）：本期一并改为通过 `MemoryManager().action_skill` 访问，但保留对 `ActionSkillManager` 单例语义的兼容（仍是同一实例）。

**FR-1.5 backup / restore / delete 流程内部一致化**

`MemoryManager.backup_memory` / `restore_memory` / `delete_current_memory` 中现有的 `from action_skill_system.action_skill_manager import ActionSkillManager` 改为 `from memory_system.action_skill_system import ActionSkillManager`（或走 `self._action_skill`）。语义不变。

### 4.2 动作技能 RAG 输出调整（需求二）

**FR-2.1 检索粒度改为 ActionSequenceTemplate**

`ActionSkillManager.get_skill_index(group_id, query, top_n=5)` 行为修改：

- 从所有技能下的全部模板取出 `ActionSequenceTemplate` 集合。
- 用 query embedding 与模板 `description_embedding` 做余弦相似度，按分排序取 top 5。
- 模板总数 ≤ 5 或 query 为空：跳过 RAG 全量返回（顺序按技能创建顺序 + 模板创建顺序）。
- 不设最低阈值（与 v0.21.0 一致）。
- 默认 `top_n` 默认值由当前 10 改为 **5**；环境变量 `SKILL_INDEX_TOP_N`（如设置）含义同步改为模板维度。

**FR-2.2 输出文本格式**

按「**先模板、再说明所属技能**」的顺序渲染，每个匹配的模板独立成块。设计意图：模板是 Agent 直接拿来执行的对象，技能名与简介只是辅助分类信息，因此把模板信息前置、技能信息作为归属说明放后。建议格式：

```
1. 模板：单浮板陷阱场景
   适用：场景中有一个自动移动平台在陷阱上方往复运动，需要利用平台跨越陷阱到达对岸。
   动作序列：
     - {"action": "wait", "condition": "objects[6].State == 'Idle'"}
     - {"action": "move", "condition": "displacement >= 5", ...}
     - ...
   使用注意：替换说明：objects[6]中的6替换为... ...
   所属技能：[乘坐自动移动平台跨越陷阱] 通过站在自动移动的平台（浮板）上，利用其往复运动跨越陷阱区域到达对岸

2. 模板：平地接近
   适用：当目标在平地上、可直接走过去时
   动作序列：
     - ...
   使用注意：...
   所属技能：[走到目标旁交互] 走到某个可交互物体附近并与之交互
```

具体格式以 solution.md 中的实现为准；本 PRD 只约束信息项与顺序：**模板名 → 模板 description（适用） → action_sequence_template → usage_notes → 所属技能（name + description）**。技能 `content` 字段不放进索引（仍由 `load_action_skill` 兜底）。

**FR-2.3 system prompt 文案调整**

`agent_interuptible.system_template` 的 `<动作技能记忆>` 段落改为：

- 不再要求"先 load_action_skill 再用"。
- 改为：「以下是当前可能用到的动作序列模板，匹配场景时把参数替换为当前场景的实际值，调用 plan_action_sequence 一次性执行；如果想知道完整技能列表或更多模板，可以用 list_action_skills / load_action_skill 查阅。」
- 兜底文案"（暂无掌握的技能）"沿用。

### 4.3 工具行为不变项

- `create_action_skill` / `add_action_skill_template` / `load_action_skill` / `list_action_skills` / `refine_action_skill` / `delete_action_skill` / `delete_action_skill_template` 七个工具的语义、签名、参数描述均**不变**。
- `plan_action_sequence` 工具不变。

---

## 5. 非功能需求

- **向后兼容**：本版本不要求兼容旧 import 路径；但需要确保仓库内所有引用一致迁移，迁移完成后跑一次 `uv run python -c "import main"` 与现有 smoke 测试不报 import 错。
- **token 预算**：top 5 模板 + action_sequence + usage_notes 约 800-2500 tokens；`agent_interuptible.chatbot` 已使用 `trim_messages_by_token` 自动裁剪历史消息以控制总 prompt，本期不额外加限。
- **性能**：RAG 路径仍为 query embedding（一次） + cosine 比对（O(N)）；总体延迟与 v0.21.0 同量级，不退化。

---

## 6. 验收标准

- [ ] 仓库内所有非测试 / 非历史归档的 `from db_conn` / `from embedder` / `from action_skill_system` import 已替换为 `memory_system.xxx`，全仓 grep 验证。
- [ ] `Src/PythonServer/main.py` 中初始化序列只剩一行 `await MemoryManager().initialize()`（TimeSystem 等非记忆模块照旧）。
- [ ] `MemoryManager().initialize()` 重复调用幂等，启动后底层 service / ASM 单例可用。
- [ ] `MemoryManager().backup_memory(0)` → `restore_memory(0)` → `delete_current_memory()` 三个流程结束后 ASM 可正常 CRUD（自测脚本通过）。
- [ ] Agent 收到一条"看到浮板想过河"的消息时，system prompt 的 `<动作技能记忆>` 段中已包含「单浮板陷阱场景」模板的完整 action_sequence_template + usage_notes，且 Agent 在当轮即可发起 `plan_action_sequence` 工具调用（终端 prompt 输出 + 工具调用日志可观测）。
- [ ] 模板总数 ≤ 5 时返回全部模板；为 0 时返回空字符串。
- [ ] 现有 `test_action_skill_smoke.py` / `test_action_skill_real_embed.py` 在 import 路径调整后仍然全部通过（必要时同步改 import）。
- [ ] PRD / solution 状态从「待确认」流转到「已确认」，开发完成后 solution 流转到「已实现」。

---

## 7. 已敲定决策（原"待确认问题"）

- [x] **D1（原 Q1）**：动作技能 RAG 默认 `top_n = 5`，纯 ActionSequenceTemplate 维度，可能多个模板归属同一技能。
- [x] **D2（原 Q2）**：**不保留**旧顶层 import 路径（`db_conn` / `embedder` / `action_skill_system`）的兼容 shim，一次性全仓替换。
- [x] **D3（原 Q3）**：`MemoryManager` 暴露属性 `action_skill`（不是方法）。
- [x] **D4（原 Q4）**：`MemoryManager` 内部对 `ActionSkillManager` 的访问也走门面属性 `self._action_skill`，不再写 `import ActionSkillManager`。

---

## 8. 测试用例要求

本期必须自测可在不启动 Unity 的情况下覆盖的逻辑（参见 `AGENTS.md` "可自测的功能必须自测完成后再提交验收"纪律）。

### 8.1 静态 import 检查

- 全仓 `rg "from (db_conn|embedder|action_skill_system)\b"` 应只出现在 `memory_system/` 内部及 git 历史归档中。
- `uv run python -c "from memory_system import MemoryManager; from memory_system.action_skill_system import ActionSkillManager, load_default_skills; from memory_system.db_conn import DBConnectionService; from memory_system.embedder import EmbedderService; print('ok')"` 输出 `ok`。

### 8.2 自动化测试脚本（必交付）

新增 `Src/PythonServer/test_v021_1_memory_facade.py`，覆盖：

1. **TC-1 启动入口幂等**：连续两次 `await MemoryManager().initialize()`，第二次直接返回，无副作用；底层 `DBConnectionService` / `EmbedderService` / `ActionSkillManager` 均处于 initialized=True。
2. **TC-2 门面属性可用**：`MemoryManager().action_skill is ActionSkillManager()`（同一单例）；通过 `MemoryManager().action_skill.create_skill(...)` 能成功写入并通过 `get_skill` 读出。
3. **TC-3 RAG 模板维度 top 5**：构造一个测试 group_id，注入 `default_2.yaml`（含 2 技能 2 模板）。
   - 模板总数 ≤ 5：query 任意，返回全部模板，且文本顺序为「模板信息 → 所属技能」。
   - 模板总数 > 5（额外注入 6 个不同模板）：query="看到浮板想过河"，返回恰好 5 个模板，且「单浮板陷阱场景」排第 1。
4. **TC-4 RAG 输出格式**：返回字符串中按顺序包含子串 `模板：` → `适用：` → `动作序列：` → `使用注意：` → `所属技能：`；且不出现旧格式标志 `[技能：` 开头作为第一行。
5. **TC-5 空场景**：group_id 下无任何技能时，`get_skill_index` 返回 `""`。
6. **TC-6 backup / restore / delete 后门面仍可用**：依次调 `MemoryManager().backup_memory(0)` → `restore_memory(0)` → `delete_current_memory()`，每步后调 `MemoryManager().action_skill.get_all_skills(...)` 都不抛异常。

### 8.3 现有测试回归

- `test_action_skill_smoke.py`、`test_action_skill_real_embed.py` 在 import 调整后全部通过。
- `test_backup.py`（如存在）通过。

### 8.4 联调（可选，非门槛）

- Unity 续玩含浮板关卡，发"我想过河看看"，观察终端输出中 prompt 的 `<动作技能记忆>` 段已含完整模板，Agent 当轮 tool_call 即为 `plan_action_sequence`。

---

*本文档由 Cursor Agent 根据 `requirements/Action_Skill与记忆系统职责调整.md` 生成，确认前请勿直接据此改代码。*
