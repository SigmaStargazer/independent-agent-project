# 技术方案 — v0.22.24 Title 场景 PanelConfig 配置子面板导航

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-19

---

## 1. 方案概述

在 v0.22.22 已定稿的「`UITitle` 总控」架构基础上，为 `PanelConfig`（设置总览）与其 4 个配置子面板补齐导航逻辑：

1. **4 个按钮 → 打开对应子面板、关闭 `PanelConfig`**：`UITitle` 新增 4 个公开方法（无参，Inspector 绑定最平滑），场景侧把按钮 `onClick` 拖拽绑定到这些方法。
2. **子面板内 ESC → 弹 `MsgboxSaveConfig`**：扩展 `UITitle.Update` 的 ESC 分发，增加「配置子面板激活」分支。
3. **返回方法**：复用现有 `ShowConfig()`（补齐关闭 4 个子面板），供 `MsgboxSaveConfig` 确认后调用。

**架构决策（2026-08-19 用户确认）**：
- **`MsgboxSaveConfig` 统一放 UI 根节点下（与其他 Msgbox 平级），不做 `PanelConfig` 子节点**——这是**功能可行性**要求：需求流程是「子面板内（`PanelConfig` 已关闭）按 ESC → 弹 `MsgboxSaveConfig`」，若它是 `PanelConfig` 子节点，`PanelConfig` 失活时子物体无法显示，需求 2 无法实现。故必须平级挂 UI 下。
- **任何弹窗打开时按 ESC → 触发 `Btn1`**：在通用 `UIMsgBox.cs`（v0.22.23）里统一处理——`UIMsgBox.Update` 轮询 ESC，触发 `mBtn1.onClick.Invoke()`；并用静态 `AnyActive` 标记让 `UITitle` 在弹窗打开时不处理 ESC。这样**所有** MsgBox 实例自动获得「ESC = 默认按钮（通常为确认）」语义，无需每个弹窗/每个场景单独绑定。

纯 Unity 侧改动，不改协议、不改 Python、不新增依赖。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | — | 无 |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UITitle.cs` | 修改（新增 4 子面板引用、4 个打开方法、ESC 分发扩展、返回方法） |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UIMsgBox.cs` | 修改（新增「弹窗激活时 ESC → 触发 Btn1」通用逻辑） |
| Unity | `Assets/Scenes/Title.unity` | 场景侧（你操作）：4 按钮 `onClick` 绑定、`UITitle` 新字段拖拽、`MsgboxSaveConfig` 按钮回调、确认其位于 UI 根节点下 |
| 协议 | `Tools/message.proto` | 无 |

## 3. 详细设计

### 3.1 现状盘点

- `UITitle` 当前持有 `mPressAnyButtonPanel` / `mMainMenuPanel` / `mConfigPanel` 三面板引用，`ShowPressAnyButton / ShowMainMenu / ShowConfig` 三方法互斥切换。
- `Update` 中 ESC 分发：`mPressAnyButtonPanel` → 任意键；`mConfigPanel` → ESC 回主菜单；`mMainMenuPanel` → ESC 回启动画面。
- `MsgboxSaveConfig` 为 v0.22.23 通用 `UIMsgBox` 的实例（挂 `UIMsgBox.cs`，`OnEnable` 时 `SetAsLastSibling` 置顶）。
- 4 个配置子面板、4 个按钮均已存在于 `Title.unity`（UI 根节点下），但无任何脚本引用/逻辑。

### 3.2 `UITitle.cs` 字段扩展

在现有字段基础上新增：

```csharp
[Header("配置子面板")]
[SerializeField] private GameObject mLLMAgentPanel;    // PanelLLMAgent
[SerializeField] private GameObject mLLMMemoryPanel;   // PanelLLMMemory
[SerializeField] private GameObject mEmbeddingPanel;   // PanelEmbedding
[SerializeField] private GameObject mRerankerPanel;    // PanelReranker

[Header("保存配置确认弹窗")]
[SerializeField] private GameObject mSaveConfigMsgBox; // MsgboxSaveConfig
```

> 说明：4 个按钮（`BtnLLMAgent` 等）**无需在 `UITitle` 持有引用**——按钮点击通过场景侧 `onClick` 持久化调用绑定到 `UITitle` 的公开方法即可（与现有 `BtnConfig`/`BtnQuit` 绑定 `OnClickConfig`/`OnClickQuit` 一致）。场景侧需把这些按钮的 `onClick` 绑到对应公开方法。

### 3.3 打开对应配置子面板（需求 1）

新增 4 个公开方法，互斥切换 `PanelConfig` 与对应子面板：

```csharp
public void ShowLLMAgentConfig()   { SetSubPanelActive(mLLMAgentPanel); }
public void ShowLLMMemoryConfig()  { SetSubPanelActive(mLLMMemoryPanel); }
public void ShowEmbeddingConfig()  { SetSubPanelActive(mEmbeddingPanel); }
public void ShowRerankerConfig()   { SetSubPanelActive(mRerankerPanel); }

// 打开指定子面板：关闭 PanelConfig 与其他 3 个子面板，只留目标子面板
private void SetSubPanelActive(GameObject subPanel)
{
    SetPanelActive(mLLMAgentPanel, subPanel == mLLMAgentPanel);
    SetPanelActive(mLLMMemoryPanel, subPanel == mLLMMemoryPanel);
    SetPanelActive(mEmbeddingPanel, subPanel == mEmbeddingPanel);
    SetPanelActive(mRerankerPanel, subPanel == mRerankerPanel);
    SetPanelActive(mConfigPanel, false);   // 关闭设置总览
    LockInput();
}
```

> 备选：也可用「一个带 int/枚举 参数的方法 + 4 个按钮绑定时指定参数」，但 Unity Button `onClick` 持久化调用绑定**带参方法**需要 `Dynamic Bool/Int` 绑定，Inspector 操作更繁琐。**推荐 4 个独立无参方法**，与现有 `OnClickConfig` 等风格一致，绑定最平滑。

### 3.4 子面板内 ESC 弹保存确认（需求 2）

扩展 `Update()` 的 ESC 分发，新增「任一配置子面板激活」分支（优先级置于 `PanelConfig` 之前）：

```csharp
void Update()
{
    if (InLockWindow) return;

    if (mPressAnyButtonPanel != null && mPressAnyButtonPanel.activeSelf)
    {
        if (Input.anyKeyDown) { ShowMainMenu(); }
    }
    else if (IsSubPanelActive())            // 任一配置子面板激活
    {
        // ESC → 弹出保存确认（MsgboxSaveConfig）
        if (Input.GetButtonDown("Menu"))
        {
            ShowSaveConfigMsgBox();
        }
    }
    else if (mConfigPanel != null && mConfigPanel.activeSelf)
    {
        if (Input.GetButtonDown("Menu")) { ShowMainMenu(); }
    }
    else if (mMainMenuPanel != null && mMainMenuPanel.activeSelf)
    {
        if (Input.GetButtonDown("Menu")) { ShowPressAnyButton(); }
    }
}

private bool IsSubPanelActive()
{
    return (mLLMAgentPanel != null && mLLMAgentPanel.activeSelf)
        || (mLLMMemoryPanel != null && mLLMMemoryPanel.activeSelf)
        || (mEmbeddingPanel != null && mEmbeddingPanel.activeSelf)
        || (mRerankerPanel != null && mRerankerPanel.activeSelf);
}

private void ShowSaveConfigMsgBox()
{
    if (mSaveConfigMsgBox != null)
    {
        mSaveConfigMsgBox.SetActive(true);   // UIMsgBox.OnEnable 自动置顶
        LockInput();
    }
}
```

> **关键顺序**：子面板分支必须放在 `mConfigPanel` 分支**之前**。因为子面板打开时 `PanelConfig` 已关闭（`activeSelf=false`），不会误入 `mConfigPanel` 分支；但为保证语义清晰、避免未来 `PanelConfig` 也保持激活时的歧义，仍显式把子面板分支放最前。

### 3.5 返回 `PanelConfig`（需求 3）

复用现有 `ShowConfig()`——它当前逻辑为「关闭 `mPressAnyButtonPanel`/`mMainMenuPanel`、打开 `mConfigPanel`」。需补充：**同时关闭 4 个子面板**（避免从子面板返回时子面板残留）：

```csharp
public void ShowConfig()
{
    SetPanelActive(mPressAnyButtonPanel, false);
    SetPanelActive(mMainMenuPanel, false);
    SetPanelActive(mLLMAgentPanel, false);
    SetPanelActive(mLLMMemoryPanel, false);
    SetPanelActive(mEmbeddingPanel, false);
    SetPanelActive(mRerankerPanel, false);
    SetPanelActive(mConfigPanel, true);
    LockInput();
}
```

> `ShowConfig()` 即为需求 3 的「关闭 4 个 Panel 并打开 PanelConfig」方法——语义完全一致，直接复用并补齐关闭子面板即可，无需另起新方法。

### 3.6 `MsgboxSaveConfig` 接线（场景侧，你操作）

`MsgboxSaveConfig` 复用 v0.22.23 通用 `UIMsgBox`，双按钮：

> **MsgboxSaveConfig 位于 UI 根节点下（用户确认，非 PanelConfig 子节点）**，故 `ShowConfig()` 打开 `PanelConfig` 时**不会**连带关闭弹窗。确认保存按钮需绑定「关闭弹窗 + 返回 PanelConfig」的组合。由于 Button `onClick` 持久化调用只能绑一个方法，推荐在 `UITitle` 新增一个聚合方法 `OnConfirmSaveConfig()`：

```csharp
// 确认保存：关闭保存确认弹窗 → 返回 PanelConfig（实际保存逻辑后续版本再做）
public void OnConfirmSaveConfig()
{
    if (mSaveConfigMsgBox != null)
        mSaveConfigMsgBox.SetActive(false);
    ShowConfig();
}
```

| 按钮 | onClick 绑定（场景里拖拽到 `UITitle`） | 行为 |
|------|----------------------------------------|------|
| 确认保存 | `UITitle.OnConfirmSaveConfig`（新增） | 关闭弹窗 + 关闭 4 子面板 + 打开 `PanelConfig` |
| 取消 | `UITitle.OnCancelSaveConfig`（新增） | 仅关闭弹窗，停留当前子面板 |

### 3.7 弹窗打开时 ESC → 触发 Btn1（通用逻辑，改 `UIMsgBox.cs`）

**用户确认（2026-08-19）**：任何弹窗打开时，按 ESC 都应触发 `Btn1`（默认按钮，通常为「确认」）。

**实现**：在通用 `UIMsgBox.cs`（v0.22.23 已存在）中统一处理，所有 MsgBox 实例自动生效，无需每个弹窗单独绑定：

```csharp
public class UIMsgBox : MonoBehaviour
{
    [SerializeField] private Text mWarningTxt;
    [SerializeField] private Button mBtn1;   // 由 GameObject 改为 Button 类型，以触发 onClick
    [SerializeField] private GameObject mBtn2;

    void Awake() { /* 原有逻辑保留 */ }

    void OnEnable()
    {
        transform.SetAsLastSibling();   // 原有：置顶

        // 新增：ESC 回退 → 触发 Btn1（默认按钮）
        // 说明：UIMsgBox 用 Update 轮询，而非 Input System 事件，避免与 UITitle 的 Update ESC 分发冲突
    }

    void OnDisable()
    {
        // 新增：注销，避免弹窗关闭后仍响应 ESC
    }

    void Update()
    {
        // 新增：仅在自身激活时响应 ESC，触发 mBtn1.onClick.Invoke()
        if (gameObject.activeSelf && Input.GetButtonDown("Menu"))
        {
            if (mBtn1 != null)
            {
                mBtn1.onClick.Invoke();
            }
        }
    }
}
```

**关键点**：

1. **`mBtn1` 类型从 `GameObject` 改为 `Button`**：需要触发 `onClick`，故直接持有 `UnityEngine.UI.Button` 引用（场景里拖拽时 Unity 会自动按组件类型过滤，选中按钮的 Button 组件即可）。
2. **用 `Update` 轮询 `Input.GetButtonDown("Menu")`**：与项目现有输入栈一致（旧版 Input Manager），不引入 Input System。
3. **弹窗激活时才响应**：`gameObject.activeSelf` 保证失活弹窗不处理；`OnDisable` 无需额外注销（Update 天然停止），但保留作清晰表达。
4. **ESC 被「任一激活的 UIMsgBox」捕获**：当 `MsgboxSaveConfig` 弹出时，它的 `Update` 会响应 ESC 触发 Btn1（确认保存）。此时 `UITitle.Update` 的子面板分支**仍会执行**（底层子面板未关闭）——需要在 `UITitle.Update` 中屏蔽「有弹窗打开时不处理子面板 ESC」，避免同一 ESC 既弹窗又触发保存。见 §3.8。

### 3.8 `UITitle.Update` 与弹窗 ESC 的协调

当 `MsgboxSaveConfig` 激活时，`UITitle.Update` 的子面板分支不应再响应 ESC（ESC 已由弹窗的 `UIMsgBox.Update` 接管，触发 Btn1）。

**方案**：`UITitle` 判断「当前是否有激活的 UIMsgBox 弹窗」。因弹窗统一挂 `UIMsgBox`，可用一个**静态属性**让 `UITitle` 查询：

```csharp
// UIMsgBox.cs 内
public static bool AnyActive { get; private set; }

void OnEnable()  { transform.SetAsLastSibling(); AnyActive = true; }
void OnDisable() { AnyActive = false; }
```

```csharp
// UITitle.Update 开头，InLockWindow 判断之后
if (UIMsgBox.AnyActive)
{
    return;   // 有弹窗打开时，UITitle 不处理任何 ESC/任意键（弹窗自行接管）
}
```

> 若同时有多个弹窗叠加，`OnDisable` 直接置 false 会有竞争问题（关一个弹窗但另一个还开着）。但本项目弹窗互斥（同一时刻至多一个），该简单实现足够。若未来需要多弹窗叠加，可改为引用计数。

**优先级总结**（从高到低）：
1. `UIMsgBox.AnyActive`（有弹窗）→ UITitle 不处理，弹窗的 `UIMsgBox.Update` 接管 ESC → 触发 Btn1；
2. 配置子面板激活 → ESC 弹 `MsgboxSaveConfig`；
3. `PanelConfig` 激活 → ESC 回主菜单；
4. 主菜单激活 → ESC 回启动画面；
5. 启动画面激活 → 任意键进主菜单。

## 4. 实现步骤

### 4.1 代码侧（Agent 完成）

1. `UITitle.cs` 新增字段：`mLLMAgentPanel` / `mLLMMemoryPanel` / `mEmbeddingPanel` / `mRerankerPanel` / `mSaveConfigMsgBox`。
2. 新增方法：`ShowLLMAgentConfig` / `ShowLLMMemoryConfig` / `ShowEmbeddingConfig` / `ShowRerankerConfig` / `SetSubPanelActive` / `ShowSaveConfigMsgBox` / `IsSubPanelActive` / `OnConfirmSaveConfig` / `OnCancelSaveConfig`。
3. 修改 `Update()`：ESC 分发新增「子面板 → 弹保存确认」分支；开头加 `UIMsgBox.AnyActive` 屏蔽。
4. 修改 `ShowConfig()`：补齐关闭 4 个子面板。
5. `UIMsgBox.cs`：`mBtn1` 类型改为 `Button`；新增 `OnEnable`/`OnDisable` 维护 `AnyActive` 静态标记；新增 `Update` 轮询 ESC → 触发 `mBtn1.onClick.Invoke()`。

### 4.2 场景侧（Unity 编辑器内操作，你完成）

5. 确认 `MsgboxSaveConfig` 位于 **UI 根节点下**（与其他 Msgbox 平级，非 `PanelConfig` 子节点）。
6. `UITitle`（UI 根节点）拖拽关联新字段：
   - `mLLMAgentPanel` → `PanelLLMAgent`
   - `mLLMMemoryPanel` → `PanelLLMMemory`
   - `mEmbeddingPanel` → `PanelEmbedding`
   - `mRerankerPanel` → `PanelReranker`
   - `mSaveConfigMsgBox` → `MsgboxSaveConfig`
7. 4 个按钮 `onClick` 绑定：
   - `BtnLLMAgent.onClick` → `UITitle.ShowLLMAgentConfig`
   - `BtnLLMMemory.onClick` → `UITitle.ShowLLMMemoryConfig`
   - `BtnEmbedding.onClick` → `UITitle.ShowEmbeddingConfig`
   - `BtnReranker.onClick` → `UITitle.ShowRerankerConfig`
8. `MsgboxSaveConfig` 按钮 `onClick` 绑定：
   - 确认保存（Btn1）→ `UITitle.OnConfirmSaveConfig`
   - 取消（Btn2）→ `UITitle.OnCancelSaveConfig`

> 场景 YAML 手改极易出错（fileID/guid），**优先在 Unity 编辑器内操作**。代码侧（`UITitle.cs` / `UIMsgBox.cs`）由 Agent 直接修改。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 子面板 ESC 分支与 `PanelConfig` 分支顺序错误 | 子面板分支显式置于最前；`ShowConfig` 同时关闭子面板，保证状态互斥 |
| 从子面板返回后 `PanelConfig` 未显示或子面板残留 | `ShowConfig()` 补齐关闭 4 子面板；用 §6 测试逐条验证 |
| 弹窗打开时 UITitle 与 UIMsgBox 都响应 ESC（双重触发） | `UIMsgBox.AnyActive` 静态标记；弹窗打开时 `UITitle.Update` 直接 return，ESC 由弹窗接管 |
| 多弹窗叠加时 `AnyActive` 误置 false | 本项目弹窗互斥（同一时刻至多一个），简单布尔足够；若未来多弹窗叠加再改引用计数 |
| `mBtn1` 类型从 `GameObject` 改 `Button` 导致场景引用失效 | 需要用户在场景里把 `UIMsgBox` 的 `mBtn1` 重新拖拽为 Button 组件（类型变化后旧引用需重挂）——**注意**：这会**影响所有场景**已有的 UIMsgBox 实例（Bootstrap/Title/UI.prefab），需一并重新关联 `mBtn1` |
| `mBtn1` 类型改动影响面大 | 备选：保留 `mBtn1` 为 `GameObject`，用 `GetComponent<Button>()` 获取组件——**无需改场景引用**，影响面最小。见 §7 待确认 |
| 按钮 `onClick` 绑定遗漏 | 场景侧按 §4.2 逐项拖拽；用 §6 测试逐条验证 |
| 回退方案 | 还原 `UITitle.cs` / `UIMsgBox.cs`（git 版本）；场景侧移除多余绑定即可回到现状 |

## 6. 测试建议

需在 Unity 编辑器内人工验证（纯 Unity 侧，不依赖 Python/协议）：

| # | 步骤 | 期望 |
|---|------|------|
| 1 | 主菜单点「设置」 | 打开 `PanelConfig`，显示 4 个配置按钮 |
| 2 | 点 `BtnLLMAgent` | 关闭 `PanelConfig`，打开 `PanelLLMAgent` |
| 3 | 点 `BtnLLMMemory` / `BtnEmbedding` / `BtnReranker` | 分别打开对应子面板，关闭 `PanelConfig` |
| 4 | 在 `PanelLLMAgent` 按 ESC | 弹出 `MsgboxSaveConfig` |
| 5 | 其余 3 个子面板按 ESC | 同样弹出 `MsgboxSaveConfig` |
| 6 | `MsgboxSaveConfig` 点「确认保存」 | 关闭 4 子面板、打开 `PanelConfig`、弹窗关闭 |
| 7 | `MsgboxSaveConfig` 点「取消」 | 弹窗关闭，停留原子面板 |
| 8 | `MsgboxSaveConfig` 打开时按 ESC | 等价于点 Btn1（确认保存）——关闭弹窗并返回 `PanelConfig`（不双重触发） |
| 9 | 其它任意 Msgbox（如 `MsgboxQuit`/`MsgboxNoApiKey`）打开时按 ESC | 等价于点各自 Btn1，行为正确 |
| 10 | 设置 → 子面板 → 返回设置 → 返回主菜单，反复 | 状态稳定无抖动、无残留面板 |

## 7. 待确认问题（需你确认后开发）

- [x] **`MsgboxSaveConfig` 是否 `PanelConfig` 的子节点？**—— **已确认：不是**。统一放 UI 根节点下（与其他 Msgbox 平级）。这是功能可行性要求（`PanelConfig` 失活时子物体无法显示，需求 2 无法实现）。确认保存因此需用聚合方法 `OnConfirmSaveConfig()`（显式关闭弹窗 + `ShowConfig()`）。
- [x] **按钮绑定方式**—— **已确认：4 个独立无参方法**（`ShowLLMAgentConfig` 等，Inspector 绑定最平滑）。
- [x] **弹窗打开时 ESC 的严格性**—— **已确认：任何弹窗打开时按 ESC = 触发 Btn1（通用逻辑，改 `UIMsgBox.cs`）**。用 `UIMsgBox.AnyActive` 静态标记让 `UITitle` 在弹窗打开时不处理 ESC，由弹窗接管。
- [x] **`MsgboxSaveConfig` 确认保存逻辑**—— **已确认：本期只做「返回 `PanelConfig`」，实际配置保存逻辑留待后续版本**。
- [x] **`UIMsgBox.mBtn1` 的引用方式**—— **已确认：方案 A（字段类型改为 `Button`）**。`mBtn1` 从 `GameObject` 改为 `UnityEngine.UI.Button`，直接持有按钮组件以触发 `onClick`。**注意：需要在场景里给所有已有 UIMsgBox 实例重新拖拽 `mBtn1`**（Bootstrap 的 `MsgboxError`、Title 的 `MsgboxNewGame`/`MsgboxNoApiKey`/`MsgboxQuit`/`MsgboxSaveConfig`、UI.prefab 内实例），拖拽时选按钮上的 Button 组件即可。

---

## 8. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-19 | 创建 PRD / solution（待确认）。 | 
| 2026-08-19 | 用户确认 4 项：MsgboxSaveConfig 统一放 UI 下（非 PanelConfig 子节点）；按钮用无参方法；任何弹窗 ESC → 触发 Btn1（改 UIMsgBox 通用逻辑）；保存逻辑后续再做。更新 solution：§1 架构决策、§3.6 聚合方法 OnConfirmSaveConfig、§3.7 UIMsgBox ESC→Btn1、§3.8 AnyActive 协调、§4/§5/§6。 |
| 2026-08-19 | 用户确认 `UIMsgBox.mBtn1` 引用方式：**方案 A（字段类型改为 `Button`）**。场景侧需给所有已有 UIMsgBox 实例重新拖拽 `mBtn1` 为 Button 组件。全部待确认问题已确认，进入开发。 |
| 2026-08-19 | **代码侧完成**：`UITitle.cs` 新增 5 字段（4 子面板 + MsgboxSaveConfig）、4 个 Show 方法、`OnConfirmSaveConfig`/`OnCancelSaveConfig`、`SetSubPanelActive`/`IsSubPanelActive`/`ShowSaveConfigMsgBox`，`Update` 加入 `UIMsgBox.AnyActive` 屏蔽 + 子面板 ESC 分支，`ShowConfig`/`ShowPressAnyButton`/`ShowMainMenu` 补齐关闭 4 子面板，`Awake` 隐藏 MsgboxSaveConfig。`UIMsgBox.cs`：`mBtn1` 改 `Button`、新增 `AnyActive` 静态标记、`Update` 轮询 ESC 触发 `mBtn1.onClick.Invoke()`。lint 通过。场景侧待配置（§4.2）。 |
| 2026-08-19 | **验收通过**：用户完成场景侧配置（UITitle 字段拖拽、4 按钮 onClick 绑定、MsgboxSaveConfig 按钮回调、已有 UIMsgBox 实例重挂 mBtn1），确认子面板导航、ESC 弹保存确认、弹窗 ESC 触发 Btn1、返回设置全流程正常。状态「已确认」→「已实现」。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
