# PRD — v0.23.5 UI 语言切换

> **状态**：已确认
> **对应需求**：`requirements/UI语言切换.md`
> **最后更新**：2026-09-03

---

## 1. 背景与目标

游戏目前 UI 文案全部为简体中文硬编码（场景静态文本 / 代码字符串），无法切换语言。目标：在设置页新增「游戏」Tab，提供**简体中文 / English** 切换（默认简体中文），并让 Title 场景、Bootstrap 场景（FlowStep 进度名 + 错误弹窗）的文案随语言切换。

语言切换的数据模型**复用现有 `GameSettingsModel`**（v0.23.4 MVC 化产物），将语言作为其新增字段持久化到 `game_settings.json`。

## 2. 范围

### 2.1 本期包含

- 设置页 `PanelTabs` 新增 `TabGameSettings`（「游戏」Tab），点击切换显示 `ContentGameSettings`。
- `ContentGameSettings` 内提供语言选择：左右箭头切换 **简体中文 / English**（默认简体中文）。
- 语言数据进 `GameSettingsModel`（新增字段）并落盘。
- `UITextProvider` 改造：支持按当前语言加载文案文件 + 运行时切换通知 UI 刷新。
- 文案覆盖范围：
  - Title 场景：主菜单 4 按钮（开始/继续/设置/退出）
  - Title 场景：9 个 Msgbox（主文本 + 按钮文本）
  - Title 场景：设置页 Tab / 显示模式 / 分辨率（已有，纳入语言文件）
  - Bootstrap 场景：13 个 FlowStep.DisplayName + 「完成」
  - Bootstrap 场景：错误弹窗（TransitionUI）静态按钮「好的」；动态异常文本原样透传
- 文案配置源：Excel 总表（策划维护），构建/运行时导出为各语言 JSON。

### 2.2 本期不包含

- 主菜单箭头符号（←→）：**不切换**，后续改用 image（已确认）。
- Bootstrap「请输入文本」无宿主占位符：**不管**（已确认）。
- 运行时游戏内其他场景（Gameplay 场景 AIPlayer 相关 UI）：**不在本期**。
- Python 侧 / 协议：**无改动**。
- `errmsg`（API 异常消息）内容翻译：**原样透传**，仅前缀走文案表。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家 | 打开设置 → 游戏 Tab → 左右箭头切到 English | 主菜单、各弹窗、进度条步骤名立即变为英文 |
| 玩家 | 切到简体中文 | 全部文案恢复简体中文 |
| 策划 | 维护文案 | 在 Excel 总表编辑 key / 简体中文 / English，导出 JSON 即生效 |

## 4. 功能需求

### 4.1 设置页「游戏」Tab

- 在 `PanelTabs` 中 `TabGameSettings`（场景已建好，Button + TMP「游戏」）加入 `UISetting.mSettingsTabs` 数组（第 3 个 Tab）。
- 点击后显示 `ContentGameSettings`，隐藏其他内容区（走既有 `UISetting` Tab 数据驱动机制）。

### 4.2 语言选择（ContentGameSettings / PanelLanguage）

- `PanelLanguage` 结构（场景已建好）：`TxtTitle`（"语言"）+ `PanelSelect`（`TxtContent` 当前语言名 + `BtnLeft`← + `BtnRight`→）。
- 点左/右箭头循环切换 **简体中文 ↔ English**（仅两个语言，来回切；默认简体中文）。
- `TxtContent` 显示当前语言名（简体中文 或 English）。
- `TxtTitle`（"语言"）、语言名（"简体中文"/"English"）均走文案表。

### 4.3 数据模型（复用 GameSettingsModel）

- `GameSettings` 数据类新增 `language` 字段（如 `0`=简体中文 / `1`=English）。
- `GameSettingsModel` 新增 `BindableProperty<int> Language`。
- `GameSettingsStore.Save/Load` 支持 language 字段；无文件时默认简体中文。
- 语言变更经 Command（`ChangeLanguageCommand` 或复用现有 Command 模式）写入 Model，落盘经 `SaveGameSettingsCommand`。

### 4.4 UITextProvider 语言切换

- 支持按当前语言加载 `strings_{lang}.json`（如 `strings_ChineseSimplified` / `strings_English`）。
- 提供运行时切换语言入口：加载目标语言文件 + 广播刷新事件（供所有 UI 重新拉取文案）。
- 缺 key 回退：找不到 key 时回退简体中文，再找不到回退 key 本身（保证切英文不漏字）。
- 默认语言：简体中文（`ChineseSimplified`）。

### 4.5 文案覆盖清单（详见 `文本盘点.md`）

- **主菜单**：开始 / 继续 / 设置 / 退出
- **9 个 Msgbox**：主文本 + 按钮文本（以场景实际文案为准）
- **设置页 Tab**：模型配置 / 画面 / 游戏（新增 `tab_game_settings`）
- **显示模式**：窗口化 / 无边框 / 全屏（`resolution_format` 分辨率格式为通用数字格式，**不纳入语言表**）
- **FlowStep.DisplayName**：13 条（「Agent」英文保留）
- **完成**、**模型不可用：**（前缀）、错误弹窗「好的」按钮
- **语言相关**：语言（TxtTitle）/ 简体中文 / English

### 4.6 Excel 文案源

- Excel 总表（策划维护）：列 = 序号 / key / 模块 / 简体中文 / English / 备注（格式已在 `solution.md` 3.7 确定）。
- **正式策划配置源**：`GameData/Config/UI文案表.xlsx`（仓库根，`src/` 之外，策划不进工程目录）。样例由 `DevDocs/v0.23.5/gen_ui_excel_sample.py` 生成后迁移。
- 运行 `Tools\export_localization.cmd`（一键，自动用 PythonServer venv / uv sync）或 `python Tools/export_localization.py` 导出为 `strings_ChineseSimplified.json` / `strings_English.json` 到 `Assets/Resources/UI/`。
- 导出工具：命令行脚本 + `.cmd` 一键入口（本期交付）。

## 5. 非功能需求

- 切换语言后**即时生效**（无需重启），所有已打开 UI 立即刷新。
- 语言选择持久化：重启游戏后保持上次选择。
- 简体中文为默认语言，英文文件缺 key 时回退简体中文，不允许出现空白/乱码。
- 所有文件 UTF-8（遵循项目编码基线）。

## 6. 验收标准

- [x] 设置页出现「游戏」Tab，点击显示语言选择（左右箭头切 简体中文/English，默认简体中文）。
- [x] 切到 English 后，Title 主菜单 4 按钮变为英文。
- [x] 切到 English 后，9 个 Msgbox 的主文本/按钮文本变为英文。
- [x] 切到 English 后，设置页 Tab/显示模式/分辨率变为英文。
- [x] Bootstrap 进度条上方各 FlowStep.DisplayName 变为英文；完成态「完成」变英文。
- [x] Bootstrap 出错弹窗「好的」按钮变英文；异常消息 `errmsg` 原样透传，前缀「模型不可用：」随语言切换。
- [x] 语言选择写入 `game_settings.json`，重启后保持。
- [x] 英文文件缺 key 时回退简体中文，不出现空文本。
- [x] Excel 总表导出 JSON 流程可用（策划可维护）。

> 验收状态：**2026-09-04 全部通过**（1–4、6 步自动化核对 + 第 5 步 Unity 运行期用户实测）。

## 7. 待确认问题

- ~~全部已确认（2026-09-03）~~，见 `文本盘点.md` 第六节。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
