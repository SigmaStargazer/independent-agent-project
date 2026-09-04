# 技术方案 — v0.23.4 配置完整性提示 + 设置页多 Tab（模型配置 / 画面）

> **状态**：已确认（代码已实现，编译通过；画面配置 + API 配置已 MVC 化；已修复打包后画面设置「退出不还原」bug + 默认值需求；待场景调整 + 验收确认）
> **依据 PRD**：`PRD.md`
> **引用方案**：`DevDocs/feature-design/打包方案.md`（§3.3、§4.2、§8）
> **基于版本**：`DevDocs/v0.23.1/`、`DevDocs/v0.23.0a-b/`
> **最后更新**：2026-09-01（画面配置 + API 配置 MVC 化：纯数据 Model + Command + Query + Controller 基类；API Key 明文风险基线记录；修复打包后「退出不还原」bug（RevertGameSettingsCommand）；默认值需求：无设置值时全屏+1920x1080；GameSettings 数据获取收敛为单 Query（GetGameSettingsQuery）；Unity 编译通过 + 运行时自检通过）

---

## 1. 方案概述

围绕两条线实现：

1. **需求一（配置不完整提示）**：新增 `MsgboxEmptyApiKey` 弹窗；在 `UITitle.TryLeaveSubPanel()` 中增加「当前 Panel 三文本框是否有空项」判定，空 → 弹 `MsgboxEmptyApiKey`（继续配置 / 退出），非空且变更 → 维持 `MsgboxSaveApiKey`。纯 Unity UI 侧，不改协议、不改 Python。
2. **需求二（设置页多 Tab + 画面配置）**：把 `PanelSetting` 重构为**可扩展的多 Tab 架构**（Tab 列表数据驱动，未来任意 Tab 无需改 Tab 框架代码）。`PanelSetting` 作为设置页**根容器**（放背景图 + `UISetting`），内含 `PanelTab`（Tab 按钮容器）与 4 个配置子面板。本次实现「模型配置」「画面」两个 Tab（`ContentDisplaySettings` 为画面 Tab 内容区）。**画面配置逻辑并入 `UISetting`**（用户明确弃独立 `UIGameSettings`），配合 `GameSettingsStore`（偏好持久化，复用 `JsonConfigIO`）。新增 **`MsgboxSaveSetting`**：退出 `PanelSetting` 时若画面设置有变更则弹窗确认（保存并退出 / 退出 / 取消）。UI 采用 **uGUI 内职责拆分**（不引入第三方 UI 包 / 不迁 UI Toolkit），理由见 §2 调研。

**架构决策（用户已确认，见 PRD §7）**：
- 设置页重构方向：**uGUI 内职责拆分 + 数据驱动 Tab 架构**，不引入成熟 UI 包、不迁 UI Toolkit。原因见 §2.2/§2.3。
- **`PanelSetting` 作为设置页根容器**（用户确认）：放设置期间的背景图 + `UISetting`（唯一脚本）；内部 `PanelTab`（Tab 按钮容器）+ 4 个配置子面板（`PanelLLMAgent` 等），**同一层级显示互斥**。
- **弃 `UIGameSettings`**（用户确认）：画面配置（显示模式/分辨率）逻辑**并入 `UISetting`**——因 `ContentDisplaySettings` 会随 Tab 切换失活，不能挂其上的脚本。
- **`MsgboxSaveSetting`**（用户新增需求）：退出 `PanelSetting` 时若画面设置有变更 → 弹窗确认（保存并退出 / 退出 / 取消），复用现有 `mSaveSettingMsgBox`。
- 画面配置持久化：`Data/Config/game_settings.json`（复用 `JsonConfigIO`）。
- **画面配置 + API 配置 MVC 化（v0.23.4 内落地，用户确认）**：`GameSettingsModel` / `ApiConfigModel` 为**纯数据 Model**（只存 `BindableProperty`，配置解析在 `OnInit`，参照 `GameModel`/`GunConfigModel`）；修改封装为 **Command**（`ChangeDisplayModeCommand`/`ChangeResolutionCommand`/`SaveGameSettingsCommand`/`SaveApiConfigCommand`）；完整性/空判断用 **Query**（`ApiConfigReadyQuery`/`ApiConfigEmptyQuery`）；UI 继承 `IndependentAgentProjectController`（`MonoBehaviour + IController`）用 `GetModel`/`SendCommand`/`SendQuery`，`BindableProperty` 事件驱动 UI 自动刷新。`GameSettingsStore`/`ApiConfigStore` 保留为静态 I/O 工具。
- **API Key 明文风险基线**：`api_config.json` 当前为明文存储，且 `Debug.Log` 打印含 Key 文件路径、内存常驻明文、Python 侧同读此文件（跨进程明文）。**加密属后续版本（Unity+Python 两端协同）**，不在本版本范围；本版本保持格式/路径不变。
- 分辨率列表：**预置列表**（含 1920x1080 + 常见 4:3，最左最低/最右最高）。
- 语言切换：本期**不实现**，仅调研 + 文案数据驱动（独立文件）铺路（见 §2.4）。未来语言切换放「游戏」Tab（`ContentGameSettings`）。
- 场景调整：**由用户手动完成**，Agent 提供独立《场景调整指引》文档。

---

## 2. 调研（业界成熟方案）

> 需求二要求「先调研成熟实现方案」，需求三要求「提供语言切换业界成熟方案」。本节为调研结论与选型依据。

### 2.1 显示模式 / 分辨率切换（Unity 官方 API）

**结论：直接使用 Unity 原生 `Screen` API，无需任何第三方库。**

- **显示模式**：`Screen.fullScreenMode = FullScreenMode.X`，枚举：
  - `Windowed`：标准可移动窗口（桌面平台）。
  - `FullScreenWindow`：无边框全屏窗口，覆盖整个屏幕（所有平台通用）。
  - `ExclusiveFullScreen`：独占全屏（仅 Windows，切换最彻底但可能有闪屏/兼容问题）。
  - `MaximizedWindow`：最大化窗口（Win/mac）。
  - 需求三态「窗口化 / 无边框 / 全屏」对应 `Windowed` / `FullScreenWindow` / `ExclusiveFullScreen`。业界实践（参考 Unity 文档与社区）普遍推荐：**无边框用 `FullScreenWindow`，全屏用 `ExclusiveFullScreen`**；若担心兼容性，全屏也可用 `FullScreenWindow`（此时无边框与全屏差异仅在分辨率自适应）。
- **分辨率**：`Screen.SetResolution(width, height, FullScreenMode, preferredRefreshRate)`。注意：
  - 设置并非立即生效，而在**当前帧结束后**应用；切换后如需立即读 `Screen.width/height` 需在下一帧/延迟一帧读取。
  - 指定分辨率不被显示器支持时，Unity 自动用最接近的支持分辨率（文档明确）。
  - 全屏下建议把分辨率设为显示器原生（`Screen.currentResolution`），避免拉伸模糊。
- **边界禁用**：显示模式/分辨率列表是**有穷线性列表**，用「当前下标」即可判断最左/最右，箭头禁用是纯 UI 状态，无异步问题。
- **持久化**：Unity 文档说明 PlayerPrefs 在运行时变更 fullScreenMode/resolution 后不会自动写回；需自行持久化（本项目用 `GameSettingsStore` 写 `game_settings.json`）。启动时按存储值应用（`Screen.fullScreenMode` / `Screen.SetResolution`）。

### 2.2 UI 架构：uGUI 重构 vs 引入成熟 UI 包 / UI Toolkit

**结论：uGUI 内职责拆分（不引入第三方 UI 包、不迁 UI Toolkit）。**

| 方向 | 说明 | 评估 |
|------|------|------|
| **A. uGUI 内职责拆分 + 数据驱动 Tab（推荐）** | 沿用 uGUI + 现有 `Msgbox.prefab` / `UISetting`，`PanelSetting` 作为设置页根容器（背景图 + `UISetting`）；`UIModelConfig`（模型配置数据）拆出；画面配置（显示模式/分辨率）**并入 `UISetting`**（因 `ContentDisplaySettings` 随 Tab 失活，不挂其上的独立脚本）；**Tab 切换并入 `UISetting`**，`UITitle` 只做页面级进出设置页；**Tab 列表由数据驱动**（配置表），未来任意 Tab 无需改 Tab 框架代码 | ✅ 与现有架构一致；改动可控；无新依赖；满足「多 Tab + 画面配置」；为后续语言切换提供数据驱动文案入口 |
| B. 引入成熟 UI 包（如 UI Toolkit / 第三方 Tab 插件） | UI Toolkit 适合编辑器工具/复杂数据 UI，游戏内 UI 生态仍以 uGUI 为主 | ❌ UI Toolkit 2021.3 在游戏内运行时 UI 支持不成熟（本项目 Unity 2021.3.8f1c1）；第三方插件引入额外依赖与维护成本，与「打包零额外依赖」约束冲突 |
| C. 完全代码重构（自建 UI 框架） | 自写 View-Controller 框架（视图注册、路由、事件） | 对当前规模（Title 单场景数面板）**过度设计**；重构风险大、收益小，违背「架构优先但不过度」原则 |

**为什么 A 是当下最干净的方向**：

1. 项目已有清晰的「`UITitle` 总控 + `UISetting` 配置读写」拆分（v0.23.0b 确立），方向正确；当前问题只是**继续在 `UITitle` 堆页面切换**会让它膨胀。本次把「模型配置数据」抽成独立 `UIModelConfig`，把「画面配置」并入 `UISetting`（`PanelSetting` 根容器，配置期间常驻）并**并入面板内 Tab 切换**，`UITitle` 保持只做页面级进出（打开/关闭设置页），符合既有架构。
2. **Tab 架构必须可扩展**（用户明确未来有更多 Tab，如「游戏」放语言切换）。因此 Tab 列表用**数据驱动**：一个 `SettingsTabConfig` 表定义「Tab 名 / 对应内容区」，由 `UISetting`（挂 `PanelSetting`）遍历表生成 Tab 切换逻辑；新增 Tab 只需在表中加一项 + 场景加内容区，**不改 Tab 框架代码**。不用「写死两个 Tab 字段」的方式。
3. 引入成熟 UI 包在本项目（Unity 2021.3、纯 Windows 打包、uGUI 生态）下没有足够收益，反而增加体积/维护/编码基线负担。

### 2.3 UI 多 Tab 的成熟做法（uGUI）

业界常见两种，本项目用**数据驱动 Tab + 内容区互斥显示**：

- 方式一：**Tab 按钮 + 内容区**，点 Tab 切内容区 `SetActive`（最常用、最直观）。
- 方式二：**Toggle 组**（`ToggleGroup`）实现单选 Tab，视觉由 `Toggle` 的 `isOn` 驱动。
- 本项目：用**普通 `Button` + 数据驱动 Tab 配置表**，由 **`UISetting`**（挂 `PanelSetting`）持有 `SettingsTabConfig[]`（每项含 Tab 按钮引用、内容区引用、文案 key），`SelectTab(index)` 统一切显隐与选中态。Tab 按钮视觉选中态（高亮）通过切换时改 `Image.color` / 字号实现。`UITitle` 不参与面板内 Tab 切换。

### 2.4 语言切换（中英文本地化）——业界成熟方案与文案存储方式

**本期不实现**（需求三注明「可以后版本再实现」），但给出选型结论与文案存储方式，并让本期 UI 为后续铺路。

#### 2.4.1 业界通用做法：文案是否用独立文件存储？

**是。业界成熟方案普遍将文案从代码/场景中剥离，存到独立文件/表**，而不是硬编码进代码。主流组织方式：

| 存储方式 | 说明 | 代表 |
|----------|------|------|
| **Localization 表（Key-Value）** | 每个语言一个表，UI 用 key 引用；运行时按当前语言查表 | Unity Localization（String Table）、I2（LanguageSource） |
| **CSV / Google Sheets** | 翻译文件用表格维护，可导出/导入、多人协作 | Unity Localization 支持 CSV/Sheets 同步；I2 支持 Google Sheets 双向 |
| **JSON / 资源文件** | 轻量 key-value 存 JSON 或 ScriptableObject | 自研轻量方案常用 |

**业界共识**：UI 组件通过 **key（Term）** 引用文案，代码/场景**不出现自然语言字面量**；语言表独立存储（内置资源或外部文件），运行时按语言加载。切换语言时只换表，不动代码/场景。

#### 2.4.2 本项目选型

| 方案 | 特点 | 适配本项目 |
|------|------|-----------|
| **Unity 官方 Localization 包**（`com.unity.localization`） | 官方、基于 Addressables、String/Asset 表、运行时切语言、CSV/Google Sheets 同步 | ✅ **推荐（长期）**。新项目官方首选；但需引入 Addressables，Unity 2021.3 支持，体积/复杂度中等 |
| **I2 Localization** | 老牌第三方（$49），同步 `GetTranslation(key)`、Google Sheets 双向、旧项目多 | ⚠️ 本项目新做，无历史包袱，不必为第三方付费 |
| 自研（JSON 文案表 + 查表脚本） | 轻量、无依赖 | 本期采用：**JSON 文案文件 + `UITextProvider` 查表**，为后续迁移 Localization 铺路 |

**本期落地（数据驱动文案文件）**：

- 新增文案资源文件（**JSON**，放 `Assets/Resources/UI/` 或 Resources 可加载位置，UTF-8）：

```json
{
  "tab_model_config": "模型配置",
  "tab_game_settings": "画面",
  "mode_windowed": "窗口化",
  "mode_borderless": "无边框",
  "mode_fullscreen": "全屏",
  "resolution_format": "{w}x{h}",
  "empty_api_key_title": "配置不完整",
  "empty_api_key_hint": "有模型配置项为空，无法测试保存",
  "empty_api_key_continue": "继续配置",
  "empty_api_key_exit": "退出"
}
```

- 新增 `UITextProvider`（静态查表）：`Resources.Load<TextAsset>("UI/strings_zh_CN").text` → `JsonUtility`/手解析为字典，`Get("tab_model_config")` 返回文案。**本期只有中文一个语言文件**；未来加语言 = 新增一个语言文件 + 运行时按当前语言加载，**代码/场景零改动**（这正是本地化的 key-value 模式）。

```csharp
public static class UITextProvider
{
    private static Dictionary<string, string> s_map;
    public static string Get(string key, params object[] args)
    {
        // 懒加载 Resources/UI/strings_zh_CN.json；缺失 key 返回 key 本身便于排查
        // args 用于 {0} 占位（如分辨率格式）
    }
}
```

- **代码中不用中文字面量**：动态文案一律 `UITextProvider.Get(key)`；场景静态文案（按钮 Text）本期仍在 Prefab 上配置（保持现状），但**新增文案的 key 与文件对齐**，后续接 Localization 时在 Prefab 挂 Localize 组件替换 Term 即可。
- **不要在代码里拼接自然语言句子**：拼接式文案难以本地化，本期避免（分辨率文本用 `{w}x{h}` 格式，显示模式用名词而非句子）。

> 说明：这一步为后续语言切换**铺路**——届时方案选 Unity Localization 包时，只需把 JSON 文案表导出为 String Table；选自研时，多语言即多一个 JSON 文件。本期新增 UI 全部走 `UITextProvider`，代码里不再出现新中文字面量（验收项）。


---

## 3. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | — | 无 |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UITitle.cs` | 修改（TryLeaveSubPanel 空项判定、新增 EmptyApiKey 弹窗引用与按钮、TryLeaveSettingTab 画面变更检测 + MsgboxSaveSetting 三按钮、ShowSetting 复位 Tab、mSetting 改引用 UIModelConfig） |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UISetting.cs` | 修改（挂 PanelSetting：并入数据驱动 Tab 切换 + 跨 Tab 协调 + **画面配置**（显示模式/分辨率 + HasDisplaySettingsChanged/SaveDisplaySettings）；模型配置数据拆出） |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UIModelConfig.cs` | 新增（挂 ContentModelConfig，模型配置数据：12 输入框读写/变更/完整性/复制/测试后保存） |
| Unity | `Assets/Scripts/IndependentAgentProject/Services/GameSettingsStore.cs` | 新增（画面偏好持久化，复用 JsonConfigIO） |
| Unity | `Assets/Scripts/IndependentAgentProject/Model/GameSettingsModel.cs` | 新增（MVC 化：纯数据 Model，`DisplayModeIndex`/`ResolutionIndex`，`OnInit` 解析） |
| Unity | `Assets/Scripts/IndependentAgentProject/Model/ApiConfigModel.cs` | 新增（MVC 化：纯数据 Model，12 个 `BindableProperty<string>`，`OnInit` 解析） |
| Unity | `Assets/Scripts/IndependentAgentProject/Command/ChangeDisplayModeCommand.cs` / `ChangeResolutionCommand.cs` / `SaveGameSettingsCommand.cs` / `SaveApiConfigCommand.cs` | 新增（MVC 化：修改/落盘封装为 Command） |
| Unity | `Assets/Scripts/IndependentAgentProject/Query/ApiConfigQueries.cs` | 新增（MVC 化：`ApiConfigReadyQuery`/`ApiConfigEmptyQuery`） |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/IndependentAgentProjectController.cs` | 新增（MVC 化：`MonoBehaviour + IController` 基类，参照 `ShootingEditor2DController`） |
| Unity | `Assets/Scripts/IndependentAgentProject/IndependentAgentProject.cs` | 修改（注册 `IGameSettingsModel` + `IApiConfigModel`） |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UISetting.cs` | 修改（继承 Controller；画面配置改从 `IGameSettingsModel` 取数 + `SendCommand` + `BindableProperty` 订阅刷新） |
| Unity | `Assets/Scripts/IndependentAgentProject/ViewController/UI/UIModelConfig.cs` | 修改（继承 Controller；改用 `IApiConfigModel` + `SaveApiConfigCommand` + Query，文本框为 View 编辑缓冲） |
| Unity | `Assets/Scenes/Title.unity` | 场景侧（你操作）：4 个配置子面板移入 PanelSetting；新增 PanelTab + 两个 Tab 按钮（模型配置/画面）+ ContentDisplaySettings 内容区；UISetting 的 mSettingsTabs 与画面控件字段绑定；ContentModelConfig 挂 UIModelConfig；MsgboxEmptyApiKey；启用 MsgboxSaveSetting 三按钮绑定（ContentGameSettings 预留「游戏」Tab，本期不创建） |
| 协议 | `Tools/message.proto` | 无 |

---

## 4. 详细设计

### 4.1 现状盘点（Title 设置相关）

- `UITitle.cs`（v0.23.1）：
  - 持有 `mPressAnyButtonPanel` / `mMainMenuPanel` / `mSettingPanel`（`PanelSetting`）+ 4 个配置子面板 + 多个 Msgbox 引用。
  - `Update()` ESC 分发：`UIMsgBox.AnyActive` 屏蔽 → `mPressAnyButtonPanel`（任意键）→ `IsSubPanelActive()`（ESC 弹保存确认）→ `mSettingPanel`（ESC 回主菜单）→ `mMainMenuPanel`（ESC 回启动）。
  - `TryLeaveSubPanel()`：`HasConfigChanged()` 为 true → 弹 `MsgboxSaveApiKey`；false → `ShowSetting()`（返回设置页主界面 `PanelTab`）。
  - `ShowSetting()`：关闭全部子面板，打开 `PanelSetting`，`RefreshInputsFromConfig()`。
  - `SetSubPanelActive()`：打开子面板时 `SetPanelActive(mSettingPanel, false)` → **`PanelSetting` 失活**。
  - 持有 `mSaveSettingMsgBox`（`MsgboxSaveSetting`，注释「v0.23.1 起不再使用，保留供以后设置项使用」）——本期**重新启用**，用于画面设置变更确认。
- `UISetting.cs`（**挂 `PanelSetting`**）：12 个 `TMP_InputField`，`HasConfigChanged()`、`RefreshInputsFromConfig()`、`GetBase/GetKey/GetModel/SetGroup`、`OnConfirmTestConfig`（测试后保存）、`IsConfigReady` 等。
- `JsonConfigIO.cs`：`Data/Config/` 读写（`#if UNITY_EDITOR` 拆分配置根）。
- `Msgbox.prefab`（`UIMsgBox`）：1~3 按钮、ESC→Btn1、`AnyActive`。

**实际场景结构（Unity MCP 核查）**：`PanelSetting` 挂 `UISetting`，`m_IsActive: 1`；子节点 `ContentModelConfig`（`m_IsActive: 0`，含 4 个配置按钮 `BtnLLMAgent`/`BtnLLMMemory`/`BtnEmbedding`/`BtnRerank`）与 `ContentGameSettings`（`m_IsActive: 1`）。**4 个配置子面板 `PanelLLMAgent`/`PanelLLMMemory`/`PanelEmbedding`/`PanelRerank` 是 `Canvas/UI` 下与 `PanelSetting` 平级的兄弟节点**（不在 `PanelSetting` 内部）。**本次需把 4 个配置子面板移入 `PanelSetting`**（用户最终层级：与 `PanelTab` 平级互斥）。

### 4.2 需求一：`MsgboxEmptyApiKey`（配置不完整提示）

**新增引用**：`UITitle` 增加 `[SerializeField] private GameObject mEmptyApiKeyMsgbox;`（`MsgboxEmptyApiKey`，基于 `Msgbox.prefab`，UI 根节点下，与其他 Msgbox 平级），`Awake` 中 `SetActive(false)`。

**调整 `TryLeaveSubPanel()` 判定顺序**：

```csharp
private void TryLeaveSubPanel()
{
    bool changed = mSetting != null && mSetting.HasConfigChanged();
    if (!changed)
    {
        ShowSetting();
        return;
    }
    if (mSetting != null && mSetting.HasEmptyFieldInActivePanel())
    {
        ShowEmptyApiKeyMsgBox();   // 有空框 → 配置不完整提示
        return;
    }
    ShowSaveApiKeyMsgBox();        // 无空框且有变更 → 原有保存确认
}
```

**`UISetting` 新增判定**：当前激活配置 Panel 的 3 个文本框是否有空项。

```csharp
/// <summary>当前激活的配置 Panel 三个文本框是否至少有一个为空。</summary>
public bool HasEmptyFieldInActivePanel()
{
    if (IsActive(mAgentBaseInput))    return HasEmpty(mAgentBaseInput, mAgentKeyInput, mAgentModelInput);
    if (IsActive(mMemoryBaseInput))   return HasEmpty(mMemoryBaseInput, mMemoryKeyInput, mMemoryModelInput);
    if (IsActive(mEmbeddingBaseInput))return HasEmpty(mEmbeddingBaseInput, mEmbeddingKeyInput, mEmbeddingModelInput);
    if (IsActive(mRerankerBaseInput)) return HasEmpty(mRerankerBaseInput, mRerankerKeyInput, mRerankerModelInput);
    return false;
}

private static bool HasEmpty(params TMP_InputField[] inputs)
{
    foreach (var input in inputs)
    {
        if (input != null && string.IsNullOrWhiteSpace(input.text)) return true;
    }
    return false;
}
```

**`MsgboxEmptyApiKey` 按钮（场景绑定到 `UITitle` 公开方法）**：

| 按钮 | 方法 | 行为 |
|------|------|------|
| Btn1 继续配置 | `OnClickEmptyContinue` | 关弹窗，留当前 Panel（`LockInput`） |
| Btn2 退出 | `OnClickEmptyExit` | 关弹窗，返回 `PanelTab`（设置页主界面，不保存） |

> 已确认：`MsgboxEmptyApiKey` 为**两个按钮**（「继续配置」/「退出」），需求原文「三个按钮」为用户笔误。`UIMsgBox` 自动隐藏未配置的 `Btn3`。

### 4.2.1 需求三（用户新增）：`MsgboxSaveSetting`（画面设置变更确认）

**触发时机（用户确认）**：退出 `PanelSetting` 时（`PanelTab` 按 ESC，见 §4.3 导航），若画面设置（`ContentDisplaySettings` 的显示模式/分辨率）相对已保存值有变更，弹 `MsgboxSaveSetting`（复用现有 `mSaveSettingMsgBox`）。

**判定（用户确认）**：`UISetting.HasDisplaySettingsChanged()`——对比 `mModeIndex`/`mResIndex`（当前）与 `GameSettingsStore.Load()`（已保存值），不同即 dirty。不依赖 `ContentDisplaySettings` 是否激活。

**按钮（复用 `UIMsgBox` 三按钮弹窗）**：

| 按钮 | 方法 | 行为 |
|------|------|------|
| Btn1 保存并退出 | `OnClickSaveSettingAndExit` | 关弹窗 + `UISetting.SaveDisplaySettings()`（写盘）+ 返回 `PanelMenu` |
| Btn2 退出 | `OnClickExitSetting` | 关弹窗，**不保存** + 返回 `PanelMenu` |
| Btn3 取消 | `OnClickCancelSaveSetting` | 仅关弹窗，留在 `PanelSetting`（`LockInput`） |

**`UITitle` 调整**：

```csharp
private void TryLeaveSettingTab()   // PanelTab 按 ESC
{
    if (mSetting != null && mSetting.HasDisplaySettingsChanged())
    {
        ShowSaveSettingMsgBox();    // 有画面变更 → 弹确认
        return;
    }
    ShowMainMenu();                 // 无变更 → 直接返回主菜单
}

public void OnClickSaveSettingAndExit()
{
    if (mSaveSettingMsgBox != null) mSaveSettingMsgBox.SetActive(false);
    if (mSetting != null) mSetting.SaveDisplaySettings();
    ShowMainMenu();
}
public void OnClickExitSetting()      // 不保存
{
    if (mSaveSettingMsgBox != null) mSaveSettingMsgBox.SetActive(false);
    ShowMainMenu();
}
public void OnClickCancelSaveSetting() // 仅关弹窗
{
    if (mSaveSettingMsgBox != null) mSaveSettingMsgBox.SetActive(false);
    LockInput();
}
```

> **导航变化（用户确认）**：原「`PanelSetting` 按 ESC 直接返回主菜单」改为「先检测画面变更，有则弹 `MsgboxSaveSetting`」。4 个配置子面板 ESC → 返回 `PanelTab`（设置页主界面）；`PanelTab` ESC → 触发上述检测。

### 4.3 需求二：设置页多标签页（可扩展）+ 画面配置并入 `UISetting`

**核心**：Tab 列表**数据驱动**（`SettingsTabConfig` 表），Tab 切换逻辑**并入 `UISetting`**（挂在 **`PanelSetting`** 上——`PanelSetting` 作为设置页根容器，配置期间保持激活）。`PanelSetting` 内含 **`PanelTab`**（Tab 按钮容器）与 4 个配置子面板（`PanelLLMAgent` 等），**同一层级显示互斥**。模型配置数据逻辑**拆出为 `UIModelConfig`**（挂 `ContentModelConfig`）；**画面配置逻辑并入 `UISetting`**（用户明确弃独立 `UIGameSettings`，因 `ContentDisplaySettings` 会随 Tab 切换失活，不能挂其上的脚本）。`UITitle` **不参与**面板内 Tab 切换（只负责"进出设置页"页面级切换）。

> **为什么用 `PanelSetting` 作根容器而不是独立 `SettingRoot`（用户最终决策）**：`PanelSetting` 自身即可作为设置页根容器——放设置期间的背景图 + `UISetting`（唯一脚本），配置期间**保持激活**。4 个配置子面板从 `Canvas/UI` 下**移入 `PanelSetting`** 作为子节点（与 `PanelTab` 平级互斥），因此 `activeInHierarchy` 判断可靠。背景图放 `PanelSetting` 上，Panel/子面板切换时背景不闪、不重叠。
>
> **职责边界（最终）**：每个脚本挂在它**管辖的对象**上，`PanelSetting` **只挂 `UISetting` 一个脚本**：
>
> | 组件 | 挂载位置 | 职责 |
> |------|----------|------|
> | `UISetting` | `PanelSetting`（唯一脚本，根容器） | **Tab 导航**（`mSettingsTabs` + `SelectTab`）+ 跨 Tab 协调（`OnExitToSetting` 退出请求、测试回调注入）+ **画面配置**（显示模式/分辨率切换、变更检测） |
> | `UIModelConfig` | `ContentModelConfig`（模型配置 Tab） | **模型配置数据**：12 输入框读写/回填/变更检测/完整性校验/复制/测试后保存 |
> | （无） | `ContentDisplaySettings`（画面 Tab） | 仅放画面配置控件（Text / 左右箭头按钮），**不挂脚本**，由 `UISetting` 通过引用驱动 |
>
> **为什么 Tab 并入 `UISetting` 而不是独立组件**：`UISetting` 挂在 `PanelSetting`（根容器，配置期间常驻），把 Tab 切换放进来与它已有职责不冲突；且 `PanelSetting` 保持**单脚本**（避免一节点挂多脚本的杂乱）。
>
> **为什么模型配置数据拆到 `UIModelConfig`（挂 `ContentModelConfig`）**：模型配置数据（12 输入框）只属于「模型配置」这个 Tab，应跟随内容区走。`UITitle` 通过引用调用 `UIModelConfig.IsConfigReady()/HasConfigChanged()`——Unity 中 `SetActive(false)` 只停生命周期回调，**外部引用仍可调用公开方法**（这两个方法内部纯读数据、不依赖 `activeInHierarchy`），故即使切到「画面」Tab、`ContentModelConfig` 隐藏，入口校验依然有效。`HasEmptyFieldInActivePanel` 依赖 `activeInHierarchy` 判断当前激活的子面板——配置子面板移入 `PanelSetting`（根容器保持激活）后，该判断始终可靠。
>
> **为什么画面配置并入 `UISetting`（用户明确）**：原计划独立 `UIGameSettings` 挂 `ContentDisplaySettings`，但 `ContentDisplaySettings` 随 Tab 切换会 `SetActive(false)`（生命周期回调停止）。因此画面配置逻辑（显示模式/分辨率切换 + 变更检测）**并入 `UISetting`**——`UISetting` 挂 `PanelSetting`（配置期间常驻），通过引用驱动 `ContentDisplaySettings` 内的 Text / 箭头按钮，即使内容区失活也能读取/设置。变更检测用「**对比当前值与已保存值**」：`UISetting` 持有 `mModeIndex`/`mResIndex`（当前），与 `GameSettingsStore.Load()`（已保存）比较，不同即视为 dirty。

**结构**（场景侧组织，UI 根下；`ContentModelConfig` **已存在于场景**；`ContentDisplaySettings` 为用户新增；`ContentGameSettings` 本期**未添加**）：

```
PanelSetting（根容器；唯一脚本：UISetting = Tab 导航 + 协调 + 画面配置；放设置期间背景图；配置期间保持激活）
├── 背景图（PanelSetting 背景；PanelTab 与 4 个配置子面板共用，切换不闪）
├── PanelTab（Tab 按钮容器）
│   ├── TabModelConfig（「模型配置」按钮）
│   ├── TabDisplaySettings（「画面」按钮）
│   └── TabGameSettings（「游戏」按钮，预留；本期不创建）
│   ├── ContentModelConfig（已存在；挂 UIModelConfig；内含 4 个配置按钮 BtnLLMAgent/BtnLLMMemory/BtnEmbedding/BtnRerank，绑定不变）
│   ├── ContentDisplaySettings（新增：画面 Tab 内容区；显示模式行 + 分辨率行；不挂脚本）
│   └── ContentGameSettings（未添加；预留「游戏」Tab 内容区，放语言设置等）
├── PanelLLMAgent（已存在，移入 PanelSetting；挂 UILLMAgent 复制按钮）
├── PanelLLMMemory（已存在，移入 PanelSetting；挂 UILLMMemory 复制按钮）
├── PanelEmbedding（已存在，移入 PanelSetting）
└── PanelRerank（已存在，移入 PanelSetting；挂 UILLMRerank 复制按钮）
```

> **同一层级互斥**：`PanelTab` 与 4 个配置子面板（`PanelLLMAgent`/`PanelLLMMemory`/`PanelEmbedding`/`PanelRerank`）是 `PanelSetting` 的平级子节点，互斥显示（显示子面板时隐藏 `PanelTab`）；`ContentModelConfig`/`ContentDisplaySettings`/`ContentGameSettings` 是 `PanelTab` 的平级子节点，互斥显示（Tab 切换）。
>
> **导航（用户确认）**：
> - 4 个配置子面板按 ESC → 返回 **`PanelTab`**（设置页主界面，含 Tab 按钮）。
> - `PanelTab` 按 ESC → 关闭 `PanelSetting`，返回 **`PanelMenu`**（主菜单）。
> - `PanelSetting` 始终由 `UITitle` 激活/失活（进出设置页）。

**`UISetting` 内并入 Tab 切换（数据驱动）**——`PanelSetting` 唯一脚本，新增 Tab 字段与方法：

```csharp
[Serializable]
public class SettingsTabConfig
{
    public Button tabButton;       // Tab 切换按钮（场景拖拽）
    public GameObject content;     // 对应内容区
    public UITextKey titleKey;       // 文案 key（枚举下拉选择，None 不赋值；经 UITextProvider 取显示名）
}

// ===== UISetting 新增字段 =====
[Header("设置页 Tab（数据驱动，可扩展任意数量）")]
[SerializeField] private SettingsTabConfig[] mSettingsTabs;  // 顺序即 Tab 顺序
private int mCurrentTabIndex;

// ===== UISetting 新增方法 =====
private void InitSettingsTabs()
{
    mCurrentTabIndex = 0;
    for (int i = 0; i < mSettingsTabs.Length; i++)
    {
        int idx = i;   // 闭包捕获
        var tab = mSettingsTabs[i];
        if (tab.tabButton != null)
        {
            tab.tabButton.onClick.RemoveAllListeners();
            tab.tabButton.onClick.AddListener(() => SelectTab(idx));
        }
        // Tab 标题文案（若 Tab 按钮有子 Text，用 titleKey 赋值）
    }
}

/// <summary>切到指定 Tab（供 UI 内点击 / 外部复位默认调用）。</summary>
public void SelectTab(int index)
{
    if (mSettingsTabs == null || index < 0 || index >= mSettingsTabs.Length) return;
    mCurrentTabIndex = index;
    for (int i = 0; i < mSettingsTabs.Length; i++)
    {
        if (mSettingsTabs[i].content != null)
            mSettingsTabs[i].content.SetActive(i == index);
        // 选中态高亮：可改 tabButton 的 Image.color / 字号（本期可选）
    }
}

/// <summary>复位到第一个 Tab（UITitle.ShowSetting 打开设置页时调用）。</summary>
public void ResetToDefaultTab() { SelectTab(0); }
```

> **`UITitle` 只做一件事**：`ShowSetting()` 打开设置页时激活 `PanelSetting`，并调用挂在 `PanelSetting` 上的 `UISetting.ResetToDefaultTab()`（默认第一个 Tab = 模型配置），**不持有任何 Tab/Content 引用**；关闭设置页时失活 `PanelSetting`。设置页内部（`PanelTab` 与 4 个配置子面板之间）切换**由 `UISetting` 或 `UITitle` 只切内部节点**，`PanelSetting` 保持激活。**新增 Tab**：场景加一个 Tab 按钮 + 内容区，把两者放进 `UISetting` 的 `mSettingsTabs` 数组对应项即可（无需改任何代码）。

**`UIModelConfig`（拆出模型配置数据，挂 `ContentModelConfig`）**：

从 `UISetting` 拆出**模型配置数据**逻辑（原 `UISetting` 的这些成员移入）：

```csharp
/// <summary>模型配置数据组件（挂 ContentModelConfig）。只负责模型配置 Tab 的数据读写/测试，不含 Tab 导航。</summary>
public class UIModelConfig : MonoBehaviour
{
    [Header("API 配置：LLM Agent 子面板")]
    [SerializeField] private TMP_InputField mAgentBaseInput;
    [SerializeField] private TMP_InputField mAgentKeyInput;
    [SerializeField] private TMP_InputField mAgentModelInput;
    // ... Memory / Embedding / Reranker 共 12 个输入框 ...

    private ApiConfig mCurrentConfig;
    private TMP_InputField[] mAllInputs;
    private bool mTestCancelled;

    // v0.23.1 回调（由 UITitle 注入）
    public System.Action<string> OnStartApiTest { get; set; }
    public System.Action<bool, string> OnApiTestFinished { get; set; }
    public System.Action OnRequestBackToSetting { get; set; }

    void Awake()
    {
        mAllInputs = /* 12 个输入框集合 */;
        // 关闭 restoreOriginalTextOnEscape（原 UISetting 逻辑）
    }

    public bool IsConfigReady()          { LoadConfigOnce(); return mCurrentConfig != null && mCurrentConfig.IsComplete(); }
    public bool HasConfigChanged()       { /* 原实现，纯读 12 输入框 vs mCurrentConfig */ }
    public void RefreshInputsFromConfig(){ /* 原实现 */ }
    public string GetBase(string group) / GetKey / GetModel / SetGroup  // 复制按钮用
    public void OnConfirmTestConfig() / CancelApiTest() / OnConfirmSaveAfterTest()  // 测试后保存
}
```

> **`UITitle` 对模型配置的引用改为 `UIModelConfig`**（原 `mSetting`）：`IsConfigReady()` / `HasConfigChanged()` / `RefreshInputsFromConfig()` 由 `UITitle` 通过 `UIModelConfig` 引用调用（即使 `ContentModelConfig` 被 Tab 隐藏，外部引用仍可调用其公开方法）。`UILLMAgent/UILLMMemory/UILLMRerank` 复制按钮的 `mSetting` 字段也改为 `UIModelConfig`。
>
> **`OnExitToSetting` + 测试回调注入**（`OnStartApiTest`/`OnApiTestFinished`/`OnRequestBackToSetting`）**留在 `UISetting`**：它们是设置页跨 Tab 协调逻辑（退出请求、测试状态切换），不属于"模型配置数据"，由挂在 `PanelSetting` 上的 `UISetting` 统一协调（`UITitle` 注入到 `UISetting`，`UISetting` 再转发给 `UIModelConfig` 或在内部处理）。

**画面配置并入 `UISetting`（挂 `PanelSetting`，用户明确弃独立 `UIGameSettings`）**——`UISetting` 通过引用驱动 `ContentDisplaySettings` 内的控件，即使内容区随 Tab 失活也能读写：

```csharp
// ===== UISetting 新增字段：画面配置（ContentDisplaySettings 内控件，由 UISetting 引用驱动） =====
[Header("画面配置（ContentDisplaySettings 内容区）")]
[SerializeField] private TMP_Text mDisplayModeText;
[SerializeField] private Button mDisplayModeLeft;   // ◀
[SerializeField] private Button mDisplayModeRight;  // ▶
[SerializeField] private TMP_Text mResolutionText;
[SerializeField] private Button mResolutionLeft;
[SerializeField] private Button mResolutionRight;

private int mModeIndex;          // 0 窗口化 / 1 无边框 / 2 全屏
private int mResIndex;           // 预置分辨率列表下标
private bool mDisplayDirty;      // 画面设置是否被改过（对比已保存值）

private static readonly FullScreenMode[] kModes = {
    FullScreenMode.Windowed, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen,
};
private static readonly (int w, int h)[] kResolutions = {
    (1024, 768),   // 常见 4:3
    (1280, 720),
    (1920, 1080),  // 需求要求必须含 1920x1080
    (2560, 1440),
};

// 启动/打开设置页时应用已保存画面设置
public void InitDisplaySettings()
{
    (mModeIndex, mResIndex) = GameSettingsStore.Load();
    ApplyAll(save: false);   // 只应用 + 刷新，不写盘
}

public void OnModeLeft()  { mModeIndex = Mathf.Max(0, mModeIndex - 1); ApplyAll(save: true); }
public void OnModeRight() { mModeIndex = Mathf.Min(kModes.Length - 1, mModeIndex + 1); ApplyAll(save: true); }
public void OnResLeft()   { mResIndex = Mathf.Max(0, mResIndex - 1); ApplyAll(save: true); }
public void OnResRight()  { mResIndex = Mathf.Min(kResolutions.Length - 1, mResIndex + 1); ApplyAll(save: true); }

private void ApplyAll(bool save)
{
    var (w, h) = kResolutions[mResIndex];
    Screen.fullScreenMode = kModes[mModeIndex];
    Screen.SetResolution(w, h, kModes[mModeIndex]);
    if (save) { GameSettingsStore.Save(mModeIndex, mResIndex); mDisplayDirty = true; }
    RefreshDisplayUI();
}

private void RefreshDisplayUI()
{
    if (mDisplayModeText != null) mDisplayModeText.text = UITextProvider.Get("mode_" + ModeKey(mModeIndex));
    if (mResolutionText != null) mResolutionText.text = string.Format(UITextProvider.Get("resolution_format"), kResolutions[mResIndex].w, kResolutions[mResIndex].h);
    if (mDisplayModeLeft != null) mDisplayModeLeft.interactable = mModeIndex > 0;
    if (mDisplayModeRight != null) mDisplayModeRight.interactable = mModeIndex < kModes.Length - 1;
    if (mResolutionLeft != null) mResolutionLeft.interactable = mResIndex > 0;
    if (mResolutionRight != null) mResolutionRight.interactable = mResIndex < kResolutions.Length - 1;
}

private string ModeKey(int idx) => idx switch { 0 => "windowed", 1 => "borderless", _ => "fullscreen" };

/// <summary>画面设置是否有未保存变更（用户确认：对比当前值与已保存值）。</summary>
public bool HasDisplaySettingsChanged()
{
    var (savedMode, savedRes) = GameSettingsStore.Load();
    return mModeIndex != savedMode || mResIndex != savedRes;
}

/// <summary>保存当前画面设置（MsgboxSaveSetting「保存并退出」调用）。</summary>
public void SaveDisplaySettings() => GameSettingsStore.Save(mModeIndex, mResIndex);
```

> **注意**：
> - `Screen.SetResolution` 在当前帧结束后生效，切换后文本/禁用态立即刷新（`RefreshDisplayUI` 用本地 index），不依赖 `Screen.width` 立即回读。
> - **变更检测用「对比当前值与已保存值」**（用户确认）：`HasDisplaySettingsChanged()` 读 `GameSettingsStore.Load()` 与 `mModeIndex`/`mResIndex` 比较，不同即 dirty——不依赖 `ContentDisplaySettings` 是否激活。
> - **为什么并入 `UISetting` 而不是 `UIGameSettings`**（用户明确）：原计划 `UIGameSettings` 挂 `ContentDisplaySettings`，但该内容区随 Tab 切换会 `SetActive(false)`（生命周期回调停止）；并入 `UISetting`（挂 `PanelSetting`，配置期间常驻）后通过引用驱动控件，始终可靠。

### 4.4 `GameSettingsStore`（画面偏好持久化）

新增 `Assets/Scripts/IndependentAgentProject/Services/GameSettingsStore.cs`，复用 `JsonConfigIO`（`Data/Config/game_settings.json`）：

```csharp
[Serializable]
public class GameSettings
{
    public int displayModeIndex;   // 0 窗口化 / 1 无边框 / 2 全屏
    public int resolutionIndex;    // 预置分辨率列表下标
}

public static class GameSettingsStore
{
    private const string FileName = "game_settings.json";

    public static (int mode, int res) Load()
    {
        var s = JsonConfigIO.LoadJson(FileName, new GameSettings());
        return (s.displayModeIndex, s.resolutionIndex);
    }

    public static void Save(int mode, int res)
    {
        JsonConfigIO.SaveJson(FileName, new GameSettings { displayModeIndex = mode, resolutionIndex = res });
    }
}
```

> 与 `ApiConfigStore` 同为「普通偏好」（非敏感），故不加密；`game_settings.json` 不进 git（加入 `.gitignore`，可选）。

### 4.4.1 MVC 化分层（v0.23.4 内落地，用户确认）

画面配置与 API 配置统一按 **QFramework 风格 MVC** 组织（参照 `GameModel` / `GunConfigModel` / `ShootingEditor2DController` / `MaxBulletCountQuery`）：

| 层 | 画面设置 | API 配置 | 职责 |
|----|----------|----------|------|
| **Model**（纯数据） | `GameSettingsModel`：`DisplayModeIndex` / `ResolutionIndex`（`BindableProperty<int>`） | `ApiConfigModel`：12 个 `BindableProperty<string>` | **只存数据**；配置解析（从 Store 读文件）放 `OnInit`；**不含** Load/Save/IsComplete/ToConfig 等业务方法 |
| **Command**（写操作） | `ChangeDisplayModeCommand` / `ChangeResolutionCommand`（改 Model）、`SaveGameSettingsCommand`（落盘） | `SaveApiConfigCommand`（文本框收集的 `ApiConfig` → 写 Model + 落盘） | Model 的修改/落盘**一律封装为 Command**，View 不得直接改 `Value` |
| **Query**（读操作） | — | `ApiConfigReadyQuery` / `ApiConfigEmptyQuery` | 完整性/空判断外置（替代原 Model 的 `IsComplete`/`IsEmpty`） |
| **View/Controller** | `UISetting`（继承 `IndependentAgentProjectController`） | `UIModelConfig`（继承 `IndependentAgentProjectController`） | 只 `GetModel`/`SendCommand`/`SendQuery`；订阅 `BindableProperty` 事件自动刷新 UI |
| **Utility** | `GameSettingsStore`（静态） | `ApiConfigStore`（静态） | 保持为纯 I/O 工具（Model `OnInit` 与 Command 内静态调用） |

**架构注册**（`IndependentAgentProject.cs`）：`RegisterModel<IGameSettingsModel>(new GameSettingsModel())` + `RegisterModel<IApiConfigModel>(new ApiConfigModel())`。

**Controller 基类**（`ViewController/IndependentAgentProjectController.cs`，参照 `ShootingEditor2DController`）：

```csharp
public class IndependentAgentProjectController : MonoBehaviour, IController
{
    public IArchitecture GetArchitecture() => IndependentAgentProject.Instance;
}
```

**职责边界（修正 v0.23.0b 的 `UIModelConfig` 内聚）**：文本框是 **View 编辑缓冲**（用户输入态，不进 Model）；Model 存**已保存值**。`HasConfigChanged` = 文本框 vs Model 值；`RefreshInputsFromConfig` = Model 值回填文本框；「测试后保存」= `SendCommand(new SaveApiConfigCommand(CollectInputsToApiConfig()))`。`UITitle` 对 `UIModelConfig`/`UISetting` 的公开调用接口**全部保持不变**，故 `UITitle` 无需改动。

> **API Key 加密说明**：本版本 MVC 化**不改变** `api_config.json` 的格式/路径（明文 + UTF-8 + 大写键），Python 侧零感知；加密属后续版本（Unity + Python 两端协同），届时解密入口可放在 `ApiConfigModel.OnInit` 与 `ApiConfigStore` 持久化边界。

### 4.5 文案数据驱动（JSON 文件 + `UITextProvider`，为语言切换铺路）

**本期所有新增 UI 文案**（Tab 名、显示模式名、分辨率格式、`MsgboxEmptyApiKey` 提示与按钮、画面页标题等）**不硬编码在 C# 中**，统一存 JSON 文案文件，经 `UITextProvider` 按 key 读取：

**文案文件**（`Assets/Resources/UI/strings_zh_CN.json`，UTF-8，仅中文一份）：

```json
{
  "tab_model_config":     "模型配置",
  "tab_display_settings": "画面",
  "game_display_mode":    "显示模式",
  "game_resolution":      "分辨率",
  "mode_windowed":        "窗口化",
  "mode_borderless":      "无边框",
  "mode_fullscreen":      "全屏",
  "resolution_format":    "{0} x {1}",
  "msgbox_empty_api_key_title":     "配置不完整",
  "msgbox_empty_api_key_hint":      "有模型配置项为空，无法测试保存",
  "msgbox_empty_api_key_continue":  "继续配置",
  "msgbox_empty_api_key_exit":      "退出",
  "msgbox_save_setting_hint":       "画面设置已变更，是否保存？",
  "msgbox_save_setting_save_exit":  "保存并退出",
  "msgbox_save_setting_exit":       "退出",
  "msgbox_save_setting_cancel":     "取消"
}
```

**`UITextProvider`**（静态查表）：

```csharp
public static class UITextProvider
{
    private static readonly Dictionary<string, string> sTable = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        var asset = Resources.Load<TextAsset>("UI/strings_zh_CN");
        if (asset == null) return;
        // 解析 JSON 为 {key:value} 字典（JsonUtility 或手写轻量解析）
        sTable.Clear();
        // ... 填充 sTable ...
    }

    public static string Get(string key, params object[] args)
    {
        if (sTable.TryGetValue(key, out var fmt))
            return args.Length == 0 ? fmt : string.Format(fmt, args);
        return key; // 缺 key 时回退显示 key，便于排查
    }
}
```

> 说明：
> - 场景**静态**文案（按钮 Text）本期仍在 Prefab 上配置（保持现状）；**代码中动态赋值**的文案一律 `UITextProvider.Get(key)`。代码里**不出现中文字面量**、**不拼接自然语言句子**（分辨率用 `"{0} x {1}"` 格式串，显示模式用名词而非句子）。
> - 为语言切换**铺路**：未来接 Unity Localization 时把 JSON 导出为 String Table；自研方案则新增 `strings_en_US.json`，运行时按当前语言加载，**代码/场景零改动**。

---

## 5. 实现步骤

### 5.1 代码侧（Agent 完成）

1. 新增 `UIModelConfig.cs`（§4.3，挂 `ContentModelConfig`）：从 `UISetting` 拆出模型配置数据（12 输入框读写/回填/变更检测/完整性校验/复制/测试后保存），含 `HasEmptyFieldInActivePanel()`。
2. `UISetting.cs`：
   - 移除模型配置数据成员（已拆到 `UIModelConfig`）。
   - 并入数据驱动 Tab：`mSettingsTabs`（`SettingsTabConfig[]`）+ `InitSettingsTabs` / `SelectTab` / `ResetToDefaultTab`。
   - 保留跨 Tab 协调：`OnExitToSetting`、测试回调注入（`OnStartApiTest`/`OnApiTestFinished`/`OnRequestBackToSetting`），并转发给 `UIModelConfig`。
   - **并入画面配置**：`ContentDisplaySettings` 内控件引用（显示模式/分辨率 Text + 左右箭头）、`InitDisplaySettings`/`OnModeLeft`/`OnModeRight`/`OnResLeft`/`OnResRight`/`HasDisplaySettingsChanged`/`SaveDisplaySettings`（§4.3）。
3. `UITitle.cs`：
   - `mSetting` 类型改为 `UIModelConfig`（引用 `ContentModelConfig` 上的组件）；`HasEmptyFieldInActivePanel`/`IsConfigReady`/`HasConfigChanged`/`RefreshInputsFromConfig` 调用改走 `mModelConfig`。
   - 新增引用 `mEmptyApiKeyMsgbox`（`MsgboxEmptyApiKey`），`Awake` `SetActive(false)`。
   - 新增 `ShowEmptyApiKeyMsgBox()`、`OnClickEmptyContinue()`、`OnClickEmptyExit()`。
   - `TryLeaveSubPanel()` 增加空项判定（§4.2）。
   - 新增 `TryLeaveSettingTab()`（`PanelTab` 按 ESC：画面变更 → 弹 `MsgboxSaveSetting`，无变更 → `ShowMainMenu()`）+ `OnClickSaveSettingAndExit()` / `OnClickExitSetting()` / `OnClickCancelSaveSetting()`（§4.2.1）。
   - `ShowSetting()` 打开设置页时，调用 `PanelSetting` 上 `UISetting.ResetToDefaultTab()`（默认第一个 Tab）；**不持有任何 Tab/Content 引用**。
4. 新增 `GameSettingsStore.cs`（§4.4）。
5. 新增文案文件 `Assets/Resources/UI/strings_zh_CN.json` + `UITextProvider.cs`（§4.5，本版本新增文案数据驱动）。

### 5.2 场景侧（Unity 编辑器内操作，你完成）

1. 新增 `MsgboxEmptyApiKey`（基于 `Msgbox.prefab`，UI 根节点下），按钮 onClick 绑定 `UITitle.OnClickEmptyContinue` / `OnClickEmptyExit`；`UITitle.mEmptyApiKeyMsgbox` 拖拽关联。
2. **把 4 个配置子面板移入 `PanelSetting`**（`Canvas/UI` 下 → `PanelSetting` 内，与 `PanelTab` 平级互斥）：`PanelLLMAgent`/`PanelLLMMemory`/`PanelEmbedding`/`PanelRerank`。
3. `PanelSetting` 内新增 **`PanelTab`**（Tab 按钮 + 内容区容器）：
   - `PanelTab` 下新建 Tab「模型配置」「画面」（**普通 `Button`**，不手动绑 OnClick，由 `UISetting.InitSettingsTabs` 自动绑定）。
   - `ContentModelConfig`（**已存在**，含现有 4 个配置按钮）：**挂 `UIModelConfig`**，绑定不变，默认显示。
   - `ContentDisplaySettings`（**新增**：画面 Tab 内容区，不挂脚本）：搭建 显示模式行 + 分辨率行（Text + 左右箭头按钮）。
4. `UISetting`（挂 `PanelSetting`）的 `mSettingsTabs`（`SettingsTabConfig[]`）数组**按顺序**填入两项：`{tabButton=Tab模型配置, content=ContentModelConfig}`、`{tabButton=Tab画面, content=ContentDisplaySettings}`（数组长度即 Tab 数量，未来加 Tab 直接数组加项）。
5. `UISetting` 画面配置字段拖拽：`mDisplayModeText`/`mDisplayModeLeft`/`mDisplayModeRight`、`mResolutionText`/`mResolutionLeft`/`mResolutionRight` 关联到 `ContentDisplaySettings` 内对应控件。
6. `UITitle` 的 `mSetting`（现为 `UIModelConfig`）拖拽关联到 `ContentModelConfig` 上的 `UIModelConfig` 组件。
7. **启用 `MsgboxSaveSetting`**（复用现有 `mSaveSettingMsgBox`）：三按钮 onClick 绑定 `UITitle.OnClickSaveSettingAndExit`（保存并退出）/ `OnClickExitSetting`（退出）/ `OnClickCancelSaveSetting`（取消）。

> 场景 YAML 手改易错，**优先 Unity 编辑器内操作**。代码侧由 Agent 直接修改。
> 场景调整完整步骤、字段拖拽对照、验收自检、常见问题见 **`场景调整指引.md`**（本目录）。

---

## 6. 风险与回退

| 风险 | 缓解 |
|------|------|
| 空项判定误伤：`HasEmptyFieldInActivePanel` 基于 `IsActive`（`activeInHierarchy`）判断当前 Panel | 配置子面板已移入 `PanelSetting` 且 `PanelSetting`（根容器）配置期间保持激活，`activeInHierarchy` 判断始终可靠；四面板互斥保证至多命中一个 |
| `TryLeaveSubPanel` 判定顺序改变导致 SaveApiKey 行为回归 | 判定顺序显式：「无变更→返回；有空框→Empty；有变更无空框→SaveApiKey」，测试逐条验证 |
| 弹窗打开时 ESC 双重触发 | `UIMsgBox.AnyActive` 屏蔽（现状机制，`MsgboxEmptyApiKey`/`MsgboxSaveSetting` 同样挂 `UIMsgBox` 自动生效） |
| `Screen.SetResolution` 异步生效导致文本/禁用态错乱 | `RefreshDisplayUI` 用本地 index 立即刷新，不依赖 `Screen.width` 回读 |
| 分辨率列表跨显示器不支持 | 文档明确 Unity 自动取最接近支持分辨率；预置列表固定（4:3 + 1080p + 2K），最左/最右边界稳定 |
| 引入成熟 UI 包的风险 | 已决策不引入（§2.2）；画面配置并入 `UISetting` 保持单脚本，符合既有架构 |
| 文案硬编码阻碍本地化 | 新增文案收敛到 JSON 文案文件 + `UITextProvider`；避免代码拼接自然语言句子（§4.5） |
| 画面变更检测误判：`HasDisplaySettingsChanged` 对比当前值 vs 已保存值 | 直接对比 `GameSettingsStore.Load()` 与 `mModeIndex`/`mResIndex`，不依赖 `ContentDisplaySettings` 激活态；测试项 13/14 逐条验证 |
| `MsgboxSaveSetting` 与「直接返回主菜单」导航变化回归 | `TryLeaveSettingTab` 显式判定：画面变更 → 弹窗；无变更 → 直接 `ShowMainMenu`；测试项 13 逐条验证 |
| 回退方案 | 还原 `UITitle`/`UISetting`（git），删除 `UIModelConfig`/`GameSettingsStore`（git），场景移除 `PanelTab`/`ContentDisplaySettings`/新增绑定即可回现状 |

---

## 7. 测试建议（Unity 编辑器内人工验证，纯 Unity 侧）

| # | 步骤 | 期望 |
|---|------|------|
| 1 | 模型配置 Panel 中某文本框清空，按 ESC | 弹 `MsgboxEmptyApiKey`（而非 SaveApiKey） |
| 2 | `MsgboxEmptyApiKey`「继续配置」 | 关弹窗，留当前 Panel |
| 3 | `MsgboxEmptyApiKey`「退出」 | 关弹窗，返回 `PanelTab`（设置页主界面，不保存） |
| 4 | 文本框全非空且有变更，按 ESC | 仍弹 `MsgboxSaveApiKey`（不回归） |
| 5 | 文本框无变更，按 ESC | 直接返回 `PanelTab`（不弹窗，不回归） |
| 6 | 打开 `PanelSetting` | 显示多 Tab，默认第一个「模型配置」，4 个配置按钮行为不变 |
| 6a | 往 `mSettingsTabs` 数组临时加一项（如「游戏」空内容区） | 自动出现新 Tab 且可切换，验证架构可扩展（演示后可移除） |
| 7 | 切到「画面」Tab | 显示 显示模式 / 分辨率 两行 |
| 7a | 切到「画面」后退出设置页，再重新打开 `PanelSetting` | 自动复位到默认 Tab「模型配置」（`ShowSetting` → `UISetting.ResetToDefaultTab()`） |
| 8 | 显示模式左右箭头 | 在 窗口化→无边框→全屏 间切换；最左/最右箭头禁用 |
| 9 | 分辨率左右箭头 | 在预置列表升序切换；最左/最右箭头禁用；列表含 1920x1080 与 4:3 |
| 10 | 切换画面设置后重进 Title / 重启 | 保持上次选择（`game_settings.json` 生效） |
| 10a | **关键回归**：切到「画面」Tab（`ContentModelConfig` 隐藏）后返回主菜单点「新游戏」/「继续游戏」 | 仍正确弹"未配置 API"提示（验证 `UIModelConfig` 挂隐藏节点后 `IsConfigReady()` 仍可调用） |
| 11 | `MsgboxEmptyApiKey` 打开时按 ESC | 等价 Btn1（继续配置），不双重触发 |
| 12 | 反复 设置→子面板→返回→主菜单 | 状态稳定无抖动、无残留面板 |
| 13 | **MsgboxSaveSetting 触发**：切到「画面」Tab 改动显示模式/分辨率（有变更）→ 回 `PanelTab` 按 ESC | 弹 `MsgboxSaveSetting`；「保存并退出」→ 写盘并返回主菜单；「退出」→ 不保存返回主菜单；「取消」→ 仅关弹窗留在设置页 |
| 13a | **MsgboxSaveSetting 不触发**：画面设置无变更 → 回 `PanelTab` 按 ESC | 直接返回主菜单（不弹窗） |
| 14 | **导航回归**：4 个配置子面板按 ESC | 返回 `PanelTab`（设置页主界面，Tab 按钮可见） |

---

## 8. 待确认问题（需你确认后开发）

- [x] **`MsgboxEmptyApiKey` 第三按钮**：已确认**两个按钮**（「继续配置」/「退出」），需求原文「三个按钮」为用户笔误（见 PRD §7）。
- [x] **UI 架构方向**：已确认 uGUI 内职责拆分（不引入 UI 包 / 不迁 UI Toolkit）→ 本方案落地。
- [x] **Tab 架构可扩展**：已确认**数据驱动多 Tab**（`mSettingsTabs` 数组，未来任意 Tab 无需改框架代码）→ 本方案落地。
- [x] **Tab 切换归属**：已确认 Tab/Content 切换**并入 `UISetting`**（挂 `PanelSetting`），`UITitle` 只做页面级进出设置页；模型配置数据**拆为 `UIModelConfig`**（挂 `ContentModelConfig`）（用户明确：不要独立 Tab 组件、模型配置数据单独成组件）→ 本方案落地。
- [x] **`PanelSetting` 根容器 + `PanelTab` 层级**：已确认 `PanelSetting` 作为设置页根容器（背景图 + `UISetting`），内含 `PanelTab`（Tab 按钮容器）+ 4 个配置子面板（移入 `PanelSetting`），同一层级显示互斥（用户明确最终层级结构）→ 本方案落地。
- [x] **弃 `UIGameSettings`，画面配置并入 `UISetting`**：已确认不新增独立画面脚本（因 `ContentDisplaySettings` 随 Tab 失活）；画面配置逻辑并入 `UISetting`，变更检测用「对比当前值与已保存值」→ 本方案落地。
- [x] **`MsgboxSaveSetting`**：已确认退出 `PanelSetting`（`PanelTab` 按 ESC）时若画面设置有变更则弹窗确认（保存并退出 / 退出 / 取消），复用现有 `mSaveSettingMsgBox`；导航改为「4 子面板 ESC → 返回 PanelTab；PanelTab ESC → 检测画面变更后回主菜单」→ 本方案落地。
- [x] **文案存储方式**：已确认**数据驱动文件**（JSON 文案文件 + `UITextProvider`，替代硬编码）→ 本方案落地。
- [x] **画面设置持久化位置**：已确认 `Data/Config/game_settings.json`（复用 `JsonConfigIO`）→ 本方案落地。
- [x] **分辨率列表**：已确认用预置列表（含 1920x1080 + 常见 4:3，最左最低/最右最高）而非 `Screen.resolutions` 动态过滤 → 本方案落地。
- [x] **显示模式与分辨率联动**：已确认本期「同一分辨率列表，随显示模式应用」简化处理 → 本方案落地。
- [x] **语言切换本期不实现**：已确认仅调研 + 文案数据驱动铺路 → 本方案落地。
- [x] **场景调整指引**：已创建独立《场景调整指引.md》文档（本目录），供你在 Unity 编辑器内手动调整 UI（不要求你写代码）；开发结束后由 Agent 最终校准确认。

> **以上待确认项均已由用户确认（2026-08-31「都按推荐方案来即可，可以开发了」）。代码侧已实现，Unity 编译通过；待场景调整（用户）与验收。**

---

## 9. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-08-30 | 创建 PRD / solution（待确认）。调研结论：显示模式/分辨率用 Unity 原生 `Screen` API；UI 架构选 uGUI 内职责拆分（不引入 UI 包/不迁 UI Toolkit）；Tab 用数据驱动多 Tab（可扩展）；文案用 JSON 文件 + `UITextProvider` 数据驱动（为语言切换铺路）；语言切换选型建议官方 Localization 包，本期不实现。 |
| 2026-08-31 | 架构调整（最终）：`PanelSetting` 作为设置页**根容器**（背景图 + `UISetting`），内含 `PanelTab`（Tab 按钮容器）+ 4 个配置子面板（移入 `PanelSetting`，同层互斥）；Tab/Content 切换**并入 `UISetting`**；模型配置数据**拆为 `UIModelConfig`**（挂 `ContentModelConfig`）；**弃 `UIGameSettings`，画面配置并入 `UISetting`**（因 `ContentDisplaySettings` 随 Tab 失活）；**新增 `MsgboxSaveSetting`**（退出设置页时画面变更确认，导航改为「4 子面板 ESC → 返回 PanelTab；PanelTab ESC → 检测后回主菜单」）。同步更新 §1/§2/§3/§4/§5/§6/§7/§8。 |
| 2026-08-31 | 一致性修正：§4.2 需求一 `MsgboxEmptyApiKey`「退出」/§7 测试 3/5 的返回目标统一为「返回 `PanelTab`」（配置子面板 ESC 返回设置页主界面，非 `PanelSetting`）；§4.1 `TryLeaveSubPanel` 无变更描述补「返回设置页主界面 `PanelTab`」。确认 `UIModelConfig` 挂 `ContentModelConfig`（失活不影响外部引用调用）保持不变。 |
| 2026-08-31 | 代码实现完成（用户确认「都按推荐方案来，可以开发了」）：新增 `UIModelConfig.cs`（模型配置数据，挂 ContentModelConfig）、`GameSettingsStore.cs`、`UITextProvider.cs`、`strings_zh_CN.json`；重构 `UISetting.cs`（挂 PanelSetting：数据驱动 Tab `SettingsTabConfig[]` + 画面配置 + HasDisplaySettingsChanged/SaveDisplaySettings）；改 `UITitle.cs`（mModelConfig 引用、MsgboxEmptyApiKey 两按钮、TryLeaveSettingTab + MsgboxSaveSetting 三按钮、ShowSetting 复位 Tab）；改 3 个复制按钮 `mSetting` 类型。**Unity 编译通过，Console 0 错误**。待场景调整（用户）+ 验收。 |
| 2026-08-31 | 用户体验改进：`SettingsTabConfig.titleKey` 由自由字符串改为 **`UITextKey` 枚举**（Inspector 下拉选择，避免策划手填拼错）；新增枚举重载 `UITextProvider.Get(UITextKey, ...)`（枚举名 == JSON key）；删除误导性的 `mTabTitleRoot` 开关（改以 `titleKey != None` 判断是否赋值）。同步更新《场景调整指引》§2.3/§2.4。Unity 编译通过。 |
| 2026-09-01 | **画面配置 + API 配置 MVC 化**（用户确认「直接在当前版本就 MVC 化」「model 的更改封装为 command」「数据模型只存数据」「配置表解析可放 model 的 init」「参考 ShootingEditor2D 用法」）：新增纯数据 Model `GameSettingsModel` / `ApiConfigModel`（只存 `BindableProperty`，配置解析在 `OnInit`）；新增 Command `ChangeDisplayModeCommand` / `ChangeResolutionCommand` / `SaveGameSettingsCommand` / `SaveApiConfigCommand`（修改/落盘封装为 Command）；新增 Query `ApiConfigReadyQuery` / `ApiConfigEmptyQuery`（完整性/空判断外置）；新增 Controller 基类 `IndependentAgentProjectController`（`MonoBehaviour + IController`，参照 `ShootingEditor2DController`）；`IndependentAgentProject` 架构注册两个 Model。改造 `UISetting`（继承 Controller：画面设置从 `IGameSettingsModel` 取数 + `SendCommand` 修改 + `BindableProperty` 订阅自动刷新）与 `UIModelConfig`（继承 Controller：改用 `IApiConfigModel` + `SaveApiConfigCommand` + Query，文本框为 View 编辑缓冲）；`UITitle` 公开接口不变故无需改动。删除了不再需要的 `LoadApiConfigCommand`/`LoadGameSettingsCommand`（加载回归 Model `OnInit`）。**Unity 编译通过（Console 0 错误），场景组件引用完好**。`api_config.json` 格式/路径保持不变。 |
| 2026-09-01 | **打包后 bug 修复 + 默认值需求**：① 画面设置「退出」不还原：新增 `RevertGameSettingsCommand`（从 `GameSettingsStore` 读已保存值写回 Model，经 `BindableProperty` 订阅自动 `ApplyScreen + 刷新 UI`，同时还原实际显示模式/分辨率与设置面板数值）；`UISetting` 新增 `RevertDisplaySettings()`；`UITitle.OnClickExitSetting` 退出前先还原。② 默认值需求（用户确认）：显示模式/分辨率**默认全屏(`ExclusiveFullScreen`) + 1920x1080**，仅当设置文件**没有任何值**（不存在/为空/解析失败）时启用；`GameSettingsStore.Load()` 改为返回 `(bool hasValue, int mode, int res)`，无值回填默认 `(false, 2, 2)`（下标 2 = 全屏 + 1920x1080）；`GameSettingsModel.OnInit` / `RevertGameSettingsCommand` / `UISetting.HasDisplaySettingsChanged` 适配新签名。**修复 Load 判断 bug**：`JsonConfigIO.LoadJson` 在文件不存在时返回 fallback（非 null），故 `settings == null` 判断恒 false；改为 `GameSettingsStore.Load` 内直接 `File.Exists` + 内容空白校验判断「无值」。**Unity 编译通过；运行时自检验证**：无文件 `Load()`→`(false,2,2)` ✅、有文件→用文件值 ✅、Revert 后 Model 回到保存值 ✅、`kModes[2]=ExclusiveFullScreen` ✅。 |
| 2026-09-01 | **GameSettings 数据获取收敛为单 Query**（用户要求「不用过渡拆分，GameSetting 的数据获取直接放在一个 query 里」）：新增 `Query/GameSettingsQueries.cs`，含 `GameSettingsSnapshot`（只读结构，含 显示模式下标/分辨率下标/是否有变更 3 个字段，不多给）与 `GetGameSettingsQuery`（统一数据获取入口，返回快照）；`UISetting` 的 `ModeIndex`/`ResIndex`/`HasDisplaySettingsChanged()` 全部改为经 `GetGameSettingsQuery` 取值（`CurrentSettings` 快照属性），不再散落裸 `GetModel`/`GameSettingsStore` 访问，与 `ApiConfig` 侧 Query 结构对称。**Unity 编译通过；运行时自检**：Query 返回 `mode=0 res=0 hasChanged=False` ✅、改值后 `mode=1 res=1 hasChanged=True` ✅、Revert 后回到 `0/0/False` ✅。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
