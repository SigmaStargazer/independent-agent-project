# Action Skill 系统设计需求文档

## 1. 背景与问题

### 1.1 问题现象

在游戏关卡测试中，Agent（小明）需要完成"坐浮板过河"的任务。预期流程为：

1. Agent 有了"过到对岸"的念头
2. 运动系统自动完成：上浮板 → 等浮板到对岸 → 从浮板走到岸上

实际测试中，流程变成了：

1. Agent 拉拉杆启动浮板 ✅
2. 玩家提示"到浮板上" → Agent 才开始走上浮板 ❌
3. 玩家提示"上来吧" → Agent 说"你拉拉杆吧"，仍未行动 ❌
4. 玩家提示"上岸吧" → Agent 才查看观察记录、确认浮板已到，然后上岸 ❌

玩家需要反复告诉 Agent 下一步该做什么，严重断心流。

### 1.2 根因分析

Agent 的"大脑"（LLM）采用**刺激-响应**模式运行——仅在收到消息时才被激活思考。当前 action_sequence 执行引擎本身已支持多阶段顺序执行（WaitAction + condition 每帧求值，条件满足后自动推进到下一步），**执行层没有问题**。

问题出在**规划层**：LLM 每次只规划"当前一步"的 action_sequence，而非"完整的全流程"。以浮板过河为例，如果 LLM 一次性输出如下完整序列：

```json
[
  {"action": "wait", "condition": "objects[0].State == 'Idle'"},
  {"action": "move", "direction": "right", "condition": "displacement >= 1.5", "allowed_contact_obj_ids": [0]},
  {"action": "wait", "condition": "objects[0].State == 'Idle'"},
  {"action": "move", "direction": "right", "condition": "displacement >= 2", "allowed_contact_obj_ids": [0]},
  {"action": "move", "direction": "right", "condition": "canInteract == true && nearestInteractableIndex == 3"}
]
```

Unity 侧的 ConditionEvaluator 会每帧判定 condition，自动推进步骤，**完全不需要 LLM 二次介入**。

因此，问题的本质是：**LLM 缺乏经验，无法一次性规划出完整的多阶段动作序列**。`plan_action_sequence_cmd` 的 docstring 中虽然提供了少量场景示例，但这些是开发者硬编码的，不是 Agent 通过经验积累动态掌握的。

### 1.3 否定的方案

#### 方案A：定时发环境消息让 Agent 被动观察

浮板每个岸边只停留 3 秒，而 LLM 推理 + 网络往返需要 2-5 秒。即使消息赶上了，LLM 每次推理也只能发一步 action_sequence，多步之间仍然断裂。此方案无法解决根本问题。

#### 方案B：Python 侧引入 Behavior 状态机

在 LLM 和 action_sequence 之间插入一个代码驱动的行为层，由预定义的状态机自主推进多阶段行为。

问题：需要开发者针对每个场景硬编码行为模板，扩展性差，不符合 Agent 自主进化的设计哲学。

#### 方案C：小脑模型做技能检索

用一个更小参数的模型作为"小脑"，根据场景描述检索和规划 action_sequence。

问题：小模型上下文不足（看不到完整环境数据+对话历史）、结构化输出可靠性弱、增加延迟和成本、判断质量必然低于主 LLM。

### 1.4 确定的方案：Action Skill 经验学习系统

类比 Claude Skill 的设计理念——将过往经验总结为可复用的 skill 供今后调用。核心思路：

- **让 LLM 做它擅长的事**：理解意图、判断场景、做决策
- **让经验模板做 LLM 不擅长的事**：提供完整的多阶段运动规划
- **让 action_sequence 做它擅长的事**：精确执行具体的物理动作

LLM 的角色从"每次都从零规划"变为"识别场景 → 调用经验模板 → 参数化并微调"。一旦经验模板被启动，后续的全套运动由 action_sequence 执行引擎自主完成，无需 LLM 二次介入。

---

## 2. 系统设计

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────┐
│  LLM 决策层                                          │
│  职责：理解意图、识别场景、选择技能、参数化模板         │
│  输入：环境消息 + <动作技能索引>                       │
│  输出：完整的 action_sequence 或 save/refine skill    │
└──────────────────┬──────────────────────────────────┘
                   │
     ┌─────────────┼─────────────┐
     │             │             │
     ▼             ▼             ▼
┌─────────┐ ┌───────────┐ ┌──────────────┐
│ 技能存储 │ │ 技能使用   │ │ 技能精进     │
│ (Save)  │ │ (Load)    │ │ (Refine/Del) │
└─────────┘ └─────┬─────┘ └──────────────┘
                   │
                   ▼
┌─────────────────────────────────────────────────────┐
│  action_sequence 执行引擎（现有，无需改动）            │
│  职责：执行具体的物理动作序列                          │
│  每帧求值 condition，自动推进步骤                     │
└─────────────────────────────────────────────────────┘
```

### 2.2 技能召回机制：渐进式披露

采用 Claude Skill 的渐进式披露思路，**由主 LLM 自己做技能匹配决策**，不引入向量数据库或小模型。

**第一层（常驻 System Prompt）**：技能索引，轻量

```
<动作技能索引>
1. [坐浮板过河] 当需要乘坐移动平台越过深渊到达对岸时
2. [推箱子垫脚] 当需要用箱子垫高来到达高处时
3. [等信号灯过路] 当需要等信号灯变绿后通过时
</动作技能索引>

规则：当你认为自己需要执行某个已掌握的动作技能时，
先调用 load_action_skill 加载完整模板，
然后根据当前环境参数化模板，最后用
plan_action_sequence_cmd 下发执行。
```

**第二层（按需加载）**：调用 `load_action_skill` 获取完整模板

**为什么不让小模型做匹配**：

| 维度 | 主 LLM 渐进式披露 | 小脑模型检索 |
|------|-------------------|-------------|
| 上下文完整度 | 完整（环境+对话+历史） | 仅 scenario_description |
| 判断质量 | 高 | 低 |
| 额外延迟 | 0 | 1次模型调用 |
| 额外成本 | 0 | 累积成本 |
| 灵活性 | 加载模板后可因地制宜微调 | 输出即终局，难以调整 |
| 基础设施 | 无 | 需向量数据库或额外模型 |

渐进式披露的 prompt 开销：每条索引仅一行（名称+触发提示），10 个技能约 200-300 字。若技能数量增长，可通过场景过滤（只注入当前关卡相关的技能索引）控制。

### 2.3 技能存储设计

#### 2.3.1 数据模型

```python
class ActionSkill(BaseModel):
    name: str                    # 技能名称，如 "坐浮板过河"
    description: str             # 简短描述，用于技能索引展示
    trigger_hint: str            # 触发提示，如 "当需要乘坐移动平台越过深渊到达对岸时"
    action_sequence_template: List[dict]  # 含参数占位符的模板
    version: int                 # 精进版本号
    source: str                  # "default" | "learned" | "refined"
    created_at: str              # 创建时间
    last_used_at: Optional[str]  # 最后使用时间
    use_count: int               # 使用次数
    success_count: int           # 成功次数
```

#### 2.3.2 模板参数化

模板中的具体物体序号等参数使用占位符，使用时由 LLM 根据当前环境替换：

```
模板：
[
  {"action": "wait", "condition": "objects[{platform_idx}].State == 'Idle'"},
  {"action": "move", "direction": "{board_direction}", "condition": "displacement >= {board_distance}", "allowed_contact_obj_ids": [{platform_idx}]},
  ...
]

使用时 LLM 填入：
platform_idx=0, board_direction="right", board_distance=1.5, ...
```

参数占位符的命名和替换由 LLM 在 `load_action_skill` 返回模板后自行完成，无需代码层面的参数引擎。LLM 看到模板+当前环境数据，自然知道如何替换。

#### 2.3.3 per-agent 存储 + 默认技能继承

- **每个 Agent 有独立的技能库**：不同 Agent 的成长路径不同，掌握的技能也不同
- **创建时注入默认技能**：默认技能与关卡/场景配置绑定，开发者预设该场景下有哪些典型操作模式

```python
class AgentSkillStore:
    skills: Dict[str, ActionSkill] = {}

    @classmethod
    def create_with_defaults(cls, scene_name: str) -> 'AgentSkillStore':
        """创建时注入该场景的默认技能"""
        store = cls()
        for skill in DEFAULT_SKILLS.get(scene_name, []):
            store.skills[skill.name] = skill.copy()
        return store
```

### 2.4 工具定义

#### 2.4.1 save_action_skill

```python
@tool
async def save_action_skill(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str,
    description: str,
    trigger_hint: str,
    action_sequence: List[ActionStep]
) -> str:
    """将一次成功的动作序列经验总结为可复用的技能模板。

    使用时机：
    - 当你成功完成了一组多步骤动作，且认为这个模式在未来类似场景中可复用时。
    - 保存时请将具体的物体序号等参数改为描述性占位符（如用 {platform_idx} 代替具体序号）。

    Args:
        skill_name(str): 技能名称，简洁明了，如"坐浮板过河"
        description(str): 技能的简短描述，用于技能索引展示
        trigger_hint(str): 触发提示，描述在什么情况下应该使用此技能
        action_sequence(List[ActionStep]): 动作序列模板
    Return:
        str: 保存结果
    """
```

#### 2.4.2 load_action_skill

```python
@tool
async def load_action_skill(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str
) -> str:
    """加载一个已掌握的动作技能的完整模板。

    使用流程：
    1. 从<动作技能索引>中识别当前场景匹配的技能
    2. 调用本工具加载完整模板
    3. 根据当前环境数据，将模板中的占位参数替换为实际值
    4. 如需微调，直接修改个别步骤
    5. 通过 plan_action_sequence_cmd 下发执行

    Args:
        skill_name(str): 技能名称，从<动作技能索引>中选择
    Return:
        str: 技能完整模板
    """
```

#### 2.4.3 refine_action_skill

```python
@tool
async def refine_action_skill(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str,
    refined_action_sequence: List[ActionStep],
    reason: str
) -> str:
    """根据实践经验精进已有的动作序列技能。

    使用时机：
    - 使用技能模板后执行失败，你发现了需要调整的地方
    - 多次使用后发现了更优的 condition 写法或步骤顺序
    - 场景机制变化导致原模板不再适用

    Args:
        skill_name(str): 要精进的技能名称
        refined_action_sequence(List[ActionStep]): 精进后的动作序列模板
        reason(str): 精进原因，如"上浮板时的位移1.5不够，改成2更稳定"
    Return:
        str: 精进结果
    """
```

#### 2.4.4 delete_action_skill

```python
@tool
async def delete_action_skill(
    agent: Annotated[str, InjectedState("name")],
    skill_name: str,
    reason: str
) -> str:
    """删除不再需要的动作序列技能。

    使用时机：
    - 技能长期未使用且不再适用
    - 场景已过时，技能无法复用
    - 技能多次执行失败且无法精进修复

    Args:
        skill_name(str): 要删除的技能名称
        reason(str): 删除原因
    Return:
        str: 删除结果
    """
```

### 2.5 System Prompt 中的技能索引注入

在 Agent 的 System Prompt 中动态注入当前掌握的技能索引：

```
<动作技能索引>
1. [坐浮板过河] 当需要乘坐移动平台越过深渊到达对岸时
2. [推箱子垫脚] 当需要用箱子垫高来到达高处时
</动作技能索引>

规则：当你认为自己需要执行某个已掌握的动作技能时，
先调用 load_action_skill 加载完整模板，
然后根据当前环境参数化模板，最后用
plan_action_sequence_cmd 下发执行。
若无匹配的技能，则照常自主规划 action_sequence。
```

注入时机：每次 LLM 被激活时，在构建 system prompt 阶段从 AgentSkillStore 读取索引信息。

---

## 3. LLM 决策流程

```
1. 收到环境消息 / 玩家消息
2. 看到 <动作技能索引>，判断当前场景是否匹配某个 skill
   ├─ 匹配 → load_action_skill(skill_name) 加载模板
   │         根据当前环境数据参数化模板
   │         如需微调 → 手动调整个别步骤
   │         plan_action_sequence_cmd 下发完整序列
   │
   └─ 不匹配 → 照常自主规划 action_sequence
              如果成功且可复用 → save_action_skill

3. action_sequence 执行完成
   ├─ 成功 → 正常继续
   │         如果觉得这个经验值得保存 → save_action_skill
   │
   └─ 失败 → 反思原因
             如果是模板问题 → refine_action_skill
             如果模板无法修复 → delete_action_skill
```

---

## 4. 默认技能示例

### 4.1 坐浮板过河

```
名称：坐浮板过河
触发提示：当需要乘坐移动平台越过深渊到达对岸时
模板：
[
  {"action": "wait", "condition": "objects[{platform_idx}].State == 'Idle'"},
  {"action": "move", "direction": "{board_direction}", "condition": "displacement >= {board_distance}", "allowed_contact_obj_ids": [{platform_idx}]},
  {"action": "wait", "condition": "objects[{platform_idx}].State == 'Idle'"},
  {"action": "move", "direction": "{board_direction}", "condition": "displacement >= {disembark_distance}", "allowed_contact_obj_ids": [{platform_idx}]},
  {"action": "move", "direction": "{board_direction}", "condition": "canInteract == true && nearestInteractableIndex == {door_idx}"}
]
```

### 4.2 走到目标旁交互

```
名称：走到目标旁交互
触发提示：当需要走到某个可交互物体旁边并进行交互时
模板：
[
  {"action": "move", "direction": "{direction}", "condition": "canInteract == true && nearestInteractableIndex == {target_idx}", "allowed_contact_obj_ids": []},
  {"action": "interact"}
]
```

### 4.3 等条件满足后行动

```
名称：等条件满足后行动
触发提示：当需要等待某个物体状态变化后再执行动作时
模板：
[
  {"action": "wait", "condition": "{wait_condition}"},
  ...后续动作
]
```

---

## 5. 改动范围

| 模块 | 改动 | 说明 |
|------|------|------|
| `agent_framwork/tools/base_tools.py` | 新增 4 个工具函数 | save / load / refine / delete_action_skill |
| `agent_framwork/agents/agent_interuptible.py` | tools 列表注册 | 注册 4 个新工具 |
| `agent_framwork/skills/` | 新增模块 | ActionSkill 模型、AgentSkillStore、默认技能定义 |
| `agent_framwork/agents/agent_interuptible.py` | system_template 注入 | 在 system prompt 中动态注入 <动作技能索引> |
| Unity 侧 | **无需改动** | 技能系统产出的是标准 action_sequence，走现有执行引擎 |

---

## 6. 后续迭代方向

### 6.1 Condition 变量体系扩展

当前 condition 无法区分"浮板停在左岸还是右岸"（`objects[0].State == 'Idle'` 只表示停了）。可考虑：
- 增加 `distanceTo(i)` 函数
- 支持 `objects[i].Position.x` 成员访问
- 或增加 `objects[i].Direction` 属性表示移动方向

### 6.2 技能索引的场景过滤

当技能数量增长后，在 system prompt 注入时只注入与当前关卡/场景相关的技能索引，避免 prompt 膨胀。

### 6.3 技能使用统计与自动淘汰

基于 `use_count` 和 `success_count` 计算技能成功率，长期低成功率的技能可由系统自动提示 Agent 考虑精进或删除。
