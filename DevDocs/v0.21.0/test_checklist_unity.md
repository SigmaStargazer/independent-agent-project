# v0.21.0 Unity 集成验收清单

> 用途：Python 端核心已自测通过（19 项 smoke + 真实 embedding RAG），本清单覆盖**必须在 Unity 联调环境下才能验证**的场景。逐项打勾后即可标记 v0.21.0 为「已验收」。

## A. 默认技能注入（NewGameFlow / CreateAgent）

| ID  | 步骤                                                                                                  | 期望                                                                                                          | 通过 |
| --- | ----------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- | ---- |
| A1  | Unity 启动新游戏，触发 `NewGameFlow.CreateAgentStep`                                                  | Python 日志看到 `[default_skill_loader] loaded N default skills for <agent_name>`                              | ☐    |
| A2  | A1 之后调用 `list_action_skills` 工具 / 直接 Cypher 查 `MATCH (s:ActionSkill) WHERE s.group_id=$gid` | 至少返回 `default_skills.yaml` 中定义的 1 个示例技能（"走到目标旁交互/平地接近"）；`source` 字段为 `"default"` | ☐    |
| A3  | 故意把 `default_skills.yaml` 改成非法 YAML 后再 NewGame                                                | Agent 创建仍成功，Python 仅日志报错，**不阻断** AgentCreate Response                                          | ☐    |
| A4  | NewGame → BackupMemory(0) → SaveData → 退出 → ContinueGame                                             | Continue 后 `list_action_skills` 仍能列出默认技能（说明已随 backup 持久化）                                   | ☐    |

## B. ActionSequence 完成时的回顾提示（AIPlayer.cs）

| ID  | 步骤                                                                                              | 期望                                                                                                                                  | 通过 |
| --- | ------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| B1  | LLM 调用任意 ActionSequence 工具（如移动到目标），Unity 执行**成功**完成 → `CompleteActionSequence` | Python 收到的 `UserSendFeedbackRequest.message` 末尾包含 `ACTION_SEQUENCE_REVIEW_PROMPT`（"刚才的动作序列…是否需要…create/refine…"） | ☐    |
| B2  | 同 B1 但 ActionSequence **执行失败**（条件不满足、被打断等）→ `OnActionFinished` 失败分支         | Feedback message 同样追加回顾提示                                                                                                     | ☐    |
| B3  | 普通环境事件反馈（角色出现、消失、消息接收）                                                      | 反馈 message **不带**回顾提示（仅 ActionSequence 完成才追加）                                                                         | ☐    |
| B4  | 把 Python 端 `agent_interuptible.py` 里 system prompt 中"动作技能记忆"小标题改一个字              | B1 仍能正常触发 LLM 调用 `create_action_skill` 或 `refine_action_skill`（验证**没有**字符串依赖）                                     | ☐    |

## C. 系统提示注入（top-N RAG）

| ID  | 步骤                                                                                                       | 期望                                                                                          | 通过 |
| --- | ---------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- | ---- |
| C1  | Agent 已存在 ≥3 个技能，玩家发一条与某技能相关的消息（如"我要过河"）                                       | 该轮 LLM 收到的 system prompt 中 `<动作技能记忆>` 排在第 1 的是"过河"；至多列出 `top_n` 条    | ☐    |
| C2  | 修改 `.env` 中 `SKILL_INDEX_TOP_N=3` 重启 Python，造 5 个技能后发消息                                      | system prompt 中 `<动作技能记忆>` 仅列前 3 条                                                 | ☐    |
| C3  | 删除所有技能后发消息                                                                                       | `<动作技能记忆>` 区块为空字符串（或仅保留固定使用规则文字），不会渲染出"None"/异常             | ☐    |
| C4  | embedding 服务挂掉（断网或改错 `EMBEDDING_API_BASE`）                                                      | `search_memory` 节点仍能跑完，技能索引部分降级为空，事实/情景 RAG 不受影响                    | ☐    |

## D. 备份 / 恢复 / 删除（GameFlow 集成）

| ID  | 步骤                                                                                                                | 期望                                                                                       | 通过 |
| --- | ------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ | ---- |
| D1  | NewGame → 通过 LLM/手动调用 `create_action_skill` + `add_action_skill_template` 创建 1 个 learned 技能 → BackupMemory(0) | `db/backups/slot_0/graphiti.kuzu` 大小 > 上次备份；从 backup 直接打开能 MATCH 到该技能      | ☐    |
| D2  | D1 后 `delete_current_memory()`（NewGameFlow 路径）→ list_action_skills                                              | 全空（包括默认技能；NewGame 之后才会重新注入）                                             | ☐    |
| D3  | RestoreMemory(0) → list_action_skills                                                                               | D1 创建的 learned 技能 + 默认技能均恢复                                                    | ☐    |
| D4  | NextMapFlow（Interrupt → Backup → LoadScene → Start）后                                                              | 切关后第一条用户消息触发的 system prompt 中仍能看到原 group_id 下的所有技能                | ☐    |

## E. 写入并发与 freeze 保护

| ID  | 步骤                                                                                                                                  | 期望                                                                                                                              | 通过 |
| --- | ------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- | ---- |
| E1  | 在 LLM 正在执行长 ActionSequence、即将调用 `add_action_skill_template` 时，玩家触发 BackupMemory                                       | Backup 等待该写入完成才开始（不会丢数据 / 不会写到一半被 backup 截断）                                                            | ☐    |
| E2  | Backup 进行中 LLM 试图调用 `create_action_skill`                                                                                       | 工具调用 await `_freeze`，backup 完成后再继续；不会抛连接错误                                                                     | ☐    |
| E3  | 多 Agent 同时 create skill                                                                                                            | 各自 `group_id` 数据互不干扰；list 时只看到自己的技能                                                                              | ☐    |

## F. 跨场景 / 跨会话回放

| ID  | 步骤                                                                                                              | 期望                                                                                          | 通过 |
| --- | ----------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- | ---- |
| F1  | 一个完整会话：玩家给 Agent 几个相似任务 → 引导 LLM 用 `create_action_skill` 总结技能 → BackupMemory → 关 Unity 重启 → ContinueGame | Agent 在新会话中收到相似任务时，system prompt 已带回上轮技能；LLM 能**复用**而非重新创建      | ☐    |
| F2  | F1 之后玩家给反例 → 引导 LLM `refine_action_skill` 修改 template description / 注意事项                          | 修改后 `updated_at` 刷新，`description_embedding` 重算；下次 RAG 命中行为符合新描述           | ☐    |
| F3  | LLM 决定弃用某技能 → `delete_action_skill_template`（最后一个模板被删时连带删除整个 ActionSkill）                | Kuzu 中既无 ActionSkill 也无 Template；后续 system prompt 不再出现该技能                       | ☐    |

---

## 排查指引

- **A1 / A2 失败**：检查 `main.py.handle_agent_create_request` 是否在 `await acreate_agent(...)` 之后调用 `load_default_skills(name)`；检查 `default_skills.yaml` 是否在 `Src/PythonServer/action_skill_system/` 目录下（YAML 路径相对模块）。
- **B1–B3 失败**：检查 Unity `AIPlayer.cs` 的 `ACTION_SEQUENCE_REVIEW_PROMPT` 是否在两处（`OnActionFinished` 失败分支 + `CompleteActionSequence` 成功分支）都拼到了 feedback message；普通事件反馈路径应**不**拼。
- **C1 失败**：检查 `.env` 是否配置 `EMBEDDING_API_*` 三件套；查 Python 日志有无 `[ActionSkillManager] embed failed` 警告。
- **D1–D4 失败**：确认 `db/graphiti.kuzu` 单库已包含 ActionSkill 表（schema 注入随 `MemoryManager.initialize` → `ActionSkillManager.initialize`）；备份文件直接复制即可，无需额外导出。
- **E2 失败**：确认 `ActionSkillManager.create_skill` / `add_template` / `refine_skill` / `delete_*` 全部进入 `async with self._mm.memory_access():` 上下文。

---

## G. 训练场基础设施（v0.21.0 训练场扩展，详见 `solution_training_ground.md`）

| ID | 步骤 | 期望 | 通过 |
|---|---|---|---|
| G1 | UI 按钮调 `AgentService.Instance.SendAgentExportSkills(name)`，Agent 已有 ≥1 个技能 | 收到 `OnAgentExportSkills(success=true, errormsg="", skillCount==实际数)`；`Src/PythonServer/db/default_skills/exports/` 下出现 `<safeName>_<timestamp>.yaml` | ☐ |
| G2 | 打开 G1 写出的 yaml 文件 | 能被 `yaml.safe_load` 解析；`source` 保留训练原值（learned/refined/default 任一） | ☐ |
| G3 | 把 G1 文件改名为 `<group_id>.yaml` 移到 `db/default_skills/` → 删档 → NewGame | 新 Agent 创建后 `list_action_skills` 列出全部导出技能；source 全为 `"default"` | ☐ |
| G4 | 删除 `<group_id>.yaml`，仅留 `default.yaml` → NewGame 一个**新 group_id** 的 Agent | 新 Agent 注入 `default.yaml` 内容；老 group_id（仍有 `<group_id>.yaml`）的 NewGame 走自己定制版 | ☐ |
| G5 | AIPlayer 走到 CheckPoint 上 | `LastCheckPoint` 被记录；无 Feedback 发送（CheckPoint 只是隐式记录，不打扰 LLM） | ☐ |
| G6 | AIPlayer 触碰 Trap | 立即位移到 `LastCheckPoint.GetRespawnPosition()`（即 respawnAnchor 位置）；Rigidbody.velocity 与 angularVelocity 都为 0；ActionSequence/Action 全停（`mCurActionRuntime == null`，ActionSequenceState == Aborted） | ☐ |
| G7 | G6 之后 Agent 收到的 Feedback 包含"已被传送回最近的检查点"字样 | LLM 下一轮推理基于该反馈决策（feedback 自带打断语义，等价于 force_interrupt=true） | ☐ |
| G8 | AIPlayer 从未碰过 CheckPoint 就掉进 Trap | 控制台打印 `ReturnToCheckPoint called but LastCheckPoint is null` 警告、不传送、保持原位；`StopMovement(true)` 仍被执行（mCurActionRuntime 被清） | ☐ |
| G9 | Trap 触发时 LLM 正在执行 ActionSequence | ActionSequence 被打断；feedback 队列中可见 `[训练场反馈]…当前动作序列已中断` 字样 | ☐ |
| G10 | CheckPoint 的 `respawnAnchor` 字段未设置（Inspector 留空） | 重生位置 = CheckPoint 自身 transform.position；运行时不抛 NullReference | ☐ |
| G11 | 多个 CheckPoint 依次触碰 A → B → C，再触发 Trap | 重生到 C 的 anchor；不是几何最近的那个 | ☐ |
| G12 | `db/default_skills/` 不存在时（首次运行） | `load_default_skills` 返回空列表；Agent 创建仍成功 | ☐ |

---

*完成所有 ☐ 后，请把本文件留作 v0.21.0 验收记录；如有失败项请在对应行下加 `> 失败原因 + 修复 commit` 链接。*
