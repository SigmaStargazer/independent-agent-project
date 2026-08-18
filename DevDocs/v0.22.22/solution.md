# 技术方案 — v0.22.22 Title 场景「按任意按钮」启动画面

> **状态**：已确认
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-08-18

---

## 1. 方案概述

在 Title 场景内新增一个「按任意按钮」启动画面，用**纯 Unity C#（旧版 Input Manager）**实现一个状态机（`PressAnyButton → TitleMenu` 双向切换），复用项目现有旧输入 API（`Input.anyKeyDown` / `Input.GetButtonDown("Menu")`），不引入 Input System，不新增依赖。核心难点是规避业界已知的「ESC 返回时被 `anyKeyDown` 立刻吞掉切回主菜单」的输入抖动问题，通过**输入消抖（按键解锁延时 + 状态切换互斥）**解决。补充开发：启动画面「按任意按钮」提示文字带**呼吸闪烁动画**（`CanvasGroup.alpha` 正弦脉动，零新依赖）。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | — | 无 |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UITitle.cs` | 修改（总控：面板切换状态机 + 统一 ESC/任意键分发） |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UIPressAnyButton.cs` | 新增（启动画面闪烁表现，仅管本面板动画，可独立复用） |
| Unity | `Assets/Scenes/Title.unity` | 修改（场景内加启动画面节点 + 提示文字 CanvasGroup + UITitle 引用三面板） |
| 协议 | `Tools/message.proto` | 无 |

> 说明：`UITitle.cs` 挂在 UI 根节点（常驻、永不失活）作为总控；场景中 `m_TargetAssemblyTypeName` 仍为旧命名空间 `ShootingEditor2D.UITitle` 的历史遗留，但脚本 guid `4cb50e8e...` 对应的是 `IndependentAgentProject.UITitle` 源码，改动即生效。

## 3. 详细设计

### 3.1 数据与协议

无协议改动。

### 3.2 Python（Brain）

无改动。

### 3.3 Unity（Environment）

#### 3.3.1 业界方案调研结论

| 方案 | 说明 | 结论 |
|------|------|------|
| **Input System 方案**（`/*/<button>` 绑定 + `triggered`） | 业界 2026 推荐，但项目是 2021.3 且未启用 Input System，引入成本高 | 本期不采用 |
| **旧版 `Input.anyKeyDown`** | 项目当前输入栈即旧版 Input Manager，天然可用；`anyKeyDown` 仅"按下沿"触发，不会因持续按住反复触发 | **采用** |
| **只判断 `Input.GetKeyDown(KeyCode.Escape)` 返回** | 漏掉鼠标/其它键，体验差 | 不采用 |

`Input.anyKeyDown` 特性确认（Unity 官方文档）：
- 返回"用户首次按下任意键/鼠标按钮的那一帧"，**必须轮询自 `Update`**；
- 在用户**松开所有键再按下**之前不会再次为 true（天然消抖，不会因长按连发）。

#### 3.3.2 场景层级

重构后 Title 场景采用 **UI 根节点总控**结构：

```
UI（根节点，挂 UITitle 总控脚本，常驻永不失活）
├── PanelPressAnyButton   ← 「按任意按钮」启动画面（挂 UIPressAnyButton，仅管闪烁）
│     └── Text (TMP)「按任意按钮」  ← 提示文字（挂 CanvasGroup，供闪烁）
├── PanelMenu             ← 主菜单（开始 / 继续 / 设置 / 退出）
├── PanelConfig           ← 设置
├── NewGameWarningPanel   ← 新游戏确认弹窗
├── NoApiKeyPanel         ← 无 API Key 弹窗
└── QuitPanel             ← 退出确认弹窗
```

职责划分：
- **`UITitle`（挂 UI 根节点）**：总控所有面板的显隐切换 + 统一 ESC/任意键分发。
- **`UIPressAnyButton`（挂 PanelPressAnyButton）**：只负责本面板的闪烁表现，不参与面板切换。

> 关键架构修正（v0.22.22 开发中）：
> - 早期版本把 `UIPressAnyButton` 挂在 `PanelPressAnyButton` 上并让它自己 `SetActive(false)` 自己，会导致脚本随面板关闭而失效——**已废弃**。
> - 面板切换统一由常驻的 `UITitle` 控制，被控面板可随意开关，脚本不受影响。

#### 3.3.3 状态机与交互逻辑（核心）

**面板切换由 `UITitle`（UI 根节点，常驻）统一管理**：

```
状态：
  PressAnyButton（按任意按钮，启动画面，默认）
  Menu（主菜单）
  Config（设置）

切换规则（UITitle.Update 统一分发）：
  PressAnyButton --按任意键(anyKeyDown)--> Menu（隐藏 PressAnyButton、显示 Menu）
  Menu          --ESC(Menu轴)-------------> PressAnyButton（显示 PressAnyButton、隐藏 Menu）
  Config        --ESC(Menu轴)-------------> Menu（显示 Menu、隐藏 Config）
```

**输入消抖设计（关键，规避业界已知坑）**：

1. **`mInputLockUntil` 时间戳消抖**：
   - 每次发生状态切换时，记录 `mInputLockUntil = Time.time + mInputLockTime`（默认 0.25s 消抖窗口）。
   - 在消抖窗口内，`Update` **不响应** `anyKeyDown` 与 ESC。
   - 目的：当玩家在主菜单按 ESC 返回启动画面时，**同一次 ESC 的 `anyKeyDown` 也在同一帧为 true**；若无消抖，会立即被当成"按任意键"又切回主菜单，形成来回抖动（正是 Unity 论坛公认问题）。
   - 闪烁动画独立于消抖窗口运行，返回启动画面时立即恢复，不受消抖影响。

2. **状态互斥**：
   - 三个面板同时至多一个激活，由 `UITitle` 的 `ShowPressAnyButton / ShowMainMenu / ShowConfig` 保证。
   - 弹窗（NewGameWarning / NoApiKey / Quit）为覆盖层，由各自按钮事件开关，不参与三态互斥。

3. **启动画面阶段 ESC 语义**：
   - 启动画面阶段 ESC 被 `anyKeyDown` 捕获，作为普通按键进入主菜单；**不退出游戏**。
   - 主菜单 / 设置阶段 ESC 走 `Input.GetButtonDown("Menu")`（escape）逐级返回。

**闪烁逻辑（`UIPressAnyButton`，面板自有脚本）**：

- `UIPressAnyButton.Update` 使用 `CanvasGroup.alpha` 正弦呼吸：
  `alpha = Lerp(mMinAlpha, mMaxAlpha, (sin(2π · Time.time / mBlinkTime) + 1) / 2)`
- `mBlinkTime` = 呼吸周期（秒），数值越大闪得越慢（**时间语义，非速度**）。
- 仅当本面板 `activeSelf` 时运行；面板被 UITitle 关闭（失活）后 `Update` 自动停止，重新激活后恢复。

#### 3.3.4 UITitle 处理（总控）

`UITitle.cs` 作为 Title 场景 **UI 根节点的总控脚本**（挂在常驻的 UI 根节点上，永不失活），统一负责：

- **面板显隐切换**：`ShowPressAnyButton / ShowMainMenu / ShowConfig` 三方法，任一调用即互斥地开关 `PanelPressAnyButton / PanelMenu / PanelConfig` 三面板。
- **统一输入分发**：在 `Update` 中按当前激活面板分发 `anyKeyDown`（启动画面阶段）与 `ESC`（主菜单/设置阶段）。
- **输入消抖**：每次切换调用 `LockInput()` 记录 `mInputLockUntil`，消抖窗口内不响应输入。
- **业务回调**：保留「开始 / 继续 / 设置 / 退出」等原有按钮事件（`OnClickNewGame` 等）。

> 关键：**不要在 `UITitle.Awake` 中 `SetActive(false)` 任何面板**——`UIPressAnyButton` 挂在 `PanelPressAnyButton` 上，初始显隐统一由 `UITitle.Start` 调 `ShowPressAnyButton()` 设置，确保脚本与面板初始状态一致。
>
> 职责划分：`UITitle` 管「切到哪个面板」；`UIPressAnyButton` 管「当前面板自己的表现（闪烁）」。二者通过面板显隐协作，不引入事件耦合。

#### 3.3.5 「按任意按钮」闪烁提示（补充开发内容）

**业界方案对比**（决定本版本做法）：

| 方案 | 原理 | 优缺点 | 结论 |
|------|------|--------|------|
| **A. Alpha 正弦脉动（呼吸）** | 每帧 `alpha = Lerp(min, max, (sin(t·2π/周期)+1)/2)` | 平滑自然、实现最简、无依赖 | **采用** |
| B. Alpha 硬切闪烁 | `alpha` 在 0/1 间固定间隔切换 | 复古感、生硬 | 不采用 |
| C. 缩放/位移脉冲 | 改 `localScale` / `anchoredPosition` | 视觉生动但更抢眼 | 不采用（本版本聚焦文字提示） |
| D. DOTween 缓动 | `DOFade` + 循环 | 代码简洁但引入新依赖 | 不采用（项目无 DOTween） |
| E. TMP vertex 动画 | `AnimateVertexColors` 等 | 效果华丽、复杂度高 | 不采用（属标题文字打磨） |

**采用方案 A 的理由**：
- **零新依赖**（项目无 DOTween/LeanTween，`Update` + `Mathf.Sin` 即可）；
- **契合项目先例**——`TransitionUI.cs` 已用 `CanvasGroup.alpha` 做 UI 显隐，风格一致；
- **呼吸感更现代**，是主流 3A 标题「提示文字」的常见处理。

**设计**：闪烁逻辑放在 **`UIPressAnyButton`**（挂在 `PanelPressAnyButton` 上，**只管理本面板表现**），只在本面板激活时脉动：

- `mHintGroup`（`CanvasGroup`，挂在提示文字或其父节点）——空引用则跳过动画，保持向后兼容；
- `mBlinkTime`（呼吸周期，秒，默认 2；数值越大闪得越慢）；
- `mMinAlpha`（最暗，默认 0.15）/ `mMaxAlpha`（最亮，默认 1）。

```
Update 内（本面板 activeSelf 时）：
  t = (sin(Time.time * 2π / mBlinkTime) + 1) / 2
  mHintGroup.alpha = Lerp(mMinAlpha, mMaxAlpha, t)
```

关键点：
- 用 `CanvasGroup` 而非直接改 `Text.color`——可整体作用于文字及子元素，且与 `TransitionUI` 先例一致；
- 面板被 UITitle 关闭（失活）后，`Update` 不再执行，脉动自然停止，无需额外开关；重新激活后自动恢复；
- 平滑连续、无需协程；
- **`UIPressAnyButton` 不再做任何面板切换 / 状态机**——那是 UITitle 的职责。

**实现位置**：`UIPressAnyButton.cs`（本版本新增脚本，仅表现层）。

#### 3.4 工具 / ActionSequence

不适用。

## 4. 实现步骤

### 4.1 已完成（代码侧，2026-08-17）

1. 修改 `UITitle.cs`：作为 UI 根节点总控，新增三面板引用（`mPressAnyButtonPanel` / `mMainMenuPanel` / `mConfigPanel`）+ 统一 ESC/任意键分发 + 输入消抖 + `ShowPressAnyButton / ShowMainMenu / ShowConfig` 互斥切换——**已完成**。
2. 新增 `UIPressAnyButton.cs`：**只负责本面板闪烁表现**（`mHintGroup` / `mBlinkTime` / `mMinAlpha` / `mMaxAlpha`，面板激活时 `CanvasGroup.alpha` 正弦脉动），**不做任何面板切换/状态机**——**已完成**。

### 4.2 补充开发：闪烁提示

3. 闪烁逻辑已并入 `UIPressAnyButton.cs`（方案 §3.3.5），由面板自有脚本管理；`UITitle` 不参与闪烁，二者职责分离——**已完成**。

### 4.3 待你完成（场景侧，Unity 编辑器内操作）

4. 编辑 `Title.unity`：
   - 确认/新增三个面板节点：`PanelPressAnyButton`（含 Image 半透明底 + Text「按任意按钮」）、`PanelMenu`（主菜单）、`PanelConfig`（设置）；
   - 为「按任意按钮」提示文字（或其父节点）添加 `CanvasGroup` 组件；
   - 在 **UI 根节点**（挂 `UITitle` 的常驻节点）上拖拽关联：
     - `UITitle.mPressAnyButtonPanel` → `PanelPressAnyButton`；
     - `UITitle.mMainMenuPanel` → `PanelMenu`；
     - `UITitle.mConfigPanel` → `PanelConfig`；
     - `UITitle.mNewGameWarmingPanel` → 新游戏弹窗；
     - `UITitle.mNoApiKeyPanel` → 无 API Key 弹窗；
     - `UITitle.mQuitPanel` → 退出弹窗；
   - 在 `PanelPressAnyButton` 上挂 `UIPressAnyButton` 组件，拖拽关联：
     - `UIPressAnyButton.mHintGroup` → 提示文字的 `CanvasGroup`。
   - 确保 `UITitle` 挂在**常驻的 UI 根节点**上（不会被任意面板开关失活）。
5. Unity 中打开 Title 场景人工验证（见 §6）。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| ESC 返回后 `anyKeyDown` 立刻切回主菜单（业界已知坑） | `mInputLockUntil` 时间戳消抖窗口 `mInputLockTime`（默认 0.25s） |
| 场景序列化（YAML）手改易出错、guid 引用错 | 优先建议在 Unity 编辑器内操作；如手改 YAML 需严格核对 fileID/guid，并人工打开场景验证 |
| 弹窗被主菜单显隐牵连 | 弹窗为独立覆盖层节点，不参与三态互斥，由按钮事件开关 |
| 鼠标长按 / 滚轮误触发 | 仅用 `anyKeyDown`（按下沿），天然忽略持续按住与滚轮增量；消抖窗口进一步过滤 |
| 手柄支持 | `Menu` 轴已绑定 `joystick button 1`，`anyKeyDown` 覆盖手柄按键，无需额外处理 |
| 远期：是否迁移 Input System | 本需求**不迁移**（输入面小、旧 API 够用）。已调研：Unity 2021.3 官方 released 版本 1.7.0，本项目 `scriptingRuntimeVersion=1`(.NET 4.x)、`apiCompatibilityLevel=6`(.NET Standard 2.1) 均满足安装前提；未来若需手柄完整适配 / 键位重绑 / 多玩家 / 主机平台，再单独立项评估迁移 |
| `UIPressAnyButton` 自关面板导致脚本失效（早期方案缺陷） | **已废弃**：面板切换全权由常驻 `UITitle` 总控，`UIPressAnyButton` 仅管闪烁、不自关自身 |
| 回退方案 | 还原 `UITitle.cs` 为原状并移除 `UIPressAnyButton.cs` 组件即可回到现状 |

## 6. 测试建议

需在 Unity 编辑器内人工验证（不依赖 Python/协议）：

| # | 步骤 | 期望 |
|---|------|------|
| 1 | 进入 Title 场景 | 显示「按任意按钮」，主菜单不可见 |
| 2 | 按键盘任意键 | 进入主菜单（「开始 / 继续」可见） |
| 3 | 回到启动画面，按鼠标左键 | 进入主菜单 |
| 4 | 主菜单按 ESC | 返回「按任意按钮」画面，且**不会**立刻又跳回主菜单 |
| 5 | 启动画面按 ESC | 不退出游戏，进入主菜单 |
| 6 | 主菜单点「开始」 | 弹出 WarningPanel，确认后进入新游戏（原有行为不变） |
| 7 | 主菜单点「继续」 | 进入继续游戏流程（原有行为不变） |
| 8 | 反复 ESC ↔ 任意键 多次 | 状态稳定无抖动 |
| 9 | 启动画面停留若干秒 | 「按任意按钮」文字平滑呼吸脉动（Alpha 在 mMinAlpha~mMaxAlpha 间循环），无跳变 |
| 10 | 按下任意键进入主菜单后 | 闪烁停止（提示文字不再脉动） |
| 11 | 主菜单按 ESC 返回启动画面 | 闪烁恢复（重新开始呼吸） |
| 12 | 主菜单点「设置」 | 显示 PanelConfig，隐藏 PanelMenu |
| 13 | 设置面板按 ESC | 返回主菜单（显示 PanelMenu，隐藏 PanelConfig） |

> 注：纯 Unity 侧改动，可在不启动 Python 的情况下验证 1-5；6-7 需要 Python 联调（沿用现有流程）。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-17 | 新增 `UIPressAnyButton.cs`；`UITitle.cs` 保留 WarningPanel 逻辑不变、不干预启动画面。修正时序：不在 `UITitle.Awake` 中 `SetActive(false)` 启动画面面板，否则会阻断 `UIPressAnyButton.Start`。场景侧 `Title.unity` 由用户自行在 Unity 编辑器配置（方案 §4.3）。 |
| 2026-08-17 | 补充开发内容：确定「按任意按钮」闪烁采用方案 A（`CanvasGroup.alpha` 正弦呼吸），写入方案 §3.3.5 / §4.2 / §6；代码待实现（`UIPressAnyButton.cs` 加闪烁字段），场景侧需为提示文字加 `CanvasGroup`。 |
| 2026-08-17 | 闪烁逻辑已实现：`UIPressAnyButton.cs` 新增 `mHintGroup`/`mBlinkTime`/`mMinAlpha`/`mMaxAlpha`，启动画面激活时正弦脉动；置于输入消抖之前，返回启动画面立即恢复呼吸。 |
| 2026-08-17 | 修正 `mBlinkSpeed` 语义矛盾：改名 `mBlinkTime`，公式由"速度×2π"改为"周期 2π/mBlinkTime"，数值越大闪得越慢（Tooltip 与实现一致）。 |
| 2026-08-17 | **架构定稿：职责分离**。`UITitle` 作 UI 根节点总控（面板切换状态机 + 统一 ESC/任意键分发 + 消抖，挂常驻节点）；`UIPressAnyButton` 回归**面板自有脚本**，**只管理本面板闪烁表现**（不做任何切换/状态机）。修复早期"脚本自关所在面板导致自身失效"的缺陷。代码侧完成，场景侧由用户配置（§4.3）。 |
| 2026-08-18 | **补充需求（验收后）**：补全 `UITitle.OnClickQuit()` 直接退出游戏——构建版 `Application.Quit()`；编辑器下 `UnityEditor.EditorApplication.isPlaying = false`（`#if UNITY_EDITOR` 双分支，风格同 `BootstrapEntry.cs`）。触发按钮/是否弹确认框由用户在场景侧自行配置，脚本不做假设。**验收通过**。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
