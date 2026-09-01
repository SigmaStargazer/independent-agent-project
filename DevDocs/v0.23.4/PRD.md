# PRD — v0.23.4 配置完整性提示 + 设置页多 Tab（模型配置 / 画面）+ 画面变更保存确认

> **状态**：待确认
> **对应需求**：`requirements/v0.22.24功能补全.md`
> **引用方案**：`DevDocs/feature-design/打包方案.md`（§3.3 端口/配置目录、§4.2 玩家 API 配置存储、§8 决策项）
> **基于版本**：`DevDocs/v0.23.1/`（MsgboxSaveApiKey / UISetting / 测试后保存）、`DevDocs/v0.23.0a-b/`（api_config.json / JsonConfigIO / 零系统）
> **最后更新**：2026-08-31（用户确认最终架构：PanelSetting 根容器 + PanelTab + ContentDisplaySettings；弃 UIGameSettings，画面并入 UISetting；新增 MsgboxSaveSetting）

---

## 1. 背景与目标

Title 场景的设置页（`PanelSetting`）在 v0.23.0/v0.23.1 已具备完整的「4 个模型配置子面板 + `MsgboxSaveApiKey`（测试后保存）+ 测试结果弹窗」链路。但存在两类问题：

1. **配置完整性判断缺失**：从模型配置 Panel 按 ESC 退出时，只要有编辑就会弹 `MsgboxSaveApiKey`。当某文本框为空（用户清空、或本来就没填）时仍会弹它询问「测试后保存」，而空配置下测试必然失败、无意义——需要一个「配置不完整」的专门提示弹窗。
2. **设置页结构单一**：`PanelSetting` 目前只有「进入 4 个模型配置」这一项能力，无法承载后续的画面设置、语言设置等。需求要求把 `PanelSetting` 扩展为**多标签页**（本次先实现 `模型配置` / `画面` 两个，架构上支持未来任意数量），并实现画面页的**显示模式**（窗口化/无边框/全屏）与**分辨率**两项配置。同时，前序 UI 实现未经认真设计、架构不成熟，本版本需先调研成熟方案，并评估是否做 UI 架构重构。
3. **画面变更无保存确认**：画面设置（显示模式/分辨率）改动后退出设置页，当前会直接返回主菜单，无「是否保存」确认——用户新增需求：退出设置页时若画面设置有变更，弹 `MsgboxSaveSetting` 确认（保存并退出 / 退出 / 取消）。

**目标**：

1. 新增 `MsgboxEmptyApiKey`：配置 Panel 内三个文本框至少一个为空时按 ESC 退出，弹此弹窗（继续配置 / 退出），不再弹 `MsgboxSaveApiKey`。
2. `PanelSetting` 扩展为**多标签页**（架构上支持任意数量 Tab，本次实现「模型配置」+「画面」两个）：`模型配置`（现有 4 个配置按钮）+ `画面`（显示模式、分辨率两项左右箭头切换配置）。**未来会继续增加 Tab**（如「游戏」Tab 放置语言切换），Tab 架构必须可扩展。`PanelSetting` 作为设置页**根容器**（背景图 + `UISetting`），内含 `PanelTab`（Tab 按钮容器）+ 4 个配置子面板，同一层级显示互斥。
3. **新增 `MsgboxSaveSetting`**：退出设置页（`PanelTab` 按 ESC）时若画面设置有变更，弹窗确认（保存并退出 / 退出 / 取消）。
4. 先完成「显示模式 / 分辨率」与「UI 多 Tab 架构」的**业界成熟方案调研**，据此给出架构方案（含是否重构/引入 UI 包的决策）。
5. 语言切换（中英文）作为**独立调研项**：本期不实现，但给出业界成熟方案与「本期实现应如何为后续语言切换铺路」的建议。文案采用**数据驱动（独立文件存储）**的集中管理，为后续接入 Localization 铺路。

## 2. 范围

### 2.1 本期包含

- **需求一**：新增 `MsgboxEmptyApiKey` 弹窗（**两个按钮**：继续配置 / 退出，见 §4.1）；调整 ESC 退出子面板的弹窗选择逻辑（空框 → EmptyApiKey，无空框且有变更 → SaveApiKey，无变更 → 直接返回）。
- **需求二**：`PanelSetting` 扩展为**可扩展多标签页**——`PanelSetting` 作为设置页根容器（背景图 + `UISetting`），内含 `PanelTab`（Tab 按钮容器）+ 4 个配置子面板（同层互斥）；本次实现 `模型配置`（现有 4 个按钮）+ `画面`（`ContentDisplaySettings`：显示模式、分辨率两项，左右箭头切换，边界禁用），架构支持未来任意数量 Tab（`ContentGameSettings` 预留「游戏」Tab，本期不创建）。
- **需求三（用户新增）**：**`MsgboxSaveSetting`**——退出设置页（`PanelTab` 按 ESC）时若画面设置有变更，弹窗确认（保存并退出 / 退出 / 取消）。
- **调研交付**：显示模式/分辨率切换、UI 多 Tab 架构、语言本地化的成熟方案调研（写入 `solution.md`），并给出 UI 架构决策（是否重构 / 是否引入 UI 包）。

### 2.2 本期不包含

- **语言切换实现**：本期仅调研并给出业界方案与「铺路」建议；不在本期实现（需求三已注明「可选择以后版本再实现」）。但本期 Tab 架构已预留「游戏」等未来 Tab 位（`ContentGameSettings`，语言切换届时放入），且文案采用数据驱动文件管理，切换语言时无需改代码/场景。
- **`ContentGameSettings`（游戏 Tab）内容**：本期**不创建**（用户明确「未添加，放语言设置等」），仅预留架构位。
- **画面设置的持久化生效时机**：本期实现 `GameSettingsStore`（普通偏好存储）与应用，但不涉及进游戏后热切换。
- 显示模式「全屏」与窗口模式的 Alt+Enter 系统快捷键冲突处理等扩展项。
- **不迁移 Input System**、不改协议、不改 Python。
- 不引入第三方 UI 包（方案调研结论，见 `solution.md` §2）。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家 | 在模型配置 Panel 中某文本框为空（或清空某项）后按 ESC 退出 | 弹出 `MsgboxEmptyApiKey` 而非 `MsgboxSaveApiKey` |
| 玩家 | `MsgboxEmptyApiKey` 点「继续配置」 | 关闭弹窗，留在当前模型配置 Panel 继续填写 |
| 玩家 | `MsgboxEmptyApiKey` 点「退出」 | 关闭弹窗，返回 `PanelTab`（不保存） |
| 玩家 | 打开 `PanelSetting` | 显示多标签页（本次 模型配置 / 画面），默认在「模型配置」 |
| 玩家 | 切到「画面」标签（`ContentDisplaySettings`） | 显示「显示模式」「分辨率」两项，各带左右箭头切换 |
| 玩家 | 切换显示模式到最左（窗口化）/最右（全屏） | 对应箭头按钮不可点击，文本显示当前模式 |
| 玩家 | 切换分辨率到最左（最低）/最右（最高） | 对应箭头按钮不可点击；列表含 1920x1080 与一个常见 4:3 选项 |
| 玩家 | 在「画面」标签改动设置后回 `PanelTab` 按 ESC 退出设置页 | 弹 `MsgboxSaveSetting`（保存并退出 / 退出 / 取消） |
| 玩家 | `MsgboxSaveSetting` 点「保存并退出」 | 保存画面设置并返回主菜单 |
| 玩家 | `MsgboxSaveSetting` 点「退出」 | 不保存画面设置，返回主菜单 |
| 玩家 | `MsgboxSaveSetting` 点「取消」 | 仅关闭弹窗，留在设置页 |
| 玩家 | 重新进入游戏/重启 | 画面设置保持上次选择（持久化到 `GameSettingsStore`） |

## 4. 功能需求

### 4.1 需求一：`MsgboxEmptyApiKey`（配置不完整提示）

**触发条件**：从 4 个模型配置 Panel 任一按 ESC 退出时，若**该 Panel 三个文本框至少有一个为空**，弹 `MsgboxEmptyApiKey`；否则维持现状（有变更弹 `MsgboxSaveApiKey`，无变更直接返回 `PanelTab`）。

**按钮**（已确认：两个按钮——需求原文「三个按钮」为用户笔误）：

| 按钮 | 行为 |
|------|------|
| 继续配置 | 关闭 `MsgboxEmptyApiKey`，留在当前模型配置 Panel |
| 退出 | 关闭 `MsgboxEmptyApiKey`，返回 `PanelTab`（不保存） |

> 语义：空配置下「测试后保存」无意义，故不再走 `MsgboxSaveApiKey` 的测试链路。

**判定顺序**（ESC 退出子面板）：

```
TryLeaveSubPanel:
  若 HasConfigChanged() == false → ShowPanelTab()（直接返回 PanelTab，设置页主界面）
  否则若 当前 Panel 三个文本框存在空项 → 弹 MsgboxEmptyApiKey
  否则 → 弹 MsgboxSaveApiKey
```

> 空项判定以**当前文本框内容**为准（不是以文件配置为准）：用户清空、或原本就空，均视为「有空框」。注：`UISetting` 已对 12 个输入框关闭 `restoreOriginalTextOnEscape`（v0.23.1），ESC 时文本框内容即为用户当前输入。
>
> 导航（用户确认）：4 个配置子面板按 ESC → 返回 **`PanelTab`**（设置页主界面，含 Tab 按钮）；`PanelTab` 按 ESC → 关闭 `PanelSetting`，返回 **`PanelMenu`**（主菜单）。

### 4.2 需求二：`PanelSetting` 多标签页（可扩展）

`PanelSetting` 作为设置页**根容器**（放设置期间的背景图 + `UISetting`，配置期间保持激活），采用**可扩展的多 Tab 架构**：Tab 列表由一个**配置表（数据驱动）**定义，未来新增 Tab（如「游戏」）只需在表中加一项 + 场景加内容区，**无需改 Tab 切换代码**。本期实现两个 Tab，同一时刻只显示一个：

**层级（用户最终确认）**：

```
PanelSetting（根容器：背景图 + UISetting）
├── PanelTab（Tab 按钮 + 内容区容器）
│   ├── TabModelConfig / TabDisplaySettings / （TabGameSettings 预留）
│   ├── ContentModelConfig（模型配置 Tab）
│   ├── ContentDisplaySettings（画面 Tab）
│   └── ContentGameSettings（未添加，预留「游戏」Tab）
├── PanelLLMAgent / PanelLLMMemory / PanelEmbedding / PanelRerank（4 个配置子面板，移入 PanelSetting）
```

同一层级显示互斥：`PanelTab` 与 4 个配置子面板互斥（显示子面板时隐藏 `PanelTab`）；`ContentModelConfig`/`ContentDisplaySettings`/`ContentGameSettings` 互斥（Tab 切换）。

**Tab：模型配置**
- 内容 = 现有 4 个模型配置按钮（`BtnLLMAgent` / `BtnLLMMemory` / `BtnEmbedding` / `BtnReranker`），行为不变（点击进入对应配置子面板）。

**Tab：画面（`ContentDisplaySettings`）**
- **显示模式**：左、右两个箭头按钮，顺序切换「窗口化 → 无边框 → 全屏」三种模式。显示当前模式文本。当前在最左（窗口化）时左箭头禁用，最右（全屏）时右箭头禁用。
- **分辨率**：左、右箭头按钮切换分辨率，**最左为最低分辨率、最右为最高分辨率**（按宽高/面积递增排序）。显示当前分辨率文本。选项列表**至少包含 `1920x1080` 与一个常见 4:3 比例选项**（如 `1024x768`）；到边界时对应箭头禁用。

**Tab 交互**：提供 Tab 切换按钮（或文本按钮），点击切换各标签内容区显隐；`PanelSetting` 打开时默认显示「模型配置」标签。Tab 按钮由数据驱动生成或按表绑定，选中态高亮，支持横向扩展。**Tab/Content 切换属于设置页面板内部导航**，由设置页自身的 `UISetting`（挂 `PanelSetting` 根容器，配置期间保持激活）负责，不由 Title 页面总控（`UITitle`）管理。

**画面配置并入 `UISetting`（用户明确弃独立 `UIGameSettings`）**：因 `ContentDisplaySettings` 会随 Tab 切换失活（`SetActive(false)` 停止生命周期回调），不能挂其上的独立脚本；故显示模式/分辨率切换与变更检测逻辑**并入 `UISetting`**，通过引用驱动 `ContentDisplaySettings` 内控件。变更检测用「**对比当前值与已保存值**」（`UISetting.HasDisplaySettingsChanged()` 与 `GameSettingsStore.Load()` 比较）。

**未来 Tab 位预留**：架构上预留任意数量 Tab 位；语言切换届时放在「游戏」Tab（`ContentGameSettings`，本期**不创建**）中（用户已确认该方向），本期不实现其内容，仅保证「新增 Tab 不改 Tab 框架代码」。

### 4.2.1 需求三（用户新增）：`MsgboxSaveSetting`（画面变更保存确认）

**触发时机（用户确认）**：退出 `PanelSetting`（`PanelTab` 按 ESC）时，若画面设置（显示模式/分辨率）相对已保存值**有变更**，弹 `MsgboxSaveSetting`（复用现有 `mSaveSettingMsgBox`，`Msgbox.prefab` 三按钮弹窗）。

**按钮（用户确认）**：

| 按钮 | 行为 |
|------|------|
| 保存并退出 | 保存画面设置（写 `game_settings.json`），返回 `PanelMenu` |
| 退出 | 不保存，返回 `PanelMenu` |
| 取消 | 仅关闭弹窗，留在设置页 |

**无变更**时直接返回 `PanelMenu`（不弹窗）。

### 4.3 画面设置的持久化与应用

- 新增 `GameSettingsStore`（复用 `JsonConfigIO`，与 `ApiConfigStore` 同级）：保存 `displayMode`（枚举）与 `resolution`（宽高）。
- 游戏启动（Title 加载）时读取并应用已保存的画面设置；无保存则用 Unity 默认。
- 切换显示模式/分辨率时：**立即应用**到 `Screen`（体验即改即见），并写入 `GameSettingsStore`。

### 4.4 调研要求（本版本交付）

需求二要求「先调研成熟实现方案」、需求三要求「提供语言切换业界成熟方案」。调研结论与建议必须写入 `solution.md`，至少覆盖：

1. **显示模式 / 分辨率切换**：Unity 官方 `Screen.fullScreenMode` / `Screen.SetResolution` 的正确用法与边界。
2. **UI 多 Tab / 设置页架构**：uGUI 下设置页 Tab 布局的成熟做法；以及「完全代码重构 vs 引入成熟 UI 包」的评估与决策。**必须支持未来扩展任意数量 Tab**（如「游戏」Tab 放置语言切换）。
3. **语言切换（中英文）**：业界成熟方案（官方 Localization 包 vs I2 Localization 等）对比与选型建议；以及本期 UI 实现应如何为后续语言切换铺路——**文案应采用数据驱动（独立文件存储）而非硬编码到代码**，调研业界主流做法（翻译文件 / 表格 / ScriptableObject / Localization 表的组织方式）并给出本项目的落地方式。

## 5. 非功能需求

- **架构优先**（已确认约定 0）：以架构最干净为最高目标；本次 `PanelSetting` 扩展是重构的契机，需给出干净的职责拆分，避免继续在 `UITitle` 堆逻辑。
- **纯 Unity 侧改动**：不改协议、不改 Python、不新增第三方依赖（除非方案调研确认引入成熟 UI 包利大于弊，需用户拍板）。
- **复用既有基础设施**：弹窗复用 v0.22.23 `Msgbox.prefab`（`UIMsgBox`，含 ESC→Btn1 通用逻辑）；配置持久化复用 `JsonConfigIO`。
- **输入消抖**：沿用 `UITitle` 的 `mInputLockTime`；弹窗激活时 `UIMsgBox.AnyActive` 机制不破坏。
- **文案数据驱动**：所有 UI 文案**不硬编码到 C# 代码**，集中存放到独立文件（如 JSON / ScriptableObject / 资源表），为后续语言切换（Localization）铺路。
- **UTF-8**：所有改动文件按 UTF-8（`.cs` 用 UTF-8 无 BOM，文案资源文件 UTF-8），见 `.cursor/rules/file-encoding.mdc`。

## 6. 验收标准

- [ ] 模型配置 Panel 内某文本框为空时按 ESC，弹 `MsgboxEmptyApiKey`（而非 `MsgboxSaveApiKey`）。
- [ ] `MsgboxEmptyApiKey`「继续配置」关闭弹窗、留在当前 Panel；「退出」关闭弹窗、返回 `PanelTab`。
- [ ] 文本框全非空且有变更时按 ESC，仍弹 `MsgboxSaveApiKey`（现状不回归）。
- [ ] `PanelSetting` 打开显示多标签页（本次 模型配置 + 画面），默认「模型配置」，展示 4 个配置按钮且点击行为不变。
- [ ] 切到「画面」标签，显示「显示模式」「分辨率」两项。
- [ ] 显示模式左/右箭头可切换「窗口化/无边框/全屏」；最左时左箭头禁用、最右时右箭头禁用。
- [ ] 分辨率左/右箭头按升序切换；最左（最低）时左箭头禁用、最右（最高）时右箭头禁用；列表含 `1920x1080` 与一个常见 4:3 选项。
- [ ] 切换画面设置即应用到 `Screen`，并写入 `GameSettingsStore`；重进 Title 后保持上次选择。
- [ ] 在「画面」标签改动显示模式/分辨率后回 `PanelTab` 按 ESC 退出设置页，弹 `MsgboxSaveSetting`。
- [ ] `MsgboxSaveSetting`「保存并退出」保存画面设置并返回主菜单；「退出」不保存返回主菜单；「取消」仅关闭弹窗留在设置页。
- [ ] 画面设置无变更时退出设置页，直接返回主菜单（不弹 `MsgboxSaveSetting`）。
- [ ] 4 个配置子面板按 ESC 返回 `PanelTab`；`PanelTab` 按 ESC 返回主菜单（导航正确）。
- [ ] 弹窗打开时按 ESC 不双重触发（`UIMsgBox.AnyActive` 协调，现状不回归）。
- [ ] Tab 架构为数据驱动可扩展：新增一个 Tab（含场景内容区）**无需改 Tab 切换代码**（验收：在配置表中临时加一项可运行）。
- [ ] 本版本新增 UI 文案全部来自独立文案文件，代码中无硬编码中文文案（验收：检索代码无新增中文字符串字面量）。
- [ ] `solution.md` 含三块调研结论（显示模式/分辨率、UI Tab 架构与重构决策、语言切换选型 + 文案文件化落地方式），并附「场景调整指引」文档。

## 7. 待确认问题（已按用户反馈更新）

- [x] **`MsgboxEmptyApiKey` 按钮数**—— **已确认：两个按钮（「继续配置」/「退出」）**。需求原文「三个按钮」为用户笔误，无第三个按钮。
- [x] **UI 架构方向**—— **已确认：uGUI 内职责拆分 + 数据驱动 Tab 架构**，不引入成熟 UI 包、不迁 UI Toolkit（见 solution §2/§3）。
- [x] **设置页层级**—— **已确认：`PanelSetting` 作为设置页根容器（背景图 + `UISetting`），内含 `PanelTab`（Tab 按钮容器）+ 4 个配置子面板（移入 `PanelSetting`），同一层级显示互斥**（用户明确最终层级结构）。
- [x] **画面配置实现方式**—— **已确认：弃独立 `UIGameSettings`，画面配置并入 `UISetting`**（因 `ContentDisplaySettings` 随 Tab 失活）；变更检测用「对比当前值与已保存值」。
- [x] **`MsgboxSaveSetting`（用户新增需求）**—— **已确认：退出设置页（`PanelTab` 按 ESC）时画面设置有变更则弹窗确认（保存并退出 / 退出 / 取消），复用现有 `mSaveSettingMsgBox`**；导航改为「4 子面板 ESC → 返回 PanelTab；PanelTab ESC → 检测后回主菜单」。
- [x] **画面设置持久化位置**—— **已确认：`Data/Config/game_settings.json`**（复用 `JsonConfigIO`，与 `api_config.json` 同目录，与打包方案 §4.2 配置目录体系一致）。
- [x] **分辨率选项来源**—— **已确认：预置列表**（固定含 1920x1080 + 常见 4:3，最左最低/最右最高），不从 `Screen.resolutions` 动态过滤。
- [x] **显示模式与分辨率联动**—— **已确认：同一分辨率列表，随显示模式应用**（简化处理）。
- [x] **语言切换本期不实现**—— **已确认**：仅调研 + 文案数据驱动（独立文件）铺路；语言切换未来放「游戏」Tab（`ContentGameSettings` 本期不创建）。
- [x] **场景调整方式**—— **已确认：由用户手动调整**。Agent 提供独立《场景调整指引》文档（仿 v0.23.1 场景绑定指引格式）。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
