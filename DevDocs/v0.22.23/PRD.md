# PRD — v0.22.23 MsgBox 弹窗 Prefab 化

> **状态**：已确认
> **对应需求**：`requirements/MsgBox Prefab需求.md`
> **最后更新**：2026-08-19

---

## 1. 背景与目标

当前多个 Scene 里都存在结构高度雷同的 MsgBox 弹窗，包括但不仅限于：

- `Bootstrap` Scene 的 `MsgboxError`
- `Title` Scene 的 `MsgboxNewGame`、`MsgboxNoApiKey`、`MsgboxQuit`
- `Level0` 等 Scene 的 `MsgboxConfirmExit`、`MsgboxGameOver`

这些 MsgBox 的共同特点：

- 有一个 `WarningTxt`，每个 MsgBox 上显示的文字不同；
- 有 1~2 个按钮，每个按钮上显示的文字、触发的方法不同。

当前它们大多是**各自场景内手工复制的独立节点**（少数是 `PanelConfirmExit.prefab` / `PanelGameOver.prefab` 引用），改外形和素材时需要逐个场景重复修改，成本高且易遗漏。

**目标**：进入 UI 素材阶段前，将 MsgBox 做成**单一可复用 Prefab**，只改一个 MsgBox 的外形和素材，即可同时调整所有 Scene 中的 MsgBox。

## 2. 范围

### 2.1 本期包含

- 将 MsgBox 弹窗抽象为**一个可配置的通用 Prefab**（或一套按用途区分的 Prefab 变体）。
- 用一个**通用 MsgBox 控制脚本**统一管理：提示文字内容、按钮数量（1~2）、按钮文字、按钮点击回调。
- 将现有各场景的 MsgBox 迁移为对该 Prefab 的引用（`Bootstrap`、`Title` 及通过 `UI.prefab` 引用的 Level 场景）。
- 保持每个场景 MsgBox 现有的**文案与触发行为完全不变**（迁移不改行为）。

### 2.2 本期不包含

- 新增任何新的 MsgBox 用途（仅重构现有）。
- 改动 MsgBox 的**视觉设计方案**（外形、素材、布局仅做整理，具体素材替换由 UI 阶段进行）。
- 改动 Python / 协议层（纯 Unity 侧重构）。
- 不处理 `Doc/`、`feature-design/` 中与弹窗无关的其它 UI 重构。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 玩家 | Bootstrap 出错 | 弹出「MsgboxError」，点击 OK 关闭，行为与现状一致 |
| 玩家 | Title 新游戏 | 弹出「MsgboxNewGame」确认，确认/取消行为与现状一致 |
| 玩家 | Title 无 API Key | 弹出「MsgboxNoApiKey」，点击行为与现状一致 |
| 玩家 | Title 退出 | 弹出「MsgboxQuit」确认，确认后退出游戏，行为与现状一致 |
| 玩家 | Level0 等关卡 | 弹出「MsgboxConfirmExit」确认退出到标题 / 「MsgboxGameOver」，行为与现状一致 |
| 开发者 | UI 阶段 | 只需修改一个 MsgBox Prefab 的外形与素材，所有场景同步更新 |

## 4. 功能需求

### 4.1 通用 MsgBox Prefab

- 提供一个可复用的 MsgBox Prefab，结构包含：背景（`Image` 底）+ `WarningTxt`（提示文字）+ **按钮区**（可容纳 1~2 个按钮）。
- Prefab 的**外观（背景、按钮、布局、素材引用）集中定义**，各场景通过引用该 Prefab 使用，不各自复制节点。

### 4.2 通用 MsgBox 控制脚本

- 提供 `MsgBox`（或 `UIMsgBox`）通用控制脚本，支持在场景/代码中配置：
  - 提示文字内容（`WarningTxt`）；
  - 按钮数量（1 或 2）；
  - 每个按钮的显示文字；
  - 每个按钮点击后触发的回调方法（可绑定到场景内其它脚本，或由代码动态赋值）。
- 脚本不感知具体业务，由各场景实例化时注入按钮行为。

### 4.3 现有 MsgBox 迁移

- 将下列场景中的 MsgBox 替换为通用 Prefab 的引用，并在引用实例上配置原有文案与按钮行为：
  - `Bootstrap.unity`：`MsgboxError`（OK 关闭）；
  - `Title.unity`：`MsgboxNewGame`（确认=开始新游戏 / 取消=关闭）、`MsgboxNoApiKey`、`MsgboxQuit`（确认=退出 / 取消=关闭）；
  - `UI.prefab` 内：`MsgboxConfirmExit`（确认=返回标题 / 取消=关闭）、`MsgboxGameOver`（重试等）。
- **迁移后行为与现状保持一致**，不得改变文案与触发结果。

### 4.4 场景引用方式

- 各场景内保留一个「MsgBox 实例节点」，其内部结构改为引用通用 Prefab；文案与按钮回调在各实例上配置。
- 若存在同一场景内多个 MsgBox，各实例共享同一 Prefab 外观，仅覆盖实例参数。

## 5. 非功能需求

- **纯 Unity 侧改动**，不引入新第三方依赖。
- Prefab 层级结构与命名简洁清晰，便于 UI 阶段替换素材。
- 保持现有旧版 UI（`Text (Legacy)` + `Image`，非 TMP）风格，不强制迁移 TMP（除非场景内已是 TMP）。
- 迁移后不得影响 `UITitle` / `UI` 等现有控制脚本对弹窗的显隐调用（引用字段保持可拖拽）。

## 6. 验收标准

- [ ] 存在一个通用 MsgBox Prefab，修改其背景/按钮素材可影响所有引用它的场景。
- [ ] 通用 MsgBox 脚本可配置提示文字、按钮数量（1~2）、按钮文字、按钮回调。
- [ ] `Bootstrap` 的 `MsgboxError` 迁移后行为与现状一致。
- [ ] `Title` 的 `MsgboxNewGame`、`MsgboxNoApiKey`、`MsgboxQuit` 迁移后行为与现状一致。
- [ ] `UI.prefab` 内 `MsgboxConfirmExit`、`MsgboxGameOver` 迁移后行为与现状一致。
- [ ] 各场景运行后能正常弹出/关闭对应 MsgBox，文案与点击结果与迁移前一致。

## 7. 待确认问题

- [ ] **Prefab 形态**：是「一个通用 MsgBox Prefab + 运行时动态配置」还是「按用途建 2~3 个变体 Prefab（单按钮/双按钮）」？（见 solution.md 方案 A/B 对比）
- [ ] **按钮回调绑定方式**：在场景里用 Inspector 拖拽绑定（对现有迁移最平滑），还是用代码动态注册？（见 solution.md 方案对比）
- [ ] **名称约定**：现有 `MsgboxXXX` 节点命名是否保留，仅替换内部结构？
- [ ] **Title 的 `MsgboxQuit`/`MsgboxNoApiKey`** 当前是手工节点还是已为 Prefab 引用？（现状为手工复制节点）

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
