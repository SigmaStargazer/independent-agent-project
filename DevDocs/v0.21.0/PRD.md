# PRD — v0.21.0 Action Skill 经验学习系统

> **状态**：已确认
> **对应需求**：`requirements/Action_Skill_需求文档.md`、`requirements/AIPlayer动作系统演变.md` + 用户追加需求
> **最后更新**：2026-06-17

---

## 1. 背景与目标

### 1.1 核心问题

Agent（LLM）缺乏经验，无法一次性规划出完整的多阶段动作序列。以"坐浮板过河"为例，LLM 每次只规划当前一步的 action_sequence，需要玩家反复提示才能完成跨阶段行为。但 action_sequence 执行引擎本身已支持多阶段自主推进——问题不在执行层，而在规划层。

### 1.2 目标

建立 **Action Skill 经验学习系统**，让 LLM 从"每次从零规划"进化为"识别场景→调用经验→参数化执行"。核心思路：

- **让 LLM 做它擅长的事**：理解意图、判断场景、做决策
- **让经验模板做 LLM 不擅长的事**：提供完整的多阶段运动规划
- **让 action_sequence 做它擅长的事**：精确执行具体的物理动作

---

## 2. 范围

### 2.1 本期包含

- Action Skill 数据模型与存储（含 1:N 归类设计，ActionSkill → ActionSequenceTemplate）
- 7 个工具函数：create / add_template / load / list / refine / delete_skill / delete_template
- System Prompt 技能索引动态注入（RAG 匹配 top N，渐进式披露）
- action_sequence 完成后自动触发动作序列回顾（方案 A：Unity 端追加）
- per-agent 技能库 + 默认技能注入（含训练场景技能提取机制）
- 技能持久化（随记忆备份/恢复一起存档）
- 独立模块目录 `action_skill_system/`，工具放在 `agent/tools/`

### 2.2 本期不包含

- Condition 变量体系扩展
- 基于使用统计的自动淘汰机制
- Unity 侧大改动（仅 AIPlayer.cs 小改追加回顾提示）
- action_sequence → action_tree 升级
- 动作序列回顾方案 B
- agent_interuptible.py 拆分

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| Agent（首次遇到浮板） | 玩家提示后逐步完成过河 | Agent 自动总结出"坐浮板过河"技能 |
| Agent（再次遇到浮板） | 看到技能索引匹配 | 加载模板→参数化→一次性下发，零玩家提示 |
| Agent（浮板过河失败） | 使用模板后执行失败 | Agent 反思后精进对应模板 |
| Agent（同类场景多次经历） | 完成了3次略有不同的操作 | 系统引导将3次经验归入同一技能的不同模板 |
| 开发者 | 新关卡有典型操作模式 | 训练 Agent 后提取习得技能，筛选调整后作为默认技能 |

---

## 4. 功能需求

### 4.1 Action Skill 数据模型（1:N 归类）

```python
class ActionSkill:
    uuid: str                               # 唯一标识（主键）
    name: str                               # 技能名称，如"乘坐移动平台"（同一 group_id 下唯一，由代码保证）
    group_id: str                           # Agent 分区
    description: str                        # 简短描述，用于技能索引（渐进式披露第一层）
    content: str                            # 详细描述，加载技能时才展示给 Agent
    version: int                            # 精进版本号
    source: str                             # "default" | "learned" | "refined"
    created_at: str                         # 创建时间（虚拟时间）
    updated_at: str                         # 最后修改时间（虚拟时间）
    templates: List[ActionSequenceTemplate] # 动作序列模板列表（1:N）

class ActionSequenceTemplate:
    uuid: str                               # 唯一标识（主键，不对 Agent 暴露）
    skill_uuid: str                         # 所属技能的 uuid（外键关联 ActionSkill.uuid）
    name: str                               # 模板名称，如"近岸上浮板"（Agent 通过此名称引用模板）
    group_id: str                           # Agent 分区
    description: str                        # 本模板的描述（如"当平台停在近岸时使用"）
    action_sequence_template: List[dict]    # 该场景下的 action_sequence 模板（含参数占位符）
    usage_notes: str                        # 使用注意事项（使用场合、填参注意事项、从过往经历总结的经验等）
    created_at: str                         # 创建时间（虚拟时间）
    updated_at: str                         # 最后修改时间（虚拟时间）
```

**主键 / 唯一约束 / 外键设计**：

| 实体 | 主键 | 业务唯一约束（代码保证） | 外键关联 |
|------|------|-------------------------|---------|
| ActionSkill | `uuid` | `(name, group_id)` 唯一 | — |
| ActionSequenceTemplate | `uuid` | `(name, skill_uuid)` 唯一（同一技能下模板名不重复） | `skill_uuid` → ActionSkill.uuid |

- 数据库主键统一用 uuid，符合规范性
- Agent 操作仍通过 name 定位（`skill_name` / `template_name`），底层先按 name 查 uuid，再用 uuid 操作
- 业务唯一性（name 不重复）由 ActionSkillManager 在写入前检查，不依赖数据库唯一约束

**`name` 字段说明**：

- ActionSkill.name 由 Agent 在 create 时指定，同一 group_id 下唯一
- ActionSequenceTemplate.name 由 Agent 在 create/add_template 时指定，同一技能下唯一
- Agent 在所有工具中只看到 name，不接触 uuid

**source 字段的导出/导入规则**：

导出时保留原始 source 值（default/learned/refined 全部导出）。导入时统一将 source 设为 `"default"`——因为这些技能经过开发者筛选调整后，作为新 Agent 的默认技能注入，身份就是"默认技能"。导出时保留原始 source 是为了让开发者知道哪些是原本的默认技能、哪些是训练中习得的、哪些是精进的，方便筛选。

**1:N 关联方式**：

- ActionSkill 主键：`uuid`（业务唯一：`(name, group_id)`）
- ActionSequenceTemplate 主键：`uuid`，外键 `skill_uuid` 指向 ActionSkill.uuid（业务唯一：`(name, skill_uuid)`）
- 一个 ActionSkill 可有多个 ActionSequenceTemplate

**索引展示格式**：

```
<动作技能记忆>
1. [乘坐移动平台] 乘坐移动平台越过深渊到达对岸
   - 近岸上浮板：当平台停在近岸时
   - 远岸上浮板：当平台停在远岸时
2. [走到目标旁交互] 走到某个可交互物体旁边并进行交互
   - 平地接近：当目标在平地上时
   - 垫脚接近：当目标在高处需要垫脚时
</动作技能记忆>
```

### 4.2 工具函数（7 个）

**设计原则**：工具描述面向游戏世界中的角色，而非冰冷的聊天机器人。文风应像一个人类大脑内的基础功能——自然、直觉、生活化。参数使用 `template_name` 而非 `template_id`。

所有涉及写入的工具函数，内部调用 ActionSkillManager 时传入 `curtime`（从 `TimeSystem` 获取虚拟时间）。

#### 4.2.1 `create_action_skill`

将一种新的行为模式总结为技能，同时记录下第一个使用场景的动作序列模板。

```
参数：
- skill_name: str                      # 技能名称
- description: str                     # 简短描述（用于技能索引）
- content: str                         # 详细说明
- template_name: str                   # 首个模板的名称
- template_description: str            # 首个模板的描述
- action_sequence_template: str        # 首个模板的动作序列模板（JSON）
- usage_notes: str                     # 首个模板的使用注意事项
```

若技能名已存在，提示使用 `add_action_skill_template` 追加。

#### 4.2.2 `add_action_skill_template`

为你已经掌握的某个技能，添加一个新的使用场景下的动作序列模板。

```
参数：
- skill_name: str                      # 已有的技能名称
- template_name: str                   # 新模板的名称
- template_description: str            # 新模板的描述
- action_sequence_template: str        # 新模板的动作序列模板（JSON）
- usage_notes: str                     # 新模板的使用注意事项
```

若技能不存在，提示先使用 `create_action_skill` 创建。若同一技能下已有同名模板，返回错误。

#### 4.2.3 `load_action_skill`

回想某个技能的完整细节，包括所有使用场景下的动作序列模板。

```
参数：
- skill_name: str                      # 要回想的技能名称

返回：
- 技能的详细说明(content)
- 所有模板列表（每个含名称、描述、动作序列模板、使用注意事项）
```

#### 4.2.4 `list_action_skills`

回顾自己掌握的所有技能概况。

```
参数：无

返回：
- 所有技能列表（每个含名称、描述）
- 每个技能下的模板摘要（每个仅含名称、描述）
```

#### 4.2.5 `refine_action_skill`

根据实践经验精进已有技能的某个方面。

```
参数：
- skill_name: str                      # 要精进的技能名称
- template_name: str = ""              # 要精进的模板名称（留空=仅精进技能说明）
- new_content: str = ""                # 更新后的技能详细说明（可选）
- new_template_description: str = ""   # 更新后模板的描述（可选）
- new_template: str = ""               # 精进后的动作序列模板（可选，JSON）
- new_usage_notes: str = ""            # 更新后的使用注意事项（可选）
- reason: str                          # 精进原因
```

若指定了 `template_name` 且 `new_template` 非空，直接覆盖该模板的 `action_sequence_template`，**不保留旧模板**（精进次数由 ActionSkill.version 字段记录，无需保留每个历史版本）。version +1。

#### 4.2.6 `delete_action_skill`

遗忘某个不再需要的技能及其所有模板。

```
参数：
- skill_name: str                      # 要遗忘的技能名称
- reason: str                          # 遗忘原因
```

#### 4.2.7 `delete_action_skill_template`

遗忘某个技能中特定场景下的模板。

```
参数：
- skill_name: str                      # 技能名称
- template_name: str                   # 要遗忘的模板名称
- reason: str                          # 遗忘原因
```

如果删除的是技能下最后一个模板，**仍允许删除**，但返回提示："该技能下已无任何模板，是否考虑遗忘整个技能"。Agent 看到提示后自行决定是否调用 `delete_action_skill`。

### 4.3 System Prompt 技能索引注入（渐进式披露 + RAG）

**第一层（system prompt，始终可见）**：技能索引，轻量

在 `search_memory` 节点中，紧跟 `<回想>` 区块（含 `mem_fact`、`mem_episode`）之后注入 `<动作技能记忆>` 区块。

**RAG 匹配规则**：

- **query 来源**：使用最后一条用户/环境消息作为 query（与 `search_memory` 现有的事实/情景检索一致）
- **匹配对象**：对每个 ActionSequenceTemplate 的 `description` 做 embedding 相似度匹配
- **embedding 存储**：在 Kuzu 的 `ActionSequenceTemplate` 节点上额外存储 `description_embedding` 字段（向量），创建/精进模板时计算并保存。避免每次推理时实时 embed，性能更好
- **去重**：一个技能可能因多个模板被召回，按 ActionSkill 去重，**取该技能下所有模板的最高分**作为该技能的代表分数
- **top N**：默认 10，可配置；技能总数 ≤ N 时全量注入，不做 RAG
- **不设最低相似度阈值**：有技能就返回 top N，避免引入需调参的相似度阈值
- **并发执行**：技能索引 RAG 与事实/情景检索在 `search_memory` 节点中通过 `asyncio.gather` 并发执行

**索引使用规则文本**：写在 system_template 的固定位置（紧跟 `<动作技能记忆>` 区块），不在每次 RAG 返回结果中重复拼接：

```
<动作技能记忆>
{动态注入的索引列表}
</动作技能记忆>

当你觉得当前场景匹配某个技能时，先调用 load_action_skill 回想完整细节，
选择最合适的模板，将参数替换为当前场景的具体值，
通过 plan_action_sequence 一次性执行。如果没有匹配的技能，则照常自主规划。
如果索引中没有匹配的，可以调用 list_action_skills 回顾完整技能列表。
```

**第二层（按需回想）**：`load_action_skill`

**第三层（全量回顾）**：`list_action_skills`

### 4.4 动作序列回顾

**本期采用方案 A**：在 Unity 端动作序列完成/中断时，直接在 SendFeedback 消息末尾追加回顾提示。不在 Python 端做字符串匹配。

```
<动作序列回顾>
你刚刚完成或中止了一次动作序列执行。请回顾这次经验：
- 如果这是一个新的行为模式，值得未来复用 → 调用 create_action_skill 总结为技能
- 如果已有类似技能但这次发现了新的使用场景 → 调用 add_action_skill_template 添加新模板
- 如果已有类似模板但这次发现了改进点 → 调用 refine_action_skill 精进
- 如果只是简单常规操作，不值得记住 → 无需操作

中止的序列也值得总结——分析失败原因并精进模板可能避免下次失败。
保存时请将具体参数替换为描述性占位符，以便未来复用。
</动作序列回顾>
```

**方案 B（备选）**：在 `UserSendFeedbackRequest` 新增 `feedback_type` 字段，Python 端 chatbot 节点根据类型注入 system prompt。

### 4.5 默认技能与训练场景提取

#### 4.5.1 初始技能注入

在 `main.py` 的 `handle_agent_create_request` 中，`acreate_agent` 成功后注入默认技能。不在 AgentManager 内部调用，保持职责分离。

```python
result = await AgentManager().acreate_agent(name=name, summary=desc, create_time=cur_time)
group_id = name.encode('utf-8').hex()
try:
    default_skills = load_default_skills()
    for skill_data in default_skills:
        await ActionSkillManager().create_skill_from_dict(group_id, skill_data, curtime=cur_time)
except Exception as e:
    # 默认技能注入失败仅记录日志，不回滚 Agent 创建——技能注入是辅助功能
    print(f"[main] 默认技能注入失败（Agent 创建已完成）: {e}")
```

导入时统一将 source 设为 `"default"`——因为这些技能经过开发者筛选调整后，作为新 Agent 的默认技能注入，身份就是"默认技能"。

#### 4.5.2 训练场景技能提取

导出时保留所有 source 类型（default/learned/refined），开发者手动筛选调整后作为默认技能配置。

### 4.6 工具失败处理

所有工具函数失败时，返回**描述性错误字符串**给 Agent（与现有工具一致），而非抛出异常。错误信息应包含：失败原因 + 后续建议（如"技能名 'xxx' 不存在，请先用 create_action_skill 创建"）。这样 Agent 看到错误后可自主决策下一步。

### 4.7 技能与记忆的融合

技能存储在 Kuzu 图库中，与 Agent 记忆共享 `group_id` 分区，备份/恢复/删除时一起处理。

---

## 5. 非功能需求

- **prompt 开销**：top 10 技能索引约 300-500 字
- **工具调用开销**：本地操作（读写 Kuzu），延迟极低
- **兼容性**：Skill 系统产出标准 action_sequence
- **可观测性**：技能操作写入 `mem_to_save`
- **索引可配置**：top N 可配置，默认 10
- **可自测**：不依赖 Unity 联调即可测试全部功能

---

## 6. 测试用例

| 用例 | 操作 | 期望结果 |
|------|------|----------|
| T01 创建新技能 | `create_action_skill("坐浮板过河", ...)` | 成功，含 1 个模板，时间有值 |
| T02 重名创建 | `create_action_skill("坐浮板过河", ...)` | 错误，提示用 add_action_skill_template |
| T03 追加模板 | `add_action_skill_template("坐浮板过河", template_name="远岸上浮板", ...)` | 成功，2 个模板 |
| T04 同名模板 | `add_action_skill_template("坐浮板过河", template_name="近岸上浮板", ...)` | 错误，模板名重复 |
| T05 追加到不存在的技能 | `add_action_skill_template("不存在的技能", ...)` | 错误，提示先 create |
| T06 加载技能 | `load_action_skill("坐浮板过河")` | 返回 content + 所有模板详情 |
| T07 加载不存在的技能 | `load_action_skill("不存在的技能")` | 返回"技能不存在" |
| T08 列出所有技能 | `list_action_skills()` | 返回所有技能名+描述+模板摘要 |
| T09 无技能时列出 | `list_action_skills()`（新 Agent） | 返回空 |
| T10 精进技能说明 | `refine_action_skill("坐浮板过河", new_content=...)` | content 更新，version +1 |
| T11 精进模板 | `refine_action_skill("坐浮板过河", template_name="近岸上浮板", new_template=...)` | 模板被覆盖，version +1，updated_at 更新 |
| T12 删除模板 | `delete_action_skill_template("坐浮板过河", template_name="远岸上浮板")` | 删除成功 |
| T13 删除最后一个模板 | `delete_action_skill_template("坐浮板过河", template_name=最后一个)` | 删除成功，返回提示"该技能下已无任何模板，是否考虑遗忘整个技能" |
| T14 删除技能 | `delete_action_skill("坐浮板过河")` | 删除成功 |
| T15 从配置文件注入 | 创建 Agent 时加载 default_skills.yaml | 技能和模板写入 Kuzu，source 统一为 default |
| T16 导出技能 | `export_skills_yaml(group_id)` | 输出 YAML，source 保留原值 |
| T17 少量技能全量注入 | 技能数 ≤ top_n | 全量注入 |
| T18 多技能 RAG 注入 | 技能数 > top_n | 只返回 top_n |
| T19 备份恢复 | 创建技能 → backup → 删除 → restore | 恢复后技能和模板还在 |
| T20 嵌入向量保存 | create 后查 Kuzu 中的 description_embedding | 字段非空，长度等于 embedder 维度 |
| T21 嵌入向量更新 | refine 模板 description 后 | description_embedding 重新计算 |
| T22 工具失败返回字符串 | `add_action_skill_template("不存在", ...)` | 返回错误描述字符串，不抛异常 |
| T23 默认技能注入失败 | 配置文件格式错误 | Agent 创建仍成功，仅日志记录 |
| T24 source 导入归一 | YAML 中 source=learned，导入后查 Kuzu | source 被改为 default |
| T25 RAG 召回正确性 | 创建 3 个技能（描述差异明显），用与某技能描述相关的 query 触发 RAG | 该技能排第 1 |
| T26 RAG 技能去重打分 | 同技能下 2 模板，1 个高匹配 + 1 个低匹配 | 该技能按高分计算，排序正确 |
| T27 backup 期间禁写 | backup 进行中调用 create_action_skill | 写操作等待 backup 完成后再执行（不报错也不丢数据） |

---

## 7. 验收标准

- [ ] Agent 可以通过 `create_action_skill` 创建新技能（含首个模板）
- [ ] Agent 可以通过 `add_action_skill_template` 为已有技能追加模板
- [ ] 同名技能/同名模板不重复创建
- [ ] Agent 可以通过 `load_action_skill` 加载技能详情和所有模板
- [ ] Agent 可以通过 `list_action_skills` 查看完整技能列表
- [ ] Agent 可以通过 `refine_action_skill` 精进技能或特定模板
- [ ] Agent 可以通过 `delete_action_skill` 删除整个技能
- [ ] Agent 可以通过 `delete_action_skill_template` 删除特定模板
- [ ] 工具参数使用 template_name 而非 template_id
- [ ] 工具描述文风面向游戏角色而非聊天机器人
- [ ] System Prompt 动态注入技能索引（RAG top N）
- [ ] Unity 端动作序列反馈消息中包含回顾提示
- [ ] 创建 Agent 时在 main.py 中注入默认技能（不在 AgentManager 内）
- [ ] 导出时 source 保留原值，导入时统一设为 default
- [ ] 技能数据随记忆备份/恢复一起存档
- [ ] 所有测试用例通过

---

## 8. 下版本考虑

### 8.1 action_sequence → action_tree

### 8.2 动作序列回顾方案 B

### 8.3 agent_interuptible.py 拆分

---

*本文档由 Cursor Agent 根据 `requirements/` 及用户反馈生成，确认前请勿直接据此改代码。*
