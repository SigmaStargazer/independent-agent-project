# 技术方案 — v0.23.5 UI 语言切换

> **状态**：已确认（2026-09-04 验收通过）
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-09-04

---

## 1. 方案概述

复用 v0.23.4 已建立的 **QFramework MVC + UITextProvider 文案表**基础设施，做一次「本地化」升级：

1. **数据层**：`GameSettingsModel` 新增 `Language` 字段（复用既有 MVC 链路，Command 改、Store 落盘），语言即 `game_settings.json` 的一个字段。
2. **文案层**：`UITextProvider` 从"单语言静态表"升级为"多语言表 + 运行时切换 + 刷新事件"。所有文案走 key 拉取，静态文本用 `UILocalizedText` 组件绑定，动态文本用 `UITextProvider.Get(key)` 并在语言事件里重新设置。
3. **UI 层**：设置页「游戏」Tab（场景已建好）加入 `UISetting.mSettingsTabs`；`PanelLanguage` 的左右箭头绑定语言切换 Command。
4. **文案源**：`GameData/Config/UI文案表.xlsx`（仓库根，src/ 外）→ `Tools/export_localization.py` 导出 `strings_ChineseSimplified.json` / `strings_English.json`（策划维护入口）。

**核心原则**：语言切换是一个**全局状态变更**，变更后通过事件驱动所有 UI 重新拉文案，做到"即时生效、无需重启"。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Unity | `Assets/Scripts/.../UI/UITextProvider.cs` | **改造**：多语言 + 运行时切换 + 事件 |
| Unity | `Assets/Scripts/.../UI/UILocalizedText.cs` | **新增**：静态文本绑定组件 |
| Unity | `Assets/Scripts/.../Model/GameSettingsModel.cs` | 扩展：新增 Language 字段 |
| Unity | `Assets/Scripts/.../Services/GameSettingsStore.cs` | 扩展：language 读写 |
| Unity | `Assets/Scripts/.../Command/ChangeLanguageCommand.cs` | **新增**：语言切换 Command |
| Unity | `Assets/Scripts/.../ViewController/UI/UISetting.cs` | 扩展：Tab 数组加游戏 Tab + 语言 Tab 刷新 |
| Unity | `Assets/Scripts/.../ViewController/UI/UITitle.cs` | 扩展：主菜单按钮 / 9 个 Msgbox 文本接入 |
| Unity | `Assets/Scripts/.../ViewController/UI/UIMsgBox.cs` | 扩展：支持 key 化文本 |
| Unity | `GameFlow/Core/FlowExecutor.cs` + `GameFlow/Steps/*.cs` | 改造：DisplayName / 「完成」走文案表 |
| Unity | `GameFlow/Transition/TransitionUI.cs` | 扩展：错误弹窗「好的」按钮走文案表 |
| Unity | `Assets/Scenes/Title.unity` | 场景：挂 UILocalizedText / 绑 key / 加 Tab |
| Unity | `Assets/Resources/UI/strings_English.json` | **新增**：英文文案文件 |
| Unity | `Assets/Resources/UI/strings_ChineseSimplified.json` | 扩展：补齐本次所有 key |
| Unity | Excel 导出工具 | **新增**：Editor 扩展或命令行脚本 |
| Python | — | **无** |
| 协议 | `Tools/message.proto` | **无** |

## 3. 详细设计

### 3.1 语言枚举与数据模型

**语言枚举**（`UITextProvider.cs` 内或独立文件）：

```csharp
public enum UITextLanguage
{
    ChineseSimplified = 0,   // 简体中文（默认）
    English = 1,             // English
}
```

**GameSettings 扩展**（`GameSettingsStore.cs`）：

```csharp
[Serializable]
public class GameSettings
{
    public int displayModeIndex;
    public int resolutionIndex;
    public int language;   // 0=ChineseSimplified / 1=English，默认 0
}
```

**GameSettingsModel 扩展**（`Model/GameSettingsModel.cs`）：

```csharp
public interface IGameSettingsModel : IModel
{
    BindableProperty<int> DisplayModeIndex { get; }
    BindableProperty<int> ResolutionIndex { get; }
    BindableProperty<int> Language { get; }   // 新增
}
```

`OnInit` 从 `GameSettingsStore.Load()` 读取 language（无文件默认 0=简体中文），并**同步初始化 UITextProvider 的当前语言**。

**ChangeLanguageCommand**（`Command/ChangeLanguageCommand.cs`，仿 `ChangeDisplayModeCommand`）：

```csharp
public class ChangeLanguageCommand : AbstractCommand
{
    private readonly int mDelta;   // +1 下一语言 / -1 上一语言（仅两个语言，来回切）
    protected override void OnExecute()
    {
        var model = this.GetModel<IGameSettingsModel>();
        int count = 2;   // 语言数量
        int next = ((model.Language.Value + mDelta) % count + count) % count;
        model.Language.Value = next;
    }
}
```

语言变更由 `BindableProperty.Language` 的 `RegisterOnValueChanged` 驱动：**写入 UITextProvider 当前语言 → 触发刷新事件**。

### 3.2 UITextProvider 多语言改造

核心改动：

```csharp
public static class UITextProvider
{
    private const string ResourcePrefix = "UI/strings_";
    private static Dictionary<string, string> sTable;      // 当前语言表
    private static Dictionary<string, string> sZhFallback; // 简体中文兜底表
    private static UITextLanguage sCurrent = UITextLanguage.ChineseSimplified;
    private static bool sLoaded;
    private static event Action sOnLanguageChanged;   // 刷新事件

    public static UITextLanguage Current => sCurrent;

    /// <summary>设置当前语言：加载对应文件，广播刷新事件。</summary>
    public static void SetLanguage(UITextLanguage lang) { ... }

    public static void RegisterLanguageChanged(Action cb) { ... }   // 订阅（生命周期安全由调用方管理）
    public static string Get(UITextKey key, params object[] args) { ... }
    public static string Get(string key, params object[] args) { ... }
}
```

要点：
- `SetLanguage`：加载 `strings_{lang}.json`；若英文缺 key，回退简体中文表（`sZhFallback` 始终加载）。
- `Get(key)`：当前语言表 → 简体中文表 → 返回 key 本身。
- 刷新事件在 `SetLanguage` 末尾触发，所有 UI 重新拉文案。

### 3.3 UILocalizedText 组件（静态文本绑定）

**新增** `UILocalizedText.cs`（挂在任意 Text / TMP_Text 上）：

```csharp
public class UILocalizedText : MonoBehaviour
{
    [SerializeField] private string mKey;        // 文案 key（与 Excel「全部文案」key 列一致）
    [SerializeField] private Text mText;         // 或 TMP_Text，二选一
    [SerializeField] private TMP_Text mTmpText;

    private void Awake()
    {
        if (mText == null) mText = GetComponent<Text>();
        if (mTmpText == null) mTmpText = GetComponent<TMP_Text>();
        UITextProvider.RegisterLanguageChanged(Refresh);
        Refresh();
    }
    private void OnDestroy() { UITextProvider.UnregisterLanguageChanged(Refresh); }
    private void Refresh() { /* 按 mKey 拉文案写入对应组件；mKey 为空则不动 */ }
}
```

> **key 策略（设计决策）**：采用**字符串 key**（与 Excel 一致），而非 `UITextKey` 枚举。原因：文案由策划在 Excel 维护、数量会持续增长，硬编码枚举需要每次加文案都改代码；字符串 key + Excel 校验即可满足，且 `UITextProvider.Get(string)` 已支持。
> `UITextKey` 枚举**保留**仅用于既有 `UISetting` 的 `SettingsTabConfig.titleKey`（`tab_model_config` / `tab_display_settings`），避免动 v0.23.4 既有机制；新增 Tab 的 `tab_game_settings` 也加进枚举以复用该机制。

- `mKey` 为空时不动（保持场景手动文案），与现有 UITextProvider 语义一致。
- 用于：主菜单 4 按钮、9 个 Msgbox 的按钮/主文本、TabGameSettings、TxtTitle、TxtContent、Bootstrap「好的」按钮等。

### 3.4 设置页「游戏」Tab + 语言选择

**场景已建好**（无需新建）：
- `TabGameSettings`（PanelTabs 下，Button + TMP「游戏」）
- `ContentGameSettings` → `PanelLanguage`（TxtTitle「语言」+ PanelSelect[TxtContent「简体中文」+ BtnLeft← + BtnRight→]）

**接入步骤**：
1. 在 `Title.unity` 的 `UISetting.mSettingsTabs` 数组**追加第 3 个元素**：`tabButton = TabGameSettings 的 Button`、`content = ContentGameSettings`、`titleKey = tab_game_settings`。
2. `UITextKey` 枚举新增 `tab_game_settings`。
3. `UISetting` 增加语言 Tab 的引用：`TxtContent`（语言名）、`BtnLeft`、`BtnRight`（面板里已有，需拖到 UISetting 或新组件）。
4. `BtnLeft/BtnRight` 绑定 `ChangeLanguageCommand(+1/-1)`；`Language.RegisterOnValueChanged` 回调里刷新 `TxtContent`（显示「简体中文」/「English」）+ 触发全局语言刷新。
5. `TxtTitle`（「语言」）、`TxtContent`（「简体中文」）挂 `UILocalizedText` 绑对应 key。

**语言选择刷新链路**：
```
BtnLeft/Right → ChangeLanguageCommand → Model.Language.Value 变化
 → UISetting 订阅回调：UITextProvider.SetLanguage(新语言) + 刷新 TxtContent
 → SetLanguage 触发 sOnLanguageChanged → 所有 UILocalizedText + UISetting.RefreshTabTitles + 动态文本重新拉取
```

### 3.5 静态文本接入（场景）

| 文本 | 方式 |
|------|------|
| 主菜单 开始/继续/设置/退出 | 挂 `UILocalizedText`，绑 key：`main_menu_start` / `main_menu_continue` / `main_menu_settings` / `main_menu_quit` |
| 9 个 Msgbox 主文本/按钮 | 挂 `UILocalizedText`，绑对应 key（复用/新增 `msgbox_*` key） |
| TabGameSettings「游戏」 | 挂 `UILocalizedText`，绑 `tab_game_settings`（或由 UISetting.RefreshTabTitles 统一填，二选一） |
| TxtTitle「语言」 | 挂 `UILocalizedText`，绑 `language_label` |
| TxtContent「简体中文」 | 挂 `UILocalizedText`，绑 `language_name_zh`（或按语言显示 name） |
| Bootstrap「好的」按钮 | 挂 `UILocalizedText`，绑 `btn_ok` |

> 说明：9 个 Msgbox 的按钮主文本采用「以场景实际为准」已确认文案，新增 `msgbox_*` key 与既有预置 key（`msgbox_empty_api_key_*` / `msgbox_save_setting_*`）统一口径。

### 3.6 动态文本接入（代码）

| 文本 | 位置 | 方式 |
|------|------|------|
| FlowStep.DisplayName（13 条） | `GameFlow/Steps/*.cs` | `DisplayName` 改为 `UITextProvider.Get("step_" + StepKey)`，key 表新增 13 条 |
| 完成 | `FlowExecutor.cs` | `SetProgress(1f, UITextProvider.Get("flow_done"))` |
| 模型不可用：前缀 | `UITitle.OnApiTestFinished` | `UITextProvider.Get("msg_model_unavailable_prefix") + errmsg`（errmsg 原样透传） |
| 错误弹窗主文本 | `TransitionUI.ShowError` | `e.Message` 原样（透传，不翻译） |
| 模型不可用弹窗 | `UITitle` | 主文本 key + errmsg 拼接 |

> FlowStep 的 DisplayName 是 `get` 属性，语言切换时进度条已显示过的文本不会自动刷新——但由于 Flow 一次性执行，切语言不会发生在进度条显示过程中（语言在 Title 场景设置）。**因此 DisplayName 只需在读取时按当前语言取一次即可**，无需实时刷新。

### 3.7 Excel 文案源与导出（格式已确定）

**正式策划配置源**：`GameData/Config/UI文案表.xlsx`（仓库根，`src/` 之外，策划不进工程目录）。
**样例生成脚本**：`DevDocs/v0.23.5/gen_ui_excel_sample.py`（一次性样例生成，非导出工具）。

**Excel 格式（已确定）**：

| Sheet | 用途 |
|-------|------|
| `全部文案` | 唯一文案编辑入口（策划维护），46 条 key |
| `使用说明` | 编辑 / 占位符 / 换行 / 导出规则说明 |

`全部文案` 列结构（表头固定，冻结首行 + 筛选）：

| 列 | 说明 |
|----|------|
| `序号` | 行号（策划维护，插入行时保持连续） |
| `key` | 文案唯一标识（勿改，代码/场景靠它引用） |
| `模块` | 所属 UI 位置（可按模块筛选，可自定义） |
| `简体中文` | 简体中文（**基准语言，必填**） |
| `English` | 英文文案 |
| `备注` | 用途 / 引用位置（可自定义） |

**编辑规则**：
- 占位符：`{0}` `{1}`（运行时被数值/变量替换；当前文案表无占位符 key，如未来需要可加）。
- 换行：`\n`（单元格内写反斜杠 n 两个字符）。
- 缺 key 回退：英文漏填回退简体中文，简体中文漏填回退 key 本身。
- 共用按钮走同一 key（如 `btn_cancel` 多个弹窗复用）。

> **已移除（用户 2026-09-03）**：`resolution_format`（`{0} x {1}`）**不纳入语言表**——分辨率格式是通用数字格式，中英文一致，无需切换。它在代码里保持现有写法（`UISetting` 仍用 `UITextProvider.Get("resolution_format")`，但该 key 不进语言表，英文文件不包含它）。

**导出流程**：
- 运行 `Tools\export_localization.cmd`（一键，自动选用 `Src/PythonServer/.venv` 的 Python，缺 openpyxl 自动 `uv sync`）或 `python Tools/export_localization.py`：读取 `GameData/Config/UI文案表.xlsx` 的 `全部文案` sheet，按语言列生成 `strings_ChineseSimplified.json` / `strings_English.json` 到 `Assets/Resources/UI/`。
- **已交付**（2026-09-04）：命令行脚本 `Tools/export_localization.py`（仓库根 `Tools/`，不在 `src/`）+ 一键入口 `Tools/export_localization.cmd`。`openpyxl` 已加入 `Src/PythonServer/pyproject.toml` 依赖，协作者 `uv sync` 一次即可。不在 Excel 里的 key（如 `resolution_format`）会从现有 json 合并保留，英文留空则省略该 key（运行时回退中文）。

## 4. 实现步骤

1. `UITextLanguage` 枚举新增；`UITextKey` 枚举仅补 `tab_game_settings`（其余文案走字符串 key，见 3.3 key 策略）。
2. `UITextProvider` 多语言改造（SetLanguage / 回退 / 事件）。
3. `UILocalizedText` 组件新增。
4. `GameSettings` / `GameSettingsModel` / `GameSettingsStore` 加 `language` 字段。
5. `ChangeLanguageCommand` 新增。
6. `UISetting`：语言 Tab 引用 + 订阅 Language 回调 + 刷新 TxtContent/RefreshTabTitles。
7. `UITitle`：主菜单 / Msgbox 文本接入（UILocalizedText 或代码）。
8. `UIMsgBox`：支持 key 化文本（可选：直接场景挂 UILocalizedText 即可，脚本可不改）。
9. FlowStep.DisplayName 13 条 + `FlowExecutor` 完成态走文案表。
10. `TransitionUI`「好的」按钮接入。
11. `strings_ChineseSimplified.json` 补齐 + `strings_English.json` 新增。
12. Title.unity 场景：挂组件 / 绑 key / 加 Tab / 绑箭头。
13. `GameData/Config/UI文案表.xlsx` 正式落地 + `Tools/export_localization.py` 导出工具（已交付）。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| 场景挂 UILocalizedText 改动量大（Title/Bootstrap 多处） | 用脚本批量挂载工具或 Editor 脚本；一次性改动，可版本管理回退 |
| 英文缺 key 导致空文本 | Get 三级回退（英文→简体中文→key 本身）兜底 |
| Tab 标题刷新遗漏 | UISetting 在语言事件里统一 RefreshTabTitles |
| Msgbox 主文本被代码覆盖（模型不可用） | 明确该弹窗走代码拼接 key，场景静态文本仅作底稿 |
| game_settings.json 旧文件无 language 字段 | Load 时容错默认 0（简体中文），`LoadJson` fallback 已覆盖 |
| Excel 导出流程未就绪 | 先手工维护 JSON（Agent 生成），Excel 导出作为可选增强 |

## 6. 测试建议

- **纯 Unity 自测**（不依赖 Python）：
  - 设置页「游戏」Tab 出现、可点击、切换内容区。
  - 左右箭头切换 简体中文/English，TxtContent 显示「简体中文」/「English」。
  - 切 English 后 Title 主菜单 4 按钮变英文。
  - 切 English 后各 Msgbox 主文本/按钮变英文。
  - 切 English 后设置页 Tab/显示模式/分辨率变英文。
  - Bootstrap FlowStep.DisplayName 显示英文；完成态显示英文「Done」。
  - Bootstrap 出错弹窗「好的」变英文，errmsg 原样透传。
  - game_settings.json 落盘 language 字段；重启保持。
  - 英文缺 key 回退简体中文不空白。
- **需 Unity 联调**：无（本版本不涉及 Python 协议）。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-09-04 | 功能开发完成：UITextProvider 多语言、UILocalizedText、ChangeLanguageCommand、UISetting 游戏 Tab、FlowStep.DisplayName 文案化、Title/Bootstrap 场景挂载。 |
| 2026-09-04 | 文案源落地：`GameData/Config/UI文案表.xlsx`（59 行）；导出工具 `Tools/export_localization.py` + 一键 `Tools/export_localization.cmd`；openpyxl 加入 PythonServer 依赖。 |
| 2026-09-04 | 验收通过（v0.23.5）：导出 60/59 keys、幂等、resolution_format 保留、Excel 内容完整、文档路径一致；运行期（Unity）由用户实测通过。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
