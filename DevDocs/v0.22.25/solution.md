# 技术方案 — v0.22.25 MsgBox 增加 Btn3（支持 1~3 个按钮）

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-19

---

## 1. 方案概述

在 v0.22.23/0.22.24 已定稿的通用 `UIMsgBox` 基础上，把支持的按钮数从 **1~2** 扩展到 **1~3**：

- Prefab 侧：`UIMsgBox.prefab` 新增同级节点 `Btn3`（Button：Image + 子 Text）。
- 脚本侧：`UIMsgBox.cs` 新增 `mBtn3` 字段，并将「自动隐藏未使用按钮」逻辑从「只处理 Btn2」扩展为「按实际配置隐藏 Btn2 / Btn3」。
- 兼容性：所有现有实例不配 `Btn3`，行为与现状一致。

**不改**置顶（`SetAsLastSibling`）、ESC → `Btn1`、`AnyActive` 等既有语义。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | — | 无 |
| Unity | `Assets/Resources/UI/UIMsgBox.prefab` | 修改（新增 `Btn3` 节点 + 布局） |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UIMsgBox.cs` | 修改（新增 `mBtn3` 字段 + 隐藏逻辑扩展） |
| Unity | 各场景 MsgBox 实例 | 无（不配 Btn3 即不受影响） |
| 协议 | `Tools/message.proto` | 无 |

## 3. 详细设计

### 3.1 现状回顾（v0.22.23/0.22.24 的 `UIMsgBox`）

- 字段：`mWarningTxt`（Text）、`mBtn1` / `mBtn2`（Button）。
- `Awake`：单按钮实例（`mBtn2 == null`）时 `transform.Find("Btn2").SetActive(false)` 隐藏第 2 个按钮。
- `OnEnable`：`transform.SetAsLastSibling()` 置顶 + `AnyActive = true`。
- `OnDisable`：`AnyActive = false`。
- `Update`：ESC（`Input.GetButtonDown("Menu")`）→ `mBtn1.onClick.Invoke()`。

### 3.2 Prefab 改动

`UIMsgBox.prefab` 的按钮区新增第三个按钮：

```
UIMsgBox（根，挂 UIMsgBox）
├── WarningTxt
├── Btn1
├── Btn2
└── Btn3        ← 新增（Button：Image + 子 Text，占位文字如「第三个选项」）
```

- `Btn3` 与 `Btn1`/`Btn2` 同级、同结构（Image + 子 Text）。
- 布局：三按钮建议等宽并排（水平排列），在 Prefab 内预设；也可由你在编辑器内手动摆位（见 §7 待确认）。
- **默认状态**：`Btn3` 节点默认 `SetActive(false)`，仅当实例配置了它才显示。

### 3.3 `UIMsgBox.cs` 改动

**新增字段**：

```csharp
[Header("按钮区（单/双按钮实例：未用的按钮留空，Awake 自动隐藏）")]
[SerializeField]
private Button mBtn1;
[SerializeField]
private Button mBtn2;
[SerializeField]
private Button mBtn3;   // 新增
```

**隐藏逻辑扩展（Awake）**——把「只隐藏 Btn2」改为「按配置隐藏未用的按钮」：

```csharp
private void Awake()
{
    if (mWarningTxt == null)
    {
        Debug.LogWarning("[UIMsgBox] 未关联 WarningTxt", this);
    }
    if (mBtn1 == null)
    {
        Debug.LogWarning("[UIMsgBox] 未关联 Btn1", this);
    }

    // 未配置的按钮一律隐藏（支持 1~3 个按钮）
    ApplyButtonVisibility(mBtn2, "Btn2");
    ApplyButtonVisibility(mBtn3, "Btn3");
}

private void ApplyButtonVisibility(Button btn, string nodeName)
{
    if (btn == null)
    {
        Transform node = transform.Find(nodeName);
        if (node != null)
        {
            node.gameObject.SetActive(false);
        }
    }
}
```

**要点**：
- 沿用现有「字段未配置 → 隐藏对应节点」的判定方式，不新增复杂状态。
- `Btn1` 始终必须配置（ESC 默认按钮依赖它），逻辑不变。
- ESC → `Btn1`、置顶、`AnyActive` 均不改。

> 说明：`ApplyButtonVisibility` 用「字段为空」判断隐藏。若某实例在 Inspector 里拖了 `mBtn3` 但节点被手动关闭，脚本不会强制开启（保持实例意愿），与 v0.22.23 行为一致。

### 3.4 兼容性

- 现有所有实例（`MsgboxError` / `MsgboxNewGame` / `MsgboxNoApiKey` / `MsgboxQuit` / `MsgboxConfirmExit` / `MsgboxGameOver` / `MsgboxSaveConfig`）**不配置** `mBtn3`：
  - `Awake` 自动隐藏 `Btn3`；
  - 场景里未使用 Btn3 的 Prefab 实例，若节点本身已隐藏则无感知。
- 不需要修改任何现有场景 / 既有按钮回调。

## 4. 实现步骤

### 4.1 代码侧（Agent 完成）

1. 修改 `UIMsgBox.cs`：新增 `mBtn3` 字段 + `ApplyButtonVisibility` 隐藏逻辑。

### 4.2 Prefab/场景侧（Unity 编辑器内操作，你完成）

2. 编辑 `UIMsgBox.prefab`：
   - 新增 `Btn3`（Button：Image + 子 Text），与 `Btn1`/`Btn2` 同级；
   - 排布三按钮（等宽并排或手动摆位）；
   - `Btn3` 默认 `SetActive(false)`。
3. 后续如需某个 MsgBox 用三按钮：在实例上拖入 Prefab 实例 → 拖 `mBtn3` 字段 → 配按钮文字与 `onClick` 回调 → 激活 `Btn3` 节点。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 新增 `Btn3` 后旧实例意外显示第 3 个按钮 | `Btn3` 默认隐藏 + `Awake` 按「未配置则隐藏」处理，旧实例不配即无影响 |
| 手改 prefab YAML 破坏结构 | 在 Unity 编辑器内操作，不手改 YAML |
| `transform.Find("Btn3")` 找不到节点 | 脚本防御（`node != null` 判断），仅日志无异常 |
| 回退方案 | 还原 `UIMsgBox.cs` 与 prefab（删除 Btn3）即可回到 v0.22.24 状态 |

## 6. 测试建议

需在 Unity 编辑器内人工验证（纯 Unity 侧，不依赖 Python/协议）：

| # | 步骤 | 期望 |
|---|------|------|
| 1 | 打开任一现有单按钮 MsgBox（`MsgboxError`） | 不显示 `Btn2`、`Btn3`，行为与现状一致 |
| 2 | 打开任一现有双按钮 MsgBox（`MsgboxNewGame`） | 不显示 `Btn3`，确认/取消行为与现状一致 |
| 3 | 新建/配置一个三按钮 MsgBox 实例 | 三按钮可见、各自回调生效 |
| 4 | 三按钮弹窗按 ESC | 触发 `Btn1`（默认按钮），与 v0.22.24 一致 |
| 5 | 三按钮弹窗显示时 | 置顶于所有 UI 之上（`SetAsLastSibling`） |

## 7. 待确认问题（已确认，2026-08-19）

- [x] **Btn3 布局**：三按钮在 Prefab 内等宽并排还是由你手动摆位？—— **已确认：由用户在 Unity 编辑器内手动摆位**（脚本不涉及布局）。
- [x] **隐藏方式**：沿用「`transform.Find("BtnX")` 按名字隐藏」是否可接受？—— **已确认：沿用**。

---

## 8. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-19 | 用户确认方案：Btn3 布局由用户在 Unity 编辑器内手动摆位；隐藏方式沿用 `transform.Find("BtnX")` 按名字隐藏。更新 PRD/solution 状态为「已确认」。修改 `UIMsgBox.cs`：新增 `mBtn3` 字段；将「只隐藏 Btn2」重构为 `ApplyButtonVisibility(mBtn2/Btn3, "Btn2"/"Btn3")`，支持 1~3 个按钮；ESC→Btn1、置顶、AnyActive 语义不变。Prefab 侧（新增 Btn3 节点 + 手动摆位）由用户完成。 |
| 2026-08-19 | **验收通过**：用户完成 `UIMsgBox.prefab` 新增 `Btn3` 节点并手动摆位，验证单/双按钮实例不受影响、三按钮实例可用。状态「已确认」→「已实现」。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
