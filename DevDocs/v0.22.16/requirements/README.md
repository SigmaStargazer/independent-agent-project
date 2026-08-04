# 需求文档目录

请将**本版本**的原始需求放在此目录下，例如：

- `功能概述.md`
- `交互说明.md`
- `验收标准.md`
- 截图、参考链接（可写在 Markdown 中）

**说明：**

- 此目录由**产品/你**维护；Cursor Agent 只读取，不修改此处文件。
- Agent 会根据此处内容，在上一级目录生成 `PRD.md` 与 `solution.md`。

---

## 本版本需求来源

本版本需求来自用户口头表述（2026-08-03 对话）：

> 我需要给我的 Chara、Device 的各个状态都增加动画。
> 所以 Animator 的控制是应该放在 HumanPlayer 或者 SceneObjBase 里，还是单独写个动画控制的脚本组件？

经 Agent 分析现有架构（`SceneObjBase` 已有统一 FSM + `OnStateChanged` 事件），结论为：**单独写一个动画控制脚本组件**，订阅状态变更事件驱动 Animator，不侵入现有基类。

PRD 与 solution 即基于此结论展开。
