# PRD — v0.22.24 Title 场景 PanelConfig 配置子面板导航

> **状态**：已实现
> **对应需求**：`requirements/PanelConfig相关逻辑需求.md`
> **最后更新**：2026-08-19

---

## 1. 背景与目标

Title 场景的 `PanelConfig`（设置面板）已更新：新增 4 个配置入口按钮（`BtnLLMAgent` / `BtnLLMMemory` / `BtnEmbedding` / `BtnReranker`）、4 个对应配置子面板（`PanelLLMAgent` / `PanelLLMMemory` / `PanelEmbedding` / `PanelReranker`）以及一个保存确认弹窗 `MsgboxSaveConfig`。

当前这些 UI 节点已就绪，但**缺少导航交互逻辑**：点击按钮不会打开对应面板、子面板里按 ESC 不会弹保存确认、也没有返回 `PanelConfig` 的方法。

**目标**：补齐 `PanelConfig` → 各配置子面板 之间的打开/关闭/ESC 导航逻辑，形成完整的「设置 → 子配置 → 返回设置」操作闭环。

## 2. 范围

### 2.1 本期包含

- 点击 `BtnLLMAgent` / `BtnLLMMemory` / `BtnEmbedding` / `BtnReranker` 任意一个，打开对应配置子面板，并关闭 `PanelConfig`。
- 在 4 个配置子面板内按 ESC，弹出 `MsgboxSaveConfig`（保存确认弹窗）。
- 提供一个方法：关闭 4 个配置子面板，并重新打开 `PanelConfig`。
- 在 `UITitle` 上新增对 4 个配置子面板、4 个按钮、`MsgboxSaveConfig` 的引用与逻辑。

### 2.2 本期不包含

- **不实现** 4 个配置子面板内部的配置读写逻辑（API Key / 模型 / Embedding / Reranker 参数等）——本期仅 UI 导航。
- **不实现** `MsgboxSaveConfig` 的实际保存动作——本期只负责「按 ESC 弹出它」；其按钮回调（确认保存 / 取消）的具体保存逻辑属于后续配置版本。
- **不迁移** Input System、不改 Python / 协议层。
- **不修改** `UIMsgBox` 通用弹窗脚本（`MsgboxSaveConfig` 复用 v0.22.23 的通用弹窗，本期只需在场景里配置其按钮回调）。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家 | Title 主菜单点「设置」 | 打开 `PanelConfig`，显示 4 个配置入口按钮 |
| 玩家 | `PanelConfig` 点 `BtnLLMAgent` | 关闭 `PanelConfig`，打开 `PanelLLMAgent` |
| 玩家 | `PanelConfig` 点 `BtnLLMMemory` | 关闭 `PanelConfig`，打开 `PanelLLMMemory` |
| 玩家 | `PanelConfig` 点 `BtnEmbedding` | 关闭 `PanelConfig`，打开 `PanelEmbedding` |
| 玩家 | `PanelConfig` 点 `BtnReranker` | 关闭 `PanelConfig`，打开 `PanelReranker` |
| 玩家 | 任一配置子面板按 ESC | 弹出 `MsgboxSaveConfig`（保存确认） |
| 玩家 | `MsgboxSaveConfig` 保存退出 | 关闭 4 个配置子面板，返回 `PanelConfig` |
| 玩家 | `MsgboxSaveConfig` 退出 | 关闭 4 个配置子面板，返回 `PanelConfig` |

## 4. 功能需求

### 4.1 打开对应配置子面板

- 提供 4 个方法（或一个带参方法），点击对应按钮时：**打开对应子面板**、**关闭 `PanelConfig`**。
- 每次只显示一个配置子面板（互斥），`PanelConfig` 与子面板不同时显示。

### 4.2 配置子面板内按 ESC 弹出保存确认

- 在 4 个配置子面板任一激活时，按 ESC（`Menu` 轴）弹出 `MsgboxSaveConfig`。
- 弹窗弹出时，底层配置子面板保持可见（作为弹窗背景）。
- 弹窗本身为覆盖层（v0.22.23 的 `UIMsgBox.OnEnable` 已 `SetAsLastSibling` 置顶）。

### 4.3 关闭配置子面板返回 PanelConfig

- 提供公开方法：关闭 4 个配置子面板，并打开 `PanelConfig`。
- 该方法的语义 = 「从任一配置子面板返回设置总览」，供 `MsgboxSaveConfig` 确认保存后调用。
- 在 `MsgboxSaveConfig` 处于打开状态时，ESC 不应再触发「再次弹窗」或「返回」逻辑（弹窗自身按钮接管）。

## 5. 非功能需求

- **纯 Unity 侧改动**，不引入新第三方依赖，不改协议、不改 Python。
- 复用 `UITitle` 现有输入消抖机制（`mInputLockTime`），避免 ESC / 任意键抖动。
- 与 v0.22.22 已定稿的「`UITitle` 总控」架构保持一致：面板切换集中在 `UITitle`，子面板脚本不参与导航。
- 场景侧 UI 节点已由用户就绪，Agent 仅补脚本逻辑 + 说明场景侧需做的拖拽绑定。

## 6. 验收标准

- [ ] `PanelConfig` 下点 `BtnLLMAgent`，关闭 `PanelConfig`、打开 `PanelLLMAgent`；其余 3 个按钮同理。
- [ ] 4 个配置子面板互斥，任一时刻至多显示一个；`PanelConfig` 与子面板不同时显示。
- [ ] 在任一配置子面板内按 ESC，弹出 `MsgboxSaveConfig`。
- [ ] `MsgboxSaveConfig` 确认后，关闭 4 个配置子面板，打开 `PanelConfig`。
- [ ] `MsgboxSaveConfig` 取消后，关闭弹窗，停留在原配置子面板。
- [ ] 从主菜单进入设置 → 进入子面板 → 返回设置 → 返回主菜单，全程状态稳定无抖动。

## 7. 待确认问题

- [x] **`MsgboxSaveConfig` 的按钮回调**—— **已确认**：确认保存按钮 → 关闭弹窗 + 返回 `PanelConfig`（`OnConfirmSaveConfig`）；取消按钮 → 仅关闭弹窗（`OnCancelSaveConfig`）。保存逻辑本身本期不实现。
- [x] **按钮绑定方式**—— **已确认**：4 个按钮 `onClick` 由场景侧拖拽绑定到 `UITitle` 的 4 个**无参**公开方法（`ShowLLMAgentConfig` / `ShowLLMMemoryConfig` / `ShowEmbeddingConfig` / `ShowRerankerConfig`）。
- [x] **子面板 ESC 语义**—— **已确认**：在子面板按 ESC 直接弹 `MsgboxSaveConfig`（而非先回 `PanelConfig`）。
- [x] **`MsgboxSaveConfig` 层级**—— **已确认**：统一放 UI 根节点下（与其他 Msgbox 平级），非 `PanelConfig` 子节点。
- [x] **弹窗 ESC 语义**—— **已确认**：任何弹窗打开时按 ESC = 触发该弹窗 `Btn1`（通用逻辑，改 `UIMsgBox.cs`）。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
