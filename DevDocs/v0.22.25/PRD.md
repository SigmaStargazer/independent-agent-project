# PRD — v0.22.25 MsgBox 增加 Btn3（支持 1~3 个按钮）

> **状态**：已确认
> **对应需求**：`requirements/Btn3需求.md`
> **最后更新**：2026-08-19

---

## 1. 背景与目标

v0.22.23 已完成 MsgBox 弹窗 Prefab 化，通用 `UIMsgBox`（Prefab + `UIMsgBox.cs`）当前支持 **1~2 个按钮**（`Btn1` / `Btn2`，单按钮实例自动隐藏第 2 个按钮）。

本版本需求：在通用 MsgBox 上**多加一个 `Btn3`**，使每个 MsgBox 可配置 **1~3 个按钮**。这样后续业务可在同一个弹窗里提供「确认 / 取消 / 第三个选项（如「稍后再说」「了解更多」等）」三个操作。

## 2. 范围

### 2.1 本期包含

- 通用 `UIMsgBox` Prefab 模板新增第 3 个按钮节点 `Btn3`。
- `UIMsgBox.cs` 新增 `mBtn3` 引用字段，并支持「单/双/三按钮」三种形态的自动隐藏逻辑。
- 各场景**现有** MsgBox 实例不受影响（未使用 `Btn3` 的实例保持原样）。

### 2.2 本期不包含

- **不指定** `Btn3` 的特定业务用途 / 文案（本版本仅扩展通用能力；具体弹窗用 Btn3 属后续需求）。
- 不改变现有 `Btn1` / `Btn2` 的行为、文案、回调。
- 不改 Python / 协议层。
- 不迁移 Input System。
- 不改变 `UIMsgBox` 的置顶（`SetAsLastSibling`）与 ESC → `Btn1` 语义（ESC 仍只触发默认按钮 `Btn1`，与 v0.22.24 一致）。

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|------|------|----------|
| 开发者 | 配置一个新的三按钮 MsgBox | 可给 `Btn1`/`Btn2`/`Btn3` 分别配文字与回调 |
| 玩家 | 三按钮弹窗显示 | 三个按钮均可见、可点击，行为正常 |
| 玩家 | 原有单/双按钮弹窗 | 与 v0.22.23 行为完全一致（不出现多余按钮） |

## 4. 功能需求

### 4.1 Prefab 模板新增 Btn3

- `UIMsgBox.prefab` 在现有 `Btn1`、`Btn2` 之后新增同级节点 `Btn3`（Button：Image + 子 Text）。
- 新按钮默认**隐藏**（或由脚本按「是否配置」自动显隐），保证未使用 Btn3 的旧实例不出现多余按钮。

### 4.2 `UIMsgBox.cs` 支持 1~3 按钮

- 新增 `[SerializeField] private Button mBtn3;`。
- 隐藏逻辑扩展为「按实际配置决定隐藏哪几个」：
  - 只配 `Btn1` → 隐藏 `Btn2`、`Btn3`；
  - 配 `Btn1`+`Btn2` → 隐藏 `Btn3`；
  - 三个都配 → 全部显示。
- 与现有实现一致：隐藏优先通过「字段未配置」判定；若字段已配置但场景未拖拽对应节点，仍做防御处理。

### 4.3 兼容性

- 现有所有实例（`Bootstrap` 的 `MsgboxError`、`Title` 的 `MsgboxNewGame`/`MsgboxNoApiKey`/`MsgboxQuit`、`MsgboxConfirmExit`、`MsgboxGameOver`、`MsgboxSaveConfig`）不配 `Btn3`，行为与现状一致。

## 5. 非功能需求

- 纯 Unity 侧改动，不新增第三方依赖。
- 脚本改动保持「仅表现层」边界：不感知业务回调，回调仍由 Inspector 拖拽绑定。
- 布局：三按钮并排时需在 Prefab 内预设合理的水平排布（或由你在编辑器内调整）。

## 6. 验收标准

- [ ] `UIMsgBox` Prefab 存在 `Btn3` 节点。
- [ ] `UIMsgBox.cs` 支持最多 3 个按钮；未使用的按钮自动隐藏。
- [ ] 单按钮实例（如 `MsgboxError`、`MsgboxNoApiKey`）不显示 `Btn2`、`Btn3`。
- [ ] 双按钮实例（如 `MsgboxNewGame`、`MsgboxQuit`）不显示 `Btn3`。
- [ ] 三按钮实例三个按钮均可点击、回调各自生效。
- [ ] ESC → `Btn1`（默认按钮）语义保持与 v0.22.24 一致。
- [ ] 置顶（`SetAsLastSibling`）行为不受影响。

## 7. 待确认问题（已确认，2026-08-19）

- [x] **Btn3 布局方式**：三个按钮在 Prefab 内如何排布？—— **已确认：由用户在 Unity 编辑器内手动摆位**（脚本不预设布局）。
- [x] **隐藏方式**：继续沿用「`transform.Find("BtnX")` 按名字隐藏」的现有模式？—— **已确认：沿用**。
- [x] 是否需要为 Btn3 配置独立的默认按钮语义（如 ESC 触发不同按钮）？—— **已确认：不需要**，ESC 仍触发 `Btn1`。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成，确认前请勿直接据此改代码。*
