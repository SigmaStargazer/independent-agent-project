# PRD — v0.23.1 API Key 配置优化

> **状态**：已实现
> **对应需求**：`requirements/API Key配置优化.md`
> **最后更新**：2026-08-22

---

## 1. 背景与目标

Title 场景 API Key 配置面板（v0.23.0）存在两处体验短板：

1. **重复配置成本高**：`PanelLLMAgent` 与 `PanelLLMMemory` 常常配置相同的模型，手动逐项填写两次很繁琐。
2. **API Key 可用性无法即时验证**：配置保存后要进入实际游戏场景才能发现 Key 无效，发现问题后还得回到设置面板逐项排查。

本版本目标：提供**一键复制**（Agent ↔ Memory 配置互拷）与**保存后即时测试**（当前面板模型的 API 连通性），降低配置成本、缩短验证回路。

## 2. 范围

### 2.1 本期包含

- **需求一**：`PanelLLMAgent` / `PanelLLMMemory` 各自的「复制」按钮，将另一组配置读取到当前面板的三个文本框。
- **需求二**：新增 `MsgboxSaveApiKey`（4 个模型配置 Panel 专用，Btn3 为「测试后保存」）。点「测试后保存」后立即测试**当前面板**模型的 API 可用性，测试通过后由用户确认才保存，按结果唤起 `MsgboxModelTesting` / `MsgboxModelAvailable` / `MsgboxModelUnavailable`。

### 2.2 本期不包含

- 不改变「ESC → MsgboxSaveApiKey」的退出确认流程（需求一复制后仍走该流程；「保存退出」按钮改名为「测试后保存」，保存动作推迟到测试通过后）。
- `MsgboxSaveSetting` 本版本**不再使用**，保留供以后增加其他设置项（分辨率/语言等）时使用（届时其「保存退出」语义恢复）。
- 不改变 `api_config.json` 的存储格式（仍为 12 个大写键）。
- 不引入 Title 阶段的系统初始化（保持 v0.23.0b「零系统」生命周期：测试独立于系统，仅发轻量探测请求）。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家/开发者 | 在 PanelLLMAgent 配好模型，想给 PanelLLMMemory 用同样配置 | 点「复制」按钮，Memory 三个文本框被 Agent 的配置填充 |
| 玩家/开发者 | 在任一配置面板点「测试后保存」 | 不保存、不切面板；先弹「正在测试模型可用性」，随后按结果弹「可用」或「不可用」 |
| 玩家/开发者 | 测试通过 | 弹「模型可用」，可「继续配置」（留当前面板）或「保存退出」（保存并返回 PanelSetting） |
| 玩家/开发者 | 测试失败 | 弹「模型不可用」，可「继续配置」修改或「退出」（不保存，返回 PanelSetting） |

## 4. 功能需求

### 4.1 一键复制（需求一）

- `PanelLLMAgent` 挂 `UILLMAgent`，提供 `OnClickCopy`：读取 Memory 组配置（Base/Key/Model）覆盖到 Agent 面板三个文本框。
- `PanelLLMMemory` 挂 `UILLMMemory`，提供 `OnClickCopy`：读取 Agent 组配置覆盖到 Memory 面板三个文本框。
- 复制动作只改文本框内容，**不自动保存**；用户按 ESC 走既有 `MsgboxSaveApiKey` 流程决定是否落盘。

### 4.2 测试后保存（需求二）

在 `MsgboxSaveApiKey`（4 个模型配置 Panel 专用）点「测试后保存」后：

1. **不保存**配置到 `api_config.json`，仅用**文本框当前值**发起「当前面板模型」的 API 可用性测试。
2. 关闭 `MsgboxSaveApiKey`，唤起 `MsgboxModelTesting`（测试中）。
3. 测试通过 → 关 `MsgboxModelTesting`、唤起 `MsgboxModelAvailable`。
4. 测试失败 → 关 `MsgboxModelTesting`、唤起 `MsgboxModelUnavailable`。
5. **保存动作推迟到测试通过之后**：仅在 `MsgboxModelAvailable` 点「保存退出」时才把配置写入 `api_config.json`。

> 目的：避免**不可用的配置覆盖掉原有可用的配置**。测试未通过（或未保存）时，`api_config.json` 保持原值；回到 PanelSetting 时会从文件回填，丢弃未保存的编辑。

**MsgboxSaveApiKey 按钮行为**（从 4 个模型配置 Panel 退出时固定弹此 Msgbox）：

| 按钮 | 行为 |
|------|------|
| Btn1 取消 | 只关闭 Msgbox，停留当前 Panel（不返回） |
| Btn2 退出 | 关闭 Msgbox，**不保存**，返回 PanelSetting（固定目标，无需记录来源） |
| Btn3 测试后保存 | 关闭 Msgbox，**不保存**，开始测试（见上） |

> 简化：`MsgboxSaveApiKey` 退出后固定返回 PanelSetting，因此**不再需要** v0.23.0b 中「记录弹窗来源层级」（`SaveMsgFrom`/`UILevel`）的复杂逻辑。`MsgboxSaveSetting` 本版本不再使用（保留供以后其他设置项使用）。

**测试范围**：只测「当前面板」对应的那组模型，不测其他 3 个面板：

| 当前面板 | 测试类型 | 探测方式 |
|----------|----------|----------|
| PanelLLMAgent | LLM | 发一条 chat 请求（max_tokens=1） |
| PanelLLMMemory | LLM | 发一条 chat 请求（max_tokens=1） |
| PanelEmbedding | embedding | 发一条 embedding 请求 |
| PanelReranker | rerank | 发一条 rerank 请求 |

### 4.3 三个结果 Msgbox 的按钮行为

| Msgbox | 按钮 | 行为 |
|--------|------|------|
| MsgboxModelTesting | 取消 | 停止测试，关闭该 Msgbox（停留当前面板，不返回） |
| MsgboxModelAvailable | 继续配置 | 关闭该 Msgbox，留在当前 Panel |
| MsgboxModelAvailable | 保存退出 | 关闭该 Msgbox，**保存配置到 api_config.json**，返回 PanelSetting |
| MsgboxModelUnavailable | 继续配置 | 关闭该 Msgbox，留在当前 Panel |
| MsgboxModelUnavailable | 退出 | 关闭该 Msgbox，返回 PanelSetting（**不保存**） |

### 4.4 取消测试

`MsgboxModelTesting` 点「取消」时：

- 停止进行中的测试（Unity 侧忽略/丢弃异步回调结果）。
- 关闭 `MsgboxModelTesting`，**停留当前 Panel**（不返回 PanelSetting）。

## 5. 非功能需求

- **零系统测试**：测试不触发任何系统初始化（不创建 Agent、不初始化 MemoryManager/EmbedderService 单例），Title 阶段保持 v0.23.0b「零系统」状态。
- **轻量探测**：chat 测试 max_tokens=1；embedding/rerank 测试用单条文本。
- **超时保护**：测试必须有明确超时（30s，比运行时 LLM 的 120s 短），超时视为「不可用」。
- **错误信息可读**：测试失败时 `errormsg` 应包含可理解的失败原因（如 401/403/404、超时、模型不存在等）。

## 6. 验收标准

- [ ] PanelLLMAgent 点「复制」后，Agent 面板 Base/Key/Model 三个文本框被 Memory 配置填充，未保存前不写盘。
- [ ] PanelLLMMemory 点「复制」后，Memory 面板三个文本框被 Agent 配置填充。
- [ ] 从 4 个模型配置 Panel 按 ESC（有变更）弹 `MsgboxSaveApiKey`，其 Btn1 取消停留、Btn2 退出固定返回 PanelSetting（不保存）、Btn3 测试后保存开始测试。
- [ ] 在任一面板点「测试后保存」后，**不保存、不切换 Panel**，先弹 MsgboxModelTesting。
- [ ] 测试通过时弹 MsgboxModelAvailable，且此时 `api_config.json` **尚未被写入**；点「继续配置」留在当前面板，点「保存退出」才写盘并返回 PanelSetting。
- [ ] 测试失败时弹 MsgboxModelUnavailable，且 `api_config.json` **保持原值**；点「继续配置」留在当前面板，点「退出」不保存并返回 PanelSetting。
- [ ] MsgboxModelTesting 点「取消」可停止测试，关闭弹窗并停留当前面板。
- [ ] 只测当前面板对应那组模型，不测试其他 3 组。
- [ ] 测试后 Title 阶段仍为零系统：Python 侧无任何系统被初始化（MemoryManager/EmbedderService/Agent 均未创建）。

## 7. 待确认问题

- [x] API 测试走 Python（新增协议）还是 Unity 直发？→ **已确认：走 Python**
- [x] 测试是独立轻量 ping 还是真实初始化？→ **已确认：独立轻量 ping（零系统）**
- [x] EMBEDDING/RERANKER 是否也测试？→ **已确认：四组都测，各自发对应类型请求**
- [x] 测试配置来源？→ **已确认：请求里携带当前面板对应组的 base/key/model**

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
