# PRD - v0.22.4 WaitAction 接触白名单 + List[int] 模板占位符

> **状态**：已确认
> **对应需求**：`requirements/wait_action_contact_ids_and_list_placeholder.md`
> **来源**：`DevDocs/需求池/backlog.md` 条目 1（P0）、条目 2（P0）
> **最后更新**：2026-07-10

---

## 1. 背景与目标

### 1.1 问题一：WaitAction 无法表达「等待期间允许接触哪些物体」

`MoveAction` 有 `allowed_contact_obj_ids` 字段，Agent 可以声明移动过程中允许接触哪些物体（如平台、箱子）。但 `WaitAction` 没有这个字段。

训练日志（2026-06-23_13-41-56）显示：小明掌握了「乘平台渡陷阱」策略（等平台到近端 -> 走上平台 -> wait actionTime >= 5 -> 走下平台），但因 wait 期间平台移动穿过陷阱区域，Agent 与陷阱的接触被 Unity `ExecuteWaitAction` 的 `ErrorConditionFunc` 判定为碰撞，动作序列中断，反复失败（line 2920、5244、5415、5584、6105）。

根因在 Unity `AIPlayer.ExecuteWaitAction`：其 `ErrorConditionFunc` 只检查「新接触物体是否在 `StartTouchingObjs` 中」，不像 `ExecuteMoveAction` 那样还检查 `AllowedContactObjs`。即使 proto 加了字段，Unity 侧也必须同步读取并应用白名单。

### 1.2 问题二：List[int] 字段无法在技能模板中参数化

v0.21.6 让技能模板可以内联 `{snake_case}` 占位符，但占位符只能出现在 JSON 字符串值中。`allowed_contact_obj_ids` 是 `List[int]`，Agent 写出 `"allowed_contact_obj_ids": [{platform_index}]` 会被 JSON 解析拒绝（`{platform_index}` 不是合法 JSON 值），只能留空并在 `usage_notes` 里写手动填法。复用时若 Agent 没读 `usage_notes` 就会漏填，直接碰撞。

### 1.3 目标

1. 给 `WaitAction` 补齐 `allowed_contact_obj_ids`，与 `MoveAction` 同名同义，跨 Python / Proto / Unity 三层落地。
2. 让 `List[int]` 字段也能在模板里用占位符参数化，使 Agent 沉淀的模板可以直接表达「这里的物体序号执行时再填」。

## 2. 范围

### 2.1 本期包含

- **WaitAction 字段补齐**（条目 1）：
  - Python `action.py`：`WaitAction` 增加 `allowed_contact_obj_ids: List[int]` 字段。
  - Protobuf `message.proto`：`WaitAction` 消息增加 `repeated int32 allowed_contact_obj_ids` 字段。
  - Python `base_tools.py`：`build_pb_action_step` 的 wait 分支填充新字段。
  - Unity `AIPlayer.ExecuteWaitAction`：读取 `curAction.Wait.AllowedContactObjIds` 并填入 `AllowedContactObjs`，`ErrorConditionFunc` 增加 `AllowedContactObjs` 白名单判断（与 `ExecuteMoveAction` 对齐）。
  - `plan_action_sequence_cmd` docstring 示例更新。

- **List[int] 模板占位符**（条目 2）：
  - `skill_tools.py` 的模板解析与占位符扫描逻辑，支持 `List[int]` 字段中字符串形式的占位符。
  - `plan_action_sequence_cmd` 执行入口（或 `build_pb_action_step` 前）将字符串占位符替换为 int 列表，并强校验。

### 2.2 本期不包含

- 不把 `allowed_contact_obj_ids` 提升到 `ActionStep` 公共基类（backlog 方案 B），本期采用方案 A（仅 WaitAction 补字段），影响面最小。
- 不改 `InteractAction` / `SelectAction` / `InputAction`，它们当前无接触白名单需求。
- 不改默认技能 YAML 文件内容（Agent 可在后续训练中自行精进模板）。
- 不改 Unity `ActionSequenceRuntime` 模型层（`AllowedContactObjs` 已存在于 `ActionRuntime`，move/wait 共用）。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| Agent（小明） | 乘移动平台渡陷阱：走上平台后需 wait 几秒，期间平台载着 Agent 穿过陷阱区 | wait 期间与平台/陷阱的接触不被判定为碰撞，动作序列正常继续 |
| Agent（小明） | 把「乘平台渡陷阱」沉淀成可复用技能模板 | 模板里 `allowed_contact_obj_ids` 能用 `{platform_index}` 等占位符表达，执行时替换为真实序号 |
| 开发者 | 回看训练日志排查碰撞失败 | wait 动作的碰撞日志能区分「白名单内接触」与「真正碰撞」 |

## 4. 功能需求

### 4.1 WaitAction 补齐 allowed_contact_obj_ids

- `WaitAction` 新增 `allowed_contact_obj_ids: List[int]`，语义与 `MoveAction` 完全一致：「等待期间允许接触的物体序号列表。当接触到列表以外的物体时，会中断动作序列。若无则填空列表 []。」
- 字段默认空列表（与 MoveAction 当前 `Field(...)` 必填不同，wait 的字段应为可选默认空，因为绝大多数 wait 场景不需要白名单）。
- Unity `ExecuteWaitAction` 的 `ErrorConditionFunc` 必须与 `ExecuteMoveAction` 对齐：新接触物体若在 `AllowedContactObjs` 中则不报错。

### 4.2 List[int] 模板占位符

- 模板保存时（`_parse_action_sequence_template`），允许 `List[int]` 字段的元素以字符串占位符形式出现，如 `"allowed_contact_obj_ids": ["{platform_index}"]`。
- 占位符扫描（`_scan_placeholders`）需能遍历到列表中的字符串元素并收集占位符名。
- 执行入口（`plan_action_sequence_cmd`）在强校验阶段：
  - 将 `List[int]` 字段中的字符串占位符替换为真实 int 值（Agent 传入的 `action_sequence` 已经是结构化参数，此时不应再有占位符）。
  - 若仍残留 `{...}` 占位符，按现有 `_find_unresolved_placeholders` 逻辑拒绝执行。

## 5. 非功能需求

- **向后兼容**：`WaitAction` 新字段为可选，旧模板（wait 无该字段）不受影响。
- **协议兼容**：proto 新增 `repeated` 字段，旧客户端不填时为空列表，向后兼容。
- **一致性**：wait 与 move 的接触白名单行为必须一致，不允许出现「move 放行但 wait 中断」的差异。

## 6. 验收标准

- [ ] Python：`WaitAction` 模型含 `allowed_contact_obj_ids: List[int]` 字段，默认空列表。
- [ ] Proto：`WaitAction` 消息含 `repeated int32 allowed_contact_obj_ids` 字段。
- [ ] Python：`build_pb_action_step` wait 分支正确填充 `allowed_contact_obj_ids`。
- [ ] Unity：`ExecuteWaitAction` 读取并填充 `AllowedContactObjs`，`ErrorConditionFunc` 检查白名单。
- [ ] Unity：wait 期间接触白名单内物体不中断；接触白名单外物体正常中断。
- [ ] Python：模板保存时 `"allowed_contact_obj_ids": ["{platform_index}"]` 可通过校验。
- [ ] Python：`plan_action_sequence_cmd` 执行时残留占位符仍被拒绝。
- [ ] `plan_action_sequence_cmd` docstring 的 wait 示例包含 `allowed_contact_obj_ids`。
- [ ] Python 侧自测通过（模型字段、build_pb、模板解析、占位符扫描）。

## 7. 待确认问题

> 以下默认方案待用户确认。

- [ ] `WaitAction.allowed_contact_obj_ids` 是默认空列表（可选）还是像 `MoveAction` 一样必填？-> **建议默认空列表**，因为绝大多数 wait 场景不需要白名单，必填会增加 Agent 无意义填空负担。
- [ ] List[int] 占位符方案选 A（仅文档约束）还是 B（代码支持字符串占位符）？-> **建议 B**，让 Agent 能真正在模板里参数化 `List[int]` 字段，否则条目 1 补齐的字段在模板里仍无法参数化。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
