# 需求池 / 候选问题清单（不依附具体版本）

> **状态**：候选 / 未分配版本  
> **最后更新**：2026-07-10  
> **文件名**：`backlog.md`（2026-06-28 由 `analysis.md` 改名，避免与版本目录内的 `analysis.md` 混淆）  
> **目录说明**：见同目录 `README.md`。原 `DevDocs/v0.21.X/` 于 2026-06-28 改名为 `DevDocs/需求池/`。

## 用途

收纳暂未立项、但已知需要后续处理的问题与想法。新版本启动时从中挑题，并把对应条目迁移到新版本目录的 `analysis.md` / `requirements/`。**不要把这里的条目当作已立项需求**。

## 索引

| # | 标题 | 类型 | 影响范围 | 优先级 | 状态 | 立项版本 |
|---|---|---|---|---|---|---|
| 1 | `WaitAction` 缺 `allowed_contact_obj_ids` | Bug / 协议+Python+Unity | 中（动作语义有缺口，已有 workaround） | P0 | 候选 | — |
| 2 | `List[int]` 字段在模板里的占位符表达边界 | 体验 / Python 工具 schema | 中（结构性表达力缺口） | P0 | 候选 | — |
| 3 | Monitor 推送过密带来的打断噪声 | 体验 / Unity+Python | 中（影响长时训练效率） | P1 | 候选 | — |
| 4 | `mem_to_save` 累积长度本身没有压缩 | 体验 / 记忆系统 | 大（Episode 越长越糟） | P1 | 候选 | — |
| 5 | 默认技能复用率评估 | 调研 / 评估 | 小 | P2 | 候选 | — |
| 6 | 网络中断/异常时各操作报错信息不统一 | 调研 / 错误处理 | 中（用户难定位故障） | P2 | 收集中 | — |
| 7 | Unity 工程内 `.cs` 源文件编码不一致（GBK/UTF-8） | 工程清理 / Unity | 中（Inspector 乱码，长期债） | P1 | 已立项 | v0.22.0 |
| 8 | Kuzu `INTERACTED_WITH` 边 `MERGE` 主键冲突 | Bug / 记忆系统 | 中（已有 3 次重试兜底，最坏可能丢 Episode） | P1 | 候选 | — |
| 9 | `observe` 工具反馈应附带「自己的状态」 | 体验 / Unity 工具 | 中（Hidden/Dead/Stunned/Follow 易遗忘自身约束） | P1 | 候选 | — |
| 10 | idle wakeup 无信息量心理活动应抑制写入 | 体验 / 记忆系统 | 中（任务完成后 idle 期反复刷重复 Episode） | P1 | 已完成 | v0.22.2 |

> 字段约定：
> - **类型**：Bug / 体验 / 工程清理 / 调研 / 评估 / 协议改动 等，标注主要落地面（Python / Unity / 协议 / 记忆系统 / 工具）。
> - **影响范围**：小 / 中 / 大，对体验或可维护性的破坏程度，括号内简述原因。
> - **优先级**：P0（影响验收 / 阻断后续训练）、P1（明显体验问题）、P2（长期改进 / 评估类）。
> - **状态**：候选 / 收集中 / 已立项（标 vX.Y）/ 已完成（迁出本文件）。立项后整段剪切到对应版本目录。

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

## 6. P2 — 网络中断/异常时各操作的报错信息不统一

### 现象

v0.21.7 联调时发现：断网状态下点 NewGame，控制台只输出：

```text
创建Agent: 小明: 是一个帮助机器人
[小明]Agent is created.
创建Agent失败: Connection error.
```

`Connection error.` 是 OpenAI Python SDK 在底层 TCP / DNS / HTTPS 出错时抛出的 `APIConnectionError` 的默认 message，被 `main.handle_agent_create_request` 一类入口的通用 `except Exception as e` 直接 `str(e)` 透传给 Unity。问题在于：

- **信息量太低**：用户看不出是「网络断了」「LLM 端点不可达」还是「embedder 拉模型失败」。
- **不一致**：不同入口（创建 Agent / 发用户消息 / 工具回调 / 记忆写入 / RAG 检索）对网络异常的捕获、日志、Unity 反馈格式各不相同；个别路径甚至会静默吞掉。
- **难定位**：日志没有标注「失败发生在哪一阶段」（`init_agent_summary` / LLM 调用 / Embedding 调用 / Reranker 调用），看到 `Connection error.` 需要逐文件翻才能复盘。

### 候选方向

后续版本立项时，先**汇总**而不是急着改。需要收集的失败入口及其当前报错文本：

| 入口 | 触发时机 | 当前报错文本（待补） |
|------|----------|----------------------|
| `AgentCreateRequest` → `init_agent_summary` | NewGame 创建 Agent，需要 LLM 抽取实体 | `Connection error.`（已确认 2026-06-28） |
| `UserSendMessageRequest` → LangGraph `chatbot` 节点 | 玩家发消息时 LLM 调用 | 待补 |
| `UserSendFeedbackRequest` → LangGraph | 工具反馈触发的下一轮推理 | 待补 |
| `save_memory` → Graphiti `add_episode` | 后台异步写图（LLM 抽取实体边） | 待补（背景任务，可能完全没有 Unity 可见的报错） |
| `search_fact_memory` / `search_episode_memory` | 每轮 RAG（Embedder + Reranker） | 待补 |
| Tool RPC `TOOL_WAITERS` | Unity 端 ToolResult 长时间不回 | 与网络无关；不在本条范围 |

收集完后再讨论：

1. 统一异常分类（建议三类：`NetworkError` / `RemoteAPIError` / `LocalError`）与 Unity 可读文案；
2. 每个入口加结构化日志：`[<阶段>] <Agent 名> <异常类型>: <原始 message>`；
3. 是否对**初始化期**（`init_agent_summary` / `search_memory` 等首轮）的 LLM 调用加重试 / 提示用户「检查网络」。

具体方案等数据汇总后再展开；本条目暂作占位。

---

## 7. P1 — Unity 工程内 .cs 源文件编码不一致（GBK / UTF-8 混杂）

> **2026-06-30 已立项至 `DevDocs/v0.22.0/`**：PRD 与方案已生成，内容详见 `DevDocs/v0.22.0/requirements/Unity工程cs文件编码统一与防回流.md`、`DevDocs/v0.22.0/PRD.md`、`DevDocs/v0.22.0/solution.md`。本条目保留作为历史背景；推进与状态变更在 v0.22.0 内进行。

### 现象

VS 2022 中文环境下打开仓库内多数含中文的 `.cs` 看是正常的，但 Unity Inspector 预览（以及 Roslyn/mcs 编译期的字符串字面量）出现替换字符 `��` / `�����豸��Ϣ` 等乱码。例如：

- `SceneObjManager.cs`、`IInteractable.cs`、`InteractionZone.cs`：VS2022 正常 / Unity 预览乱码
- `SceneObjBase.cs`：两边都正常（v0.21.7 中曾手动重写为 UTF-8）

### 根因

抽样 `file` 检测：

| 文件 | 实际编码 | VS2022 | Unity 预览 |
|---|---|---|---|
| `SceneObjBase.cs` | UTF-8 (无 BOM) | OK | OK |
| `SceneObjManager.cs` / `IInteractable.cs` / `InteractionZone.cs` | **GBK (无 BOM)** | OK（回退 CP936） | 乱码（强制 UTF-8） |

仓库内多数老 `.cs` 是早期 VS 在中文 Windows 默认以 ANSI/GBK 保存的，且**无 BOM**：

- **VS2022**：先尝试 UTF-8，若解码失败回退到系统 ANSI（CP936/GBK），所以 GBK 与 UTF-8 都能正常显示。
- **Unity Inspector / Roslyn 编译**：固定按 UTF-8 解析，遇到非法 UTF-8 字节序列直接渲染为 U+FFFD。

`.editorconfig` 已经声明 `charset = utf-8`，但只对**新建文件**生效，不会自动转换历史文件。

### 受影响清单（2026-06-28 扫描，`Src/IndependentAgentProject/Assets/Scripts/**/*.cs`）

`IndependentAgentProject` 部分（与 Agent 链路相关，必须修）：

```
GameFlow/Core/FlowExecutor.cs
SaveManager/SaveManager.cs
Services/AgentServiceAsyncExtensions.cs
ViewController/Bootstrap/BootstrapEntry.cs
ViewController/Gameplay/Action/ActionSequence/ConditionEvaluator/ConditionContext.cs
ViewController/Gameplay/Action/ActionSequence/ConditionEvaluator/ExprViewFactory.cs
ViewController/Gameplay/Action/ActionSequence/Model/ActionSequenceRuntime.cs
ViewController/Gameplay/Action/ObserveRuntime/ObserveRuntime.cs
ViewController/Gameplay/CameraController.cs
ViewController/Gameplay/GameFlow/Flows/NextMapFlow.cs
ViewController/Gameplay/GameFlow/Steps/BackupMemoryStep.cs
ViewController/Gameplay/GameFlow/Steps/BroadcastMessageToAgentsStep.cs
ViewController/Gameplay/GameFlow/Steps/CreateAgentStep.cs
ViewController/Gameplay/GameFlow/Steps/DeleteMemoryStep.cs
ViewController/Gameplay/GameFlow/Steps/LoadAgentStep.cs
ViewController/Gameplay/GameFlow/Steps/LoadSceneStep.cs
ViewController/Gameplay/GameFlow/Steps/RestoreMemoryStep.cs
ViewController/Gameplay/GameFlow/Steps/SaveDataStep.cs
ViewController/Gameplay/GameFlow/Steps/StartAgentStep.cs
ViewController/Gameplay/SceneObj/Base/IInteractable.cs
ViewController/Gameplay/SceneObj/Base/InteractionZone.cs
ViewController/Gameplay/SceneObj/Base/SceneObjInfo/SceneObjInfoMapper.cs
ViewController/Gameplay/SceneObj/Base/SceneObjInfo/SceneObjInfoModel.cs
ViewController/Gameplay/SceneObj/Base/SceneObjManager.cs
ViewController/Gameplay/SceneObj/Chara/Merchant.cs
ViewController/Gameplay/SceneObj/Device/Abyss.cs
ViewController/Gameplay/SceneObj/Device/Box.cs
ViewController/Gameplay/SceneObj/Device/ClickableSignalLight.cs
ViewController/Gameplay/SceneObj/Device/Core/DeviceBase.cs
ViewController/Gameplay/SceneObj/Device/Core/IClickable.cs
ViewController/Gameplay/SceneObj/Device/Core/ITriggerable.cs
ViewController/Gameplay/SceneObj/Device/Lever.cs
ViewController/Gameplay/SceneObj/Device/Mailbox.cs
ViewController/Gameplay/SceneObj/Device/MouseClickInteractor2D.cs
ViewController/Gameplay/SceneObj/Device/MovingPlatformAuto.cs
ViewController/Gameplay/SceneObj/Device/MovingPlatformTrigger.cs
ViewController/Gameplay/SceneObj/Device/NextMapDoor.cs
ViewController/Gameplay/SceneObj/Device/Safebox.cs
ViewController/Gameplay/SceneObj/Device/SignalLight.cs
ViewController/Gameplay/SceneObj/Device/Telephone.cs
ViewController/Gameplay/SceneObj/Device/Wall.cs
ViewController/UI/UIAgentStart.cs
ViewController/UI/UIMenu.cs
ViewController/UI/UITitle.cs
```

`ShootingEditor2D/` 旧场景部分（参见 `AGENTS.md` §1.6，"遗留无关"，可选转或一次性清理）：

```
ShootingEditor2D/Model/IGunConfigModel.cs
ShootingEditor2D/System/IStatSystem.cs
ShootingEditor2D/System/TimeSystem/ITimeSystem.cs
ShootingEditor2D/ViewController/Gameplay/Bullet.cs
ShootingEditor2D/ViewController/Gameplay/CameraController.cs
ShootingEditor2D/ViewController/Gameplay/Enemy.cs
ShootingEditor2D/ViewController/Gameplay/Gun.cs
ShootingEditor2D/ViewController/Gameplay/Player.cs
ShootingEditor2D/ViewController/Gameplay/Trigger2DCheck.cs
ShootingEditor2D/ViewController/LevelEditor/LevelEditor.cs
ShootingEditor2D/ViewController/LevelEditor/LevelPlayer.cs
ShootingEditor2D/ViewController/UI/UIController.cs
ShootingEditor2D/ViewController/UI/UIGameOver.cs
ShootingEditor2D/ViewController/UI/UIGameStart.cs
```

### 候选方案

| 方案 | 说明 |
|---|---|
| A | 用 `iconv -f GBK -t UTF-8` 整批转换（保留 CRLF / 无 BOM），单独 git commit 便于审 diff。**推荐** |
| B | 逐文件用 VS2022「文件→高级保存选项」改为 UTF-8 无 BOM 重存。直观但容易漏 |
| C | 仅修受 Unity 预览影响最明显的文件（`SceneObjManager.cs` / `IInteractable.cs` / `InteractionZone.cs` 等），其余先不动 |

潜在风险：

- 行结束符变化（脚本里要显式保持 CRLF，避免误触 LF）。
- `git blame` 会失真——可同时提交 `.git-blame-ignore-revs`。
- 转换后必须立刻在 Unity 里完整跑一遍，确认中文 Inspector 字段、运行时日志、UI 文本都正常。

### 影响范围预估

- Unity：受影响的 `.cs` 文件本身；不会影响 .meta、Prefab 序列化。
- Python 侧无影响。
- 测试：开关 Unity 主菜单 → NewGame → 走完第一关 → 切换语言/中文 UI 显示无替换字符。

### 复现验证

```bash
file Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Base/SceneObjManager.cs
# 期望输出：UTF-8 text, with CRLF line terminators
```

---

## 8. P1 — Kuzu `INTERACTED_WITH` 边 `MERGE` 主键冲突（重复事实抽取）

### 现象

v0.21.7_fix_1 联调（2026-06-28_14-32-42 训练）期间，`memory_manager._memory_worker` 在写后台 Episode 时报：

```text
[MemoryManager._save_memory] ❌ 写记忆失败（最终）: Runtime exception: Found duplicated primary key value <uuid>, which violates the uniqueness constraint of the primary key column.
```

错误来源是 Graphiti 把抽到的事实边（关系类型 `INTERACTED_WITH`）`MERGE` 到 Kuzu 时撞主键。出错事实文本与「你从柜子里出来了。状态从 Hidden 回到 Idle。」高度相似，多个 Episode 被先后处理时抽到同一条事实，复用了同一个 uuid，导致 `MERGE` 行为退化为「插入但主键已存在」直接抛 `RuntimeException`。

`MemoryManager._save_memory` 自带最多 3 次重试（见 `memory_system/memory_manager.py`），所以这次没影响后续写入；但日志里会刷一长串 `Runtime exception`，并且**最后一次重试如果仍冲突，对应 Episode 会被静默丢弃**（catch 到 Exception 只打 ❌，不再回滚或入死信队列）。

### 根因初判

1. Graphiti 抽事实边的 uuid 不是 deterministic-by-content 而是「会复用已存在边的 uuid」——多 Episode 中同一对实体 + 同一谓词的事实被合并到一条边。
2. Kuzu 的 `REL TABLE` 主键 = 边 uuid；`MERGE (a)-[e:INTERACTED_WITH {uuid}]->(b)` 在并发或自指（同一 episode 内 a==b？待确认）场景下不会幂等地命中现有行，而是尝试创建新行。
3. `_graph_write_lock` 已序列化 `add_episode`，但 Graphiti 内部一个 Episode 会产生多条 Cypher，自身在事务边界内可能就违反主键。

### 待调查

- [ ] 拿到出错 uuid，反查 `Episodic` / 关系节点，确认它是「跨 Episode 复用」还是「同 Episode 内重复」。
- [ ] 复现：连续 `add_episode` 同样文本 5 次，看是否稳定触发。
- [ ] 检查 Graphiti 版本是否已修复（搜 issue：`duplicated primary key` / `INTERACTED_WITH`）。
- [ ] 评估是否给 `_save_memory` 加「主键冲突 = 视为已写入，吞掉」分支，避免噪声。

### 候选方案

| 方案 | 说明 |
|---|---|
| A | 升级 Graphiti / Kuzu 到含修复的版本（先调研）。零业务改动 |
| B | `_save_memory` 捕获 `duplicated primary key` 错误，记 INFO 日志后视为成功；不重试不丢 Episode |
| C | 在 Cypher 层把 `MERGE` 改为 `ON CREATE SET ... ON MATCH SET ...`（需要 Graphiti 暴露 hook，改动大） |

短期 B 即可消噪声；A 是根治；C 改动太深，暂不考虑。

### 复现日志

- `Src/PythonServer/logs/prompts/小明/2026-06-28_14-32-42.log` 训练终端输出。
- 终端：`terminals/4.txt` 报错段。
- **2026-06-29 补充复现**：`logs/prompts/小明/2026-06-29_19-50-46.log`（v0.21.7_fix_3 完成连续 5 次穿越激光网后的 idle 等待期）。`terminals/4.txt:935-1024` 报错段，参数细节：
  - `uuid = 31540bf9-8397-4a0e-a0be-c70da9bffd09`
  - `name = "MONITORED"`、`fact = "一切如常，继续待命。"`
  - `source_node_uuid == target_node_uuid == 5aa935d8...`（self-loop）
  - `episodes` 累计 5 个 episode uuid，且 3 次重试均失败
  - 上游成因：任务完成后 AI 在 idle wakeup 上反复生成几乎完全相同的心理活动「一切如常，继续待命」，Graphiti 事实去重把它们判为同一条边并复用 uuid，触发 `MERGE` 时 Kuzu 当作 `CREATE` 处理 → 撞主键。该复现进一步说明：**仅做下游 retry 不够，还需要上游抑制无意义心理活动写入（见条目 10）**。

---

## 9. P1 — `observe` 工具反馈应附带「自己的状态」

### 现象

v0.21.7_fix_1 联调时观察到：小明在 Hidden 期间被推送了多条 monitor / observe 反馈，文本只描述外部环境（柜子、地板、平台、敌人位置 / 状态），**不包含 Agent 自身的状态**（`state=Hidden / Idle / Move / Dead / Stunned`、`TargetFollowing`、可移动性等）。

后果：

- Agent 在 Hidden 状态下读到 observe 反馈后，常常忘记自己「正躲在柜子里」，下一步规划 `move_cmd` 被 `IsImmovable` 守卫驳回（参见 fix_1 测试 1）。本来可以通过 prompt 上下文记住，但被高频反馈淹没后会丢。
- 同理 Dead / Stunned / 被定身 / Follow 中的 Agent，所有外界反馈都没有「我现在能做什么」的提示。

### 候选方向

把 Agent 自身状态作为 observe / monitor 反馈的标准头部统一拼进去。建议字段：

| 字段 | 含义 | 来源 |
|---|---|---|
| `state` | 当前 FSMState 名 | `CharaBase.CurState.Name` |
| `is_immovable` | 是否处于不可移动状态 | `SceneObjBase.IsImmovable` |
| `is_invulnerable` | 是否无敌 | `SceneObjBase.IsInvulnerable` |
| `is_undetectable` | 是否被敌人忽略 | `SceneObjBase.IsUndetectable` |
| `following` | 当前 Follow 目标（若有） | `CharaBase.TargetFollowing` |
| `position` | 当前坐标 | `transform.position`，已在部分反馈中有 |

预期渲染（与现有 `<你的状态>` 块一致，但保证 observe 反馈也带）：

```text
<你的状态>
状态: Hidden（无法移动 / 无敌 / 不可被察觉）
位置: (x, y)
</你的状态>
```

### 候选方案

| 方案 | 说明 |
|---|---|
| A | 在 `AIPlayer.CreateMessageText` 拼接 observe / monitor 反馈时统一插入 `<你的状态>` 块。改动集中，影响面小 |
| B | 在工具结果 proto 中加 `agent_self_state` 字段，由 Python 侧渲染到 prompt 头部。跨语言改动大 |

A 起步即可，后续如果发现 Agent 仍然遗忘，再升级到 B。

### 影响范围预估

- Unity：`AIPlayer.CreateMessageText`（已存在 `<你的状态>` 拼接逻辑，扩展到 observe / monitor 路径即可）。
- Python：无改动。
- 测试：Hidden / Dead / Stunned / Follow 四种状态下触发一次 observe 反馈，确认头部都有「无法移动 / 无敌 / 不可被察觉 / 跟随中」标签。

---

## 10. P1 — idle wakeup 无信息量心理活动应抑制写入长期记忆

> **2026-07-10 已立项至 `DevDocs/v0.22.2/`**，验收通过，已实现。详见 `DevDocs/v0.22.2/PRD.md`、`DevDocs/v0.22.2/solution.md`。

### 现象

v0.21.7_fix_3 联调（2026-06-29_19-50-46）的训练后期，小明在完成「连续 5 次穿越激光网」任务后进入 idle 等待。系统每隔几十秒推送一次 `idle wakeup`：

```text
[2016年03月04日 21:23]你已经空闲了一段时间，可以稍微留意一下周围。
[世界事件摘要]
最近事件数: 3
1. 0.3秒前，2. 自动开关的激光网: Inactive -> Active
...
```

AI 每次产出几乎完全相同的心理活动：

- 「一切如常，继续在门旁待命。任务已完成，随时可进入第二关。」
- 「一切如常，激光网规律不变，继续在门旁待命。」
- 「一切如常，继续在门旁待命。已等待多月，激光网规律依旧稳定，随时可以行动。」

每条都会触发一次 `save_memory` → `add_episode`。**详见 `logs/prompts/小明/2026-06-29_19-50-46.log` line 11605~12482，连续 30+ 条几乎同义的 idle 响应**。

直接后果：

1. **触发条目 8 的主键冲突连锁**：Graphiti 事实去重把这些同义句判为同一条事实边，3 次 retry 都用同一 uuid，全部失败 → Episode 被丢弃；
2. **Worker 日志被刷得很满**：每条 idle 响应都会跑一次 LLM 抽取（费 token）、3 次 retry（费时间），最终还失败；
3. **记忆图谱噪声**：即使 retry 成功，长期下来「一切如常」类无信息量 Episode 会大量堆积，挤压有用 Episode 的语义权重。

### 根因

- `agent_interuptible.py` 的 `save_memory` 节点目前**无条件**入队（只要本轮跑到了 END）。
- idle wakeup 的语义是「让 Agent 留意一下周围」，并非要求长期记忆这件事；但当前是把它当作普通用户消息处理，因此心理活动也被入队。
- 缺乏「内容与上一条几乎相同」的去重判断；缺乏「本轮无信息量、跳过记忆」的旁路。

### 候选方案

| 方案 | 说明 |
|------|------|
| A | **prompt 侧**：在 idle wakeup 提示语里加一条「若与上一次状态完全一致，则只用极简词回应，不再展开心理活动」。改动最小，但只能减少而不能根治 |
| B | **节点侧**：`save_memory` 节点检查本轮 `mem_to_save` 是否与上一条已落库 Episode 相似度过高（hash / 长度差 / embedding 相似），过高则跳过入队。需要存最近一条文本摘要 |
| C | **入口侧**：`Agent.asend_message` 中识别 idle wakeup（前缀「你已经空闲了一段时间」）→ 标记本轮 `skip_memory=True`，`save_memory` 节点读到该标记直接 return | 
| D | **组合**：A + C。idle wakeup 默认 skip_memory；但若 AI 决定主动调工具（说明 idle 触发了真正的行动），则不再 skip |

推荐 **D**：D = A（prompt 引导简短）+ C（入口标记 skip）。idle 期反复刷的纯心理活动不再入库，但如果 idle wakeup 触发了真正动作（observe / 移动 / communicate 等），则保留写入。

### 验收建议

- 训练任务完成后让 Agent 在 idle 状态待至少 20 个 idle wakeup 周期。
- 验证 worker 日志无 `duplicate edge retry` 噪声；
- 验证 `mem_episode` 检索时不会出现大量「一切如常」类 Episode；
- 验证若 Agent 在 idle 期主动调工具（如 `observe_cmd`），该轮记忆仍正常写入。

### 影响范围预估

- Python：`agent_framwork/agents/agent_interuptible.py`（`save_memory` 节点 + State 中加 `skip_memory` 字段）、`main.py` 或 `Agent.asend_message`（识别 idle wakeup 前缀，注入标记）。
- 与条目 8 强相关：本条若先落地，条目 8 的触发概率会大幅下降；条目 8 仍需独立处理「正常推理路径下的事实去重 retry 兼容性」。
- 测试：不依赖 Unity 联调，可用 `pytest` 直接驱动 `Agent.aprocess_message` mock idle 输入。

### 复现日志

- `logs/prompts/小明/2026-06-29_19-50-46.log`（line 11605~12482，30+ 条 idle 响应）
- `terminals/4.txt:935-1024`（v0.21.7_fix_3 联调时 worker 报错段）

---

- 以上 10 条均未立项；新版本启动时从中挑题，并把对应条目从本文件迁移到新版本目录的 `analysis.md`。立项后请同步更新顶部索引表的「状态 / 立项版本」字段。
- 复现日志：
  - 条目 1~5：`Src/PythonServer/logs/prompts/小明/2026-06-23_13-41-56.log`（v0.21.6 验收训练）
  - 条目 6：v0.21.7 联调期间断网 NewGame 控制台输出（2026-06-28）。
  - 条目 7：2026-06-28 `file` 工具扫描 `Src/IndependentAgentProject/Assets/Scripts/**/*.cs`。
  - 条目 8~9：`Src/PythonServer/logs/prompts/小明/2026-06-28_14-32-42.log`（v0.21.7_fix_1 联调）；条目 8 另有 2026-06-29 复现 `logs/prompts/小明/2026-06-29_19-50-46.log`。
  - 条目 10：`Src/PythonServer/logs/prompts/小明/2026-06-29_19-50-46.log`（v0.21.7_fix_3 联调）。
