# 技术方案 — v0.22.23 MsgBox 弹窗 Prefab 化

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-19

---

## 1. 方案概述

将分散在各场景、手工复制的 MsgBox 弹窗重构为**「一个通用 MsgBox Prefab（模板）+ 通用控制脚本 `UIMsgBox.cs` + 各场景实例引用」**的结构。通用 Prefab 集中定义外观（背景 Image、WarningTxt、按钮布局、素材引用）；每个场景保留一个实例节点，仅通过 Inspector 配置**文案、按钮数量、按钮文字、按钮点击回调**。改外形与素材时只改 Prefab，所有场景同步生效。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | — | 无 |
| Unity | `Assets/Resources/UI/UIMsgBox.prefab`（新） | 新增（通用 MsgBox 模板） |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UIMsgBox.cs`（新） | 新增（通用控制脚本） |
| Unity | `Assets/Scenes/Bootstrap.unity` | 修改（MsgboxError → Prefab 实例） |
| Unity | `Assets/Scenes/Title.unity` | 修改（MsgboxNewGame / MsgboxNoApiKey / MsgboxQuit → Prefab 实例） |
| Unity | `Assets/Resources/UI/UI.prefab` | 修改（MsgboxConfirmExit / MsgboxGameOver → Prefab 实例） |
| Unity | `Assets/Resources/UI/PanelConfirmExit.prefab`、`PanelGameOver.prefab` | 视方案决定（保留为独立 Prefab 或并入通用模板） |
| 协议 | `Tools/message.proto` | 无 |

## 3. 详细设计

### 3.1 现状盘点（已调研）

**MsgBox 节点分布与按钮绑定**：

| 场景 | MsgBox | WarningTxt 文案（当前） | 按钮 | 按钮回调（当前） |
|------|--------|------------------------|------|-----------------|
| `Bootstrap.unity` | `MsgboxError` | 手工节点 | `BtnOK` | `GameObject.SetActive`（关闭自身） |
| `Title.unity` | `MsgboxNewGame` | 「注意！\n将会覆盖已有存档」 | `BtnOK`/`BtnCancel` 等 | `ShootingEditor2D.UITitle.OnClickNewGame` 等 |
| `Title.unity` | `MsgboxNoApiKey` | 手工节点 | 单按钮 | `UITitle` 相关方法 |
| `Title.unity` | `MsgboxQuit` | 手工节点 | 双按钮 | `IndependentAgentProject.UITitle.OnClickQuit` 等 |
| `UI.prefab` | `MsgboxConfirmExit` | 引用 `PanelConfirmExit.prefab` | `BtnRetry` / `BtnReturnToTitle` | `UI.OnClickRetry` / `UI.OnClickConfirmReturnToTitle` |
| `UI.prefab` | `MsgboxGameOver` | 引用 `PanelGameOver.prefab` | `BtnRetry` 等 | `UI.OnClickRetry` 等 |

**共性结构**：`MsgBox 根节点（Image 底）+ WarningTxt + 1~2 个按钮（每按钮 = Image + 子 Text）`。

**关键事实**：
- `Bootstrap` 的 `MsgboxError` 与 `Title` 的 `MsgboxNewGame/NoApiKey/Quit` 均为**场景内手工复制节点**（非 Prefab 引用），改素材需逐个改。
- `UI.prefab`（`guid: b28e2ac3...`，被 `Level0` 等场景引用）内的 `MsgboxConfirmExit`/`MsgboxGameOver` 已引用独立 Prefab `PanelConfirmExit.prefab`（`guid: 49b36362...`）与 `PanelGameOver.prefab`（`guid: 7de20b81...`）。
- 弹窗显隐由 `UITitle`（Title）与 `UI`（关卡）控制，二者以 `GameObject` 引用字段持有弹窗节点，因此**实例节点的字段名 / 引用关系不能破坏**。

### 3.2 方案对比与选型

#### 3.2.1 Prefab 形态

| 方案 | 说明 | 优缺点 | 结论 |
|------|------|--------|------|
| **A. 单个通用 MsgBox Prefab + 每实例配置** | 一个模板含 WarningTxt + 2 个按钮位；单按钮时隐藏第 2 个。各场景实例只配文案/按钮文字/回调 | 模板最少、改动外形一次全生效；但需脚本负责「隐藏多余按钮」 | **推荐采用**（最贴合「只改一个 MsgBox 就能调所有」诉求） |
| B. 按用途多个变体 Prefab | 单按钮版 / 双按钮版各建一个 | 无隐藏逻辑、结构更直白；但有两个模板，改外形要改两处 | 备选（若更在意「无隐藏逻辑」可选此） |
| C. 运行时由代码动态创建 | 一个 Prefab，业务代码 `Instantiate` + 赋值 | 最灵活；但需改所有触发点代码，改动面大 | 不采用（迁移工作量大、破坏现有 Inspector 绑定） |

#### 3.2.2 按钮回调绑定方式

| 方案 | 说明 | 优缺点 | 结论 |
|------|------|--------|------|
| **A. Inspector 拖拽绑定（Button.onClick 持久化调用）** | 复用现有 Button `onClick` 的 `m_PersistentCalls`，各实例在场景里直接绑定到 `UITitle`/`UI` 的公开方法 | 迁移最平滑，与现状（`m_TargetAssemblyTypeName` + `m_MethodName`）完全一致，不需改任何触发逻辑 | **采用** |
| B. 代码动态注册 | `UIMsgBox.Show(title, btn1Text, btn1Action, ...)` | 灵活但所有触发点需改代码 | 不采用（除非后续需要动态弹窗） |

**结论**：采用 **方案 A（单个通用 Prefab）+ Inspector 绑定回调**。`UIMsgBox.cs` 仅做**表现层控制**（隐藏多余按钮、刷新按钮文字、可选按钮文字直接读子 Text 的默认值），**不感知业务回调**——回调仍由场景实例上的 `Button.onClick` 拖拽绑定到现有 `UITitle`/`UI` 方法，与现状行为 100% 对齐。

### 3.3 通用 Prefab 结构（`UIMsgBox.prefab`）

```
UIMsgBox（根，挂 UIMsgBox 脚本）
├── WarningTxt（Text (Legacy)）            ← 提示文字（各实例在场景里改内容）
├── Btn1（Button：Image + 子 Text）        ← 第 1 个按钮
│     └── Text
└── Btn2（Button：Image + 子 Text）        ← 第 2 个按钮（单按钮场景隐藏）
      └── Text
```

- 根节点上的 `Image` 为弹窗背景（外观素材统一在此改）。
- 按钮文字、提示文字在**场景实例**上修改（Prefab 内为占位默认值，如「确定」/「取消」）。
- `UIMsgBox` 脚本字段：`mBtn1` / `mBtn2`（`GameObject` 或 `Button`）。脚本在 `Awake` 根据「是否有第 2 个按钮的配置」决定是否隐藏 `Btn2`（见 §3.4）。
- 单按钮实例：直接把 `Btn2` 在实例上 `SetActive(false)`，或脚本通过字段判空隐藏——两种都支持，脚本做防御性处理。

### 3.4 `UIMsgBox.cs` 职责（新脚本）

```csharp
public class UIMsgBox : MonoBehaviour
{
    [SerializeField] private GameObject mBtn1;   // 第 1 个按钮
    [SerializeField] private GameObject mBtn2;   // 第 2 个按钮（单按钮时可为空或由场景关闭）

    void Awake()
    {
        // 防御：mBtn2 为空或被场景显式关闭时，隐藏第 2 个按钮
        if (mBtn2 == null || !mBtn2.activeSelf) return;
    }
}
```

职责边界（**关键，遵循「表现与控制分离」**）：
- `UIMsgBox` **只负责**：按需隐藏第 2 个按钮、以及（可选）统一刷新按钮/标题文字。**不做任何面板切换、不做任何业务回调**。
- 按钮点击行为完全由场景实例的 `Button.onClick` 拖拽绑定（沿用现状的 `m_TargetAssemblyTypeName` 持久化调用），因此 `UITitle` / `UI` 的公开方法（`OnClickNewGame`、`OnClickQuit`、`OnClickConfirmReturnToTitle`、`OnClickRetry` 等）**无需改动**。

> 若后续需要代码动态弹窗（新业务），可再扩展 `UIMsgBox.Show(title, btn1Text, btn1Action, btn2Text, btn2Action)` 静态方法，本期不做。

### 3.5 各场景迁移

迁移原则：**不改变文案、不改变按钮触发方法、不改变 `UITitle`/`UI` 对弹窗节点的字段引用**。

#### 3.5.1 `Bootstrap.unity` — `MsgboxError`

- 将 `MsgboxError` 根节点内部结构替换为 `UIMsgBox.prefab` 实例（保留节点名 `MsgboxError`）。
- 在实例上配置：
  - `WarningTxt` = 现有错误文案（从现有节点拷贝）；
  - 单按钮：`Btn1` 文字 = 现有（OK），`onClick` 绑定 = 现有 `GameObject.SetActive(false)`（关闭自身）。
- 若 `Bootstrap` 场景存在对 `MsgboxError` 的引用（如某脚本 `Awake` 关闭它），保留引用。

#### 3.5.2 `Title.unity` — `MsgboxNewGame` / `MsgboxNoApiKey` / `MsgboxQuit`

- 将三个 MsgBox 根节点内部结构替换为 `UIMsgBox.prefab` 实例（保留节点名）。
- 各实例配置：
  - `MsgboxNewGame`：`WarningTxt` = 「注意！\n将会覆盖已有存档」；双按钮 `Btn1`=确认→`OnClickNewGame`、`Btn2`=取消→`SetActive(false)`。
  - `MsgboxNoApiKey`：单按钮→关闭。
  - `MsgboxQuit`：双按钮→确认=`OnClickQuit`、取消=`SetActive(false)`。
- `UITitle` 中 `mNewGameWarmingPanel` / `mNoApiKeyPanel` / `mQuitPanel` 三个 `GameObject` 引用字段继续指向这三个实例节点（脚本无改动）。

#### 3.5.3 `UI.prefab` — `MsgboxConfirmExit` / `MsgboxGameOver`

- `UI.prefab` 内的 `MsgboxConfirmExit` 已引用 `PanelConfirmExit.prefab`，`MsgboxGameOver` 已引用 `PanelGameOver.prefab`——这两处已是 **Prefab 引用**，无需手工复制。
- 二选一（由确认决定）：
  - **方案 1（最小改动）**：保留 `PanelConfirmExit.prefab` / `PanelGameOver.prefab` 两个独立 Prefab，只把它们内部的结构对齐通用 `UIMsgBox.prefab` 的**外观模板**（背景/按钮素材引用统一指向同一批素材）。好处：`UI.prefab` 引用零改动，行为零风险。
  - **方案 2（彻底统一）**：删除这两个 Prefab，改为在 `UI.prefab` 内引用通用 `UIMsgBox.prefab` 并重新拖拽 `UI` 脚本字段。改动面更大但模板唯一。
- **推荐方案 1**：本期诉求是「改素材一处全生效」——让 `PanelConfirmExit`/`PanelGameOver` 与通用 `UIMsgBox` 共享同一套素材引用即可达成，且不触碰 `UI.prefab` 既有引用与 `UI.cs` 字段。

### 3.6 素材引用统一（核心诉求落地）

- 将所有 MsgBox（通用模板 + `PanelConfirmExit` + `PanelGameOver`）的**背景 Sprite、按钮 Sprite、字体、字号、布局尺寸**整理为**同一组素材/常量**。
- 后续 UI 阶段只需替换这组素材（或直接改通用 `UIMsgBox.prefab`），即可同时影响 `Bootstrap`、`Title`、各 Level 场景的 MsgBox。
- 建议素材放 `Assets/Resources/UI/` 下（与现有 `PanelConfirmExit.prefab` 同目录），便于统一管理。

### 3.7 数据与协议

无协议、无 Python 改动。

## 4. 实现步骤

### 4.1 代码侧（Agent 完成）

1. 新增 `Assets/Scripts/IndependentAgentProject/ViewController/UI/UIMsgBox.cs`（表现层控制：隐藏第 2 个按钮等，§3.4）。

### 4.2 场景/Prefab 侧（Unity 编辑器内操作，需你确认后由你或 Agent 在编辑器完成）

2. 新建 `Assets/Resources/UI/UIMsgBox.prefab`：根节点（Image 底，挂 `UIMsgBox`）+ `WarningTxt` + `Btn1` + `Btn2`，占位文案「确定 / 取消」。
3. `Bootstrap.unity`：`MsgboxError` 内部结构 → `UIMsgBox` 实例，配文案与 OK 回调。
4. `Title.unity`：`MsgboxNewGame` / `MsgboxNoApiKey` / `MsgboxQuit` → `UIMsgBox` 实例，配文案与按钮回调；`UITitle` 三字段重新拖拽到实例节点。
5. `PanelConfirmExit.prefab` / `PanelGameOver.prefab`：内部结构对齐通用外观模板（素材引用统一）。
6. 人工验证（见 §6）。

> 场景 YAML 手改极易出错（fileID/guid），**优先在 Unity 编辑器内操作**。代码侧（`UIMsgBox.cs`）可由 Agent 直接新增。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 场景 YAML 手改破坏 Prefab 引用 / fileID | 一律在 Unity 编辑器内替换节点；不手改 YAML |
| `UITitle`/`UI` 对弹窗节点的字段引用断裂 | 保留实例节点名与层级，迁移后重新拖拽字段；迁移后编译/运行验证 |
| 按钮回调丢失（`m_PersistentCalls` 随节点替换丢失） | 替换后必须在实例上重新拖拽 `onClick` 到 `UITitle`/`UI` 方法；用 §6 逐条验证 |
| 单按钮实例残留隐藏的第 2 个按钮拦截点击 | `UIMsgBox.Awake` 防御性隐藏；单按钮实例在场景直接 `SetActive(false)` |
| 回退方案 | 保留被替换节点的备份（Prefab 变体或场景副本）；还原 `UIMsgBox.cs` 与场景节点即可回退 |

## 6. 测试建议

需在 Unity 编辑器内人工验证（纯 Unity 侧，不依赖 Python/协议；`Title` 触发新游戏 / `Bootstrap` 报错除外，沿用现有流程）：

| # | 步骤 | 期望 |
|---|------|------|
| 1 | 打开 `Bootstrap`，触发错误 | 弹出 `MsgboxError`，文案正确，点 OK 关闭 |
| 2 | 打开 `Title`，点「开始」 | 弹出 `MsgboxNewGame`，文案「注意！\n将会覆盖已有存档」，确认/取消行为与现状一致 |
| 3 | `Title` 无 API Key 场景 | 弹出 `MsgboxNoApiKey`，单按钮，行为与现状一致 |
| 4 | `Title` 点「退出」 | 弹出 `MsgboxQuit`，确认退出 / 取消关闭 |
| 5 | 进入 Level0，触发退出确认 | 弹出 `MsgboxConfirmExit`，确认返回标题 / 取消关闭 |
| 6 | 触发 GameOver | 弹出 `MsgboxGameOver`，重试等行为与现状一致 |
| 7 | 修改通用 `UIMsgBox.prefab` 背景/按钮素材 | 上述所有场景的 MsgBox 外观同步更新 |
| 8 | 单按钮 MsgBox（Error/NoApiKey） | 不出现第 2 个按钮，无遮挡 |

> 回归重点：**每个 MsgBox 的文案与按钮触发方法必须与迁移前逐一对比一致**（§3.1 表）。

## 7. 待确认问题（已确认，2026-08-19）

- [x] Prefab 形态选型：**方案 A 通用模板（推荐）** vs 方案 B 多变体。—— **已确认：方案 A（单个通用 MsgBox Prefab）**。
- [x] 按钮回调：Inspector 拖拽绑定（推荐，零代码改动） vs 代码动态注册。—— **已确认：Inspector 拖拽绑定**；`UIMsgBox.cs` 仅提供表现层控制，不感知业务回调。
- [x] `UI.prefab` 内 `PanelConfirmExit`/`PanelGameOver` 处理：方案 1 保留独立 Prefab 仅统一素材 vs 方案 2 全部并入 `UIMsgBox`。—— **已确认：方案 1（保留独立 Prefab，仅统一素材引用）**。
- [x] 是否需要 `UIMsgBox.Show(...)` 代码动态弹窗 API。—— **已确认：本期不做（暂无此需求）**。
- [x] 脚本与 Prefab/场景落地分工。—— **已确认：Agent 仅编写脚本并说明 Prefab 制作方式；Prefab 创建与场景替换由用户自行在 Unity 编辑器完成**。

---

## 8. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-19 | 用户确认方案：方案 A（通用模板）+ Inspector 拖拽绑定 + 仅编写脚本（Prefab/场景落地由用户自行完成）+ 本期不做动态弹窗 API。更新 PRD/solution 状态为「已确认」。新增 `UIMsgBox.cs`（表现层：单按钮自动隐藏 Btn2、缺失引用告警；不感知业务回调）。 |
| 2026-08-19 | 补充「MsgBox 必定显示在所有 UI 之前」：选用**方案一**，`UIMsgBox.OnEnable()` 中 `transform.SetAsLastSibling()`，每次显示时将实例移到所在 Canvas 最上层（同 Canvas 内后绘制在上层），覆盖主菜单/设置面板等。由用户加入脚本。注：`Bootstrap` 过渡层 Canvas SortingOrder=10000，若需连它也盖住再评估独立 Canvas 方案。 |
| 2026-08-19 | **验收通过**：用户完成 UIMsgBox.prefab 制作与各场景（Bootstrap / Title / UI.prefab）替换落地，确认各 MsgBox 文案与按钮行为一致、改 prefab 素材全局生效。状态「已确认」→「已实现」。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
