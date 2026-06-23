# v0.21.X 候选问题清单（暂未排版本）

> **状态**：候选 / 未分配版本  
> **最后更新**：2026-06-23  
> **来源**：`v0.21.6` 验收（基于 `Src/PythonServer/logs/prompts/小明/2026-06-23_13-41-56.log` 的训练日志分析）

---

## 用途

`v0.21.6` 已验收通过。本次训练日志里暴露出**与 v0.21.6 改动无关、但需要后续版本继续处理**的若干问题。`v0.21.7` 已有其它计划，所以先把这份候选清单单独留在 `v0.21.X/`，等真正起新版本时再从里面挑题立项。

不要把这里的条目当作已立项需求；这是一份**冷备问题池**。

---

## 1. P0 — `WaitAction` 缺 `allowed_contact_obj_ids`

### 现象

训练日志（2026-06-23_13-41-56）中，小明掌握了「乘平台渡陷阱」的总体思路（等平台到近端 → 走上平台 → `wait actionTime >= 5` → 走下平台）后，仍然在 2 月～3 月反复触发 `[返回检查点] 你触碰到: 2. 陷阱` 多次失败（line 2920、5244、5415、5584、6105）。Agent 自己分析的根因：

> "`wait` 动作在平台上时，平台移动穿过陷阱区域，可能触发了陷阱碰撞"（line 5343）

### 根因

`WaitAction` 的 schema 没有 `allowed_contact_obj_ids` 字段，只有 `MoveAction` 有。导致 Agent 无法表达「我站着等的这 5 秒里，允许跟陷阱（2）和平台（3）发生接触」。Agent 也尝试过把 `wait` 换成 `move` 配合 `allowed_contact_obj_ids: [2, 3]`，但 `move` + `actionTime` 的组合在实际位移很小的「站着等」语义下不可靠。

### 候选方案

| 方案 | 说明 |
|---|---|
| A | 给 `WaitAction` 单独加 `allowed_contact_obj_ids: List[int] = []`，与 `MoveAction` 字段同名同义 |
| B | 把 `allowed_contact_obj_ids` 提升到 `ActionStep` 公共基类（所有动作都允许指定 allow-list） |

短期 A 影响面最小；B 长期更一致。

### 影响范围预估

- Python：`agent_framwork/tools/action_sequence_model/model/action.py`（`WaitAction` 或 `StateChangeAction` 增加字段）。
- Protobuf：`Tools/message.proto` 中 `ActionStep` 的 `wait` 子消息加字段。
- Unity：`ActionSequenceRuntime` 的 wait 动作执行时读取 allow-list。
- 默认技能 YAML：`借助移动平台渡越陷阱` 模板可以增加该字段示例。

---

## 2. P0 — `List[int]` 字段在模板里的占位符表达边界

### 现象

`v0.21.6` 让模板可以内联 `{snake_case}` 占位符，但占位符**只能是字符串**（因为要写在 JSON 字符串字面量里）。问题是 `allowed_contact_obj_ids` 是 `List[int]`，Agent 在 2026-06-23 日志中两次写出 `"allowed_contact_obj_ids": [{platform_index}]`（line 6760、6781），都被「`action_sequence_template` 不是合法 JSON」拦下。Agent 最后选择**把字段留空** + 在 `usage_notes` / `adjustment_hint` 里写「需手动填入平台序号」。

后果：

- 已沉淀的核心模板 `从左到右渡陷阱` / `从右到左渡陷阱` 里 `allowed_contact_obj_ids: []`，复用时如果 Agent 没读 `adjustment_hint` 就会漏填，直接踩平台 / 陷阱碰撞。
- 这是模板表达力的**结构性缺口**：能参数化字符串，无法参数化整数列表。

### 候选方案

| 方案 | 说明 |
|---|---|
| A | 仅在 `skill_tools` 工具描述中明确："`List[int]` 字段不能放字符串占位符；如需参数化请在 `adjustment_hint` / `usage_notes` 中说明手动填法"。零代码风险 |
| B | 放宽 `_parse_action_sequence_template`：对部分整数列表字段，允许字符串形式的占位符；执行入口 (`plan_action_sequence_cmd`) 由 Agent 显式替换为 int 列表，并由占位符扫描兜底 |
| C | 模板态 schema 改为 `List[Union[int, str]]`，执行入口强制 int |

A 最稳。B 更友好但需要为每个 `List[int]` 字段单独白名单。C 改动最大，但语义最干净。

---

## 3. P1 — Monitor 推送过密带来的打断噪声

### 现象

训练末尾自我状态（line 14137–14142）：

```text
持续观察目标[1]
对象: 3. 自动移动的平台
观察时长: 7680.0 秒
状态变化次数: 2617 次
未读记录: 2610 条
存储记录: 20 条
```

即「自动移动的平台」在持续观察期间每 2.9 秒 + 3 秒一次状态翻转，累计推送了 2617 次状态变化反馈。每次反馈都是 `is_feedback=True`，**总是打断 Agent**（参见 `AGENTS.md` §2.5）。

可观察到的影响：

- Agent 思考被频繁打断，`mem_to_save` 在被打断/被恢复之间反复拼接，间接放大上下文长度。
- Agent 全程仅在最开头调用了一次 `get_monitor_records_cmd`（line 524），其余时间被推送淹没。

### 候选方向

- 让 Agent 自己决定持续观察的「推送策略」：例如新增一个工具参数 `notify_on_change: bool` 或 `notify_interval_sec: float`，默认不推送、只累积记录，Agent 主动 `get_monitor_records` 时再读。
- 或者保留推送，但 Unity 侧合并高频 Idle↔Move 切换为「最近 X 秒内 N 次切换」摘要。
- 或者把这种"周期性"目标识别出来，仅在「周期被打破」时推送。

需要先讨论这三个方向的取舍。

---

## 4. P1 — `mem_to_save` 累积长度本身没有压缩

### 现象

`v0.21.5` 已经把 `mem_to_save` 在打断时拼接的策略改成压缩 / 情景日记，但日志显示 `<回想>` 的情景片段仍可见多轮"我心想 / 我使用了 …"原文拼接（line 17–52），单条 Episode 数千字。

`v0.21.5` 解决的是「上下文裁剪上限」与「打断后继续累积」的情景断裂问题，**没有压缩 `mem_to_save` 自身的累计长度**。长程训练下 Episode 仍会越来越长，最终写图谱时会触发 Graphiti 的 8000 字符截断（`memory_manager._save_memory` 中已硬截断）。

### 候选方向

- 给 `mem_to_save` 加 rolling 压缩：每超过 N 段心理活动 / 工具调用就压缩成"流水账日记"段落，保留时间戳与关键动作摘要。
- 提供一个 Agent 可见的工具 `summarize_recent_thoughts`，让 Agent 自己决定何时把当前 `mem_to_save` 压一下。

具体方案等真正排上版本时再展开。

---

## 5. P2 — 默认技能复用率评估

### 现象

我们提供的默认技能 `借助移动平台渡越陷阱.单向渡越（标准）` 使用「`state == Idle` + `LeftPosition.x < 阈值` + `state == Move` + `state == Idle`」四段式判定。但 Agent 在本次训练中**没有命中**这个默认模板，而是自主创建了基于 `wait actionTime >= 5/7` 的新模板 `乘平台渡陷阱`。

### 候选方向

- 评估默认技能在 RAG 检索中的命中率（可以加一个统计 hook）。
- 评估默认模板的写法（位置阈值 vs. 时间阈值）哪一种对 Agent 更友好。
- 极端方案：删掉所有默认技能，让 Agent 完全从零摸索（v0.21.4 时已部分朝这个方向走过）。

非紧迫，可放在 v0.22 之后讨论。

---

## 备注

- 以上 5 条均未立项；新版本启动时从中挑题，并把对应条目从本文件迁移到新版本目录的 `analysis.md`。
- 所有问题的复现日志：`Src/PythonServer/logs/prompts/小明/2026-06-23_13-41-56.log`（v0.21.6 验收训练）。
