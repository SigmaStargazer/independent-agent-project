# DevDocs — 版本开发文档

本目录用于**按小版本组织的需求、PRD 与方案**。与 `Doc/` 中长期有效的技术指南（协议、工具开发流程等）分工如下：

| 目录 | 用途 |
|------|------|
| **`DevDocs/vX.Y/`** | 按版本号存放：你的需求 → Agent 生成的 PRD / 方案 → 确认后再开发 |
| **`DevDocs/feature-design/`** | 跨版本的功能设计原则、体验边界与迭代底线；供多个版本 PRD / 方案引用 |
| **`Doc/`** | 跨版本的技术参考与开发流程（不随单个版本归档） |
| **`AGENTS.md`** | 项目架构与 Agent 导航 |

---

## 目录约定

`DevDocs/` 同时承载两类文档：

1. **版本迭代文档**：每个小版本一个文件夹，命名建议：

```
DevDocs/
  v0.1/
  v0.2/
  v1.0/
  ...
```

版本号格式：`v{主版本}.{次版本}`（可按需加补丁位，如 `v0.1.1`）。

2. **跨版本功能设计文档**：放在 `DevDocs/feature-design/`，用于记录某个功能长期必须遵守的设计原则、体验目标、边界与迭代底线。例如：

```
DevDocs/feature-design/
  IdleWakeup.md
```

版本 PRD / 方案若涉及已有功能设计文档，必须在文档开头引用对应路径，并检查本次迭代是否违反其中原则。

### 单个版本文件夹结构

```
DevDocs/v0.x/
  requirements/     ← 你放需求文档（Markdown、图片、附件等）
  PRD.md            ← Agent 根据 requirements 生成，待你确认
  solution.md       ← Agent 根据 PRD 生成技术方案，待你确认
```

- **`requirements/`**：只放**你写的**原始需求、参考、截图说明等；Agent 不修改此目录内容。
- **`PRD.md`**：产品需求文档（范围、用户故事、验收标准）。
- **`solution.md`**：技术方案（涉及模块、协议/数据结构、实现步骤、风险）。

复制 **`_template/`** 可快速新建一个版本目录。

---

## 协作流程（人类 ↔ Cursor Agent）

```
你创建 DevDocs/vX.Y/requirements/ 并写入需求
        ↓
Agent 阅读 requirements，在同目录生成 PRD.md、solution.md
        ↓
你审阅并确认（或提出修改，Agent 只改 PRD/solution，不改 requirements）
        ↓
你明确说「可以开发 / 按方案实现」后，Agent 才开始改代码
        ↓
（可选）开发完成后在 solution.md 末尾补充「实现记录」
```

**Agent 必须遵守：**

1. 开发前先看当前版本目录下是否有 `requirements/`；有则**必须先读**再写 PRD/方案。
2. **未经你确认 PRD 与方案，不得开始写业务代码**（紧急 bugfix 或你明确说「跳过文档直接改」除外）。
3. 生成的 `PRD.md`、`solution.md` 放在**同一版本文件夹**内，不要散落到别处。
4. 若需求涉及 `DevDocs/feature-design/` 中已有功能，PRD / 方案必须引用对应功能设计文档，并检查是否违反其中原则。
5. 文件编码：**UTF-8**（见 `.cursor/rules/file-encoding.mdc`）。
6. **每个开发环节完成后必须同步更新 PRD.md / solution.md 的状态字段**（见下方状态流转规则）。

**你可以这样触发：**

- 「请阅读 `DevDocs/v0.2/requirements`，生成 PRD 和方案」
- 「v0.2 方案已确认，开始开发」
- 「按 DevDocs 最新版本的需求做」

---

## 与现有文档的关系

- 实现 Agent 工具、ActionSequence、改协议等：方案里应引用 `Doc/Agent工具开发流程.md`、`Doc/ActionSequence开发流程.md` 等。
- 架构背景：先读仓库根目录 `AGENTS.md`。

---

## 新建版本（快捷步骤）

1. 复制 `_template` 为 `DevDocs/vX.Y/`
2. 删除或重命名 `_template` 里不需要的占位文件
3. 在 `requirements/` 下添加你的需求 Markdown
4. 在 Cursor 中让 Agent 阅读该版本并生成 PRD / 方案

---

## 文档状态流转规则

PRD.md 和 solution.md 的**状态**字段必须随开发环节推进同步更新。完整状态枚举：

| 状态 | 含义 | 触发时机 |
|------|------|----------|
| `草稿` | Agent 已生成但尚未提交给用户审阅 | Agent 首次生成文档时 |
| `待确认` | 已提交给用户审阅，等待反馈 | Agent 生成完毕并告知用户后 |
| `已确认` | 用户明确确认文档内容，可进入下一环节 | 用户说「确认 / 可以开发 / 按方案实现」等 |
| `已实现` | 代码开发完成且用户验收通过 | 用户验收通过时 |

**PRD.md 状态流转**：

```
草稿 → 待确认 → 已确认
```

- Agent 生成 PRD 后立即设为「待确认」。
- 用户确认 PRD 后，Agent 更新为「已确认」。

**solution.md 状态流转**：

```
草稿 → 待确认 → 已确认 → 已实现
```

- Agent 生成方案后立即设为「待确认」。
- 用户确认方案后，Agent 更新为「已确认」。
- 用户验收通过后，Agent 更新为「已实现」。

**Agent 必须在每个环节完成时立即更新状态**，不得遗漏。同时更新「最后更新」日期。
