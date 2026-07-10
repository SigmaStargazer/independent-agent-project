# idle wakeup 无信息量心理活动应抑制写入长期记忆

> **来源**：需求池 `DevDocs/需求池/backlog.md` 条目 10（P1）
> **提交日期**：2026-07-10

## 现象

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

每条都会触发一次 `save_memory` -> `add_episode`。详见 `logs/prompts/小明/2026-06-29_19-50-46.log` line 11605~12482，连续 30+ 条几乎同义的 idle 响应。

## 直接后果

1. **触发条目 8 的主键冲突连锁**：Graphiti 事实去重把这些同义句判为同一条事实边，3 次 retry 都用同一 uuid，全部失败 -> Episode 被丢弃；
2. **Worker 日志被刷得很满**：每条 idle 响应都会跑一次 LLM 抽取（费 token）、3 次 retry（费时间），最终还失败；
3. **记忆图谱噪声**：即使 retry 成功，长期下来「一切如常」类无信息量 Episode 会大量堆积，挤压有用 Episode 的语义权重。

## 根因

- `agent_interuptible.py` 的 `save_memory` 节点目前**无条件**入队（只要本轮跑到了 END）。
- idle wakeup 的语义是「让 Agent 留意一下周围」，并非要求长期记忆这件事；但当前是把它当作普通用户消息处理，因此心理活动也被入队。
- 缺乏「内容与上一条几乎相同」的去重判断；缺乏「本轮无信息量、跳过记忆」的旁路。

## 候选方案（已确认采用方案 C）

| 方案 | 说明 |
|------|------|
| A | **prompt 侧**：在 idle wakeup 提示语里加一条「若与上一次状态完全一致，则只用极简词回应，不再展开心理活动」。改动最小，但只能减少而不能根治 |
| B | **节点侧**：`save_memory` 节点检查本轮 `mem_to_save` 是否与上一条已落库 Episode 相似度过高（hash / 长度差 / embedding 相似），过高则跳过入队。需要存最近一条文本摘要 |
| C | **入口侧**：`Agent.asend_message` 中识别 idle wakeup（前缀「你已经空闲了一段时间」）-> 标记本轮 `skip_memory=True`，`save_memory` 节点读到该标记直接 return |
| D | **组合**：A + C。idle wakeup 默认 skip_memory；但若 AI 决定主动调工具（说明 idle 触发了真正的行动），则不再 skip |

**已确认采用方案 C**：入口侧标记 idle wakeup 消息 `skip_memory=True`，`save_memory` 节点跳过写入；若 idle wakeup 触发了 Agent 主动调工具（说明 idle 触发了真正的行动），`cache_tool_mem` 节点将 `skip_memory` 置 `False`，该轮记忆正常写入。不改动 prompt。

## 影响范围预估

- Python：`agent_framwork/agents/agent_interuptible.py`（`save_memory` 节点 + State 中加 `skip_memory` 字段）、`Agent._enqueue_idle_wakeup_message`（识别 idle wakeup，注入标记）。
- 与条目 8 强相关：本条若先落地，条目 8 的触发概率会大幅下降；条目 8 仍需独立处理「正常推理路径下的事实去重 retry 兼容性」。
- 测试：不依赖 Unity 联调，可用 `pytest` 直接驱动 `Agent.aprocess_message` mock idle 输入。

## 复现日志

- `logs/prompts/小明/2026-06-29_19-50-46.log`（line 11605~12482，30+ 条 idle 响应）
- `terminals/4.txt:935-1024`（v0.21.7_fix_3 联调时 worker 报错段）
