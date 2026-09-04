# UI 文案配置指南（v0.23.5 多语言）

本指南说明本项目 UI 文案的完整配置链路：**策划在 Excel 里改文案 → 导出工具生成运行时 json → Unity 组件/代码按 key 取文案**，以及美术重做 UI 时如何把文案重新挂回去。

---

## 一、配置链路总览

```
                    ┌────────────────────────────────────────────┐
                    │  策划唯一编辑入口（工程外，不进 src/）          │
                    │  GameData/Config/UI文案表.xlsx               │
                    └─────────────────────┬──────────────────────┘
                                          │ Tools\export_localization.cmd
                                          │（自动用 PythonServer .venv / uv sync）
                                          ▼
   ┌──────────────────────────────────────────────────────────────┐
   │  运行时资源（Unity 直接读取，勿手改）                            │
   │  Src/IndependentAgentProject/Assets/Resources/UI/            │
   │    strings_ChineseSimplified.json   （简体中文）               │
   │    strings_English.json             （English）               │
   └──────────────────────────────────────────────────────────────┘
                                          │
                                          ▼
                    ┌────────────────────────────────────────────┐
                    │  UITextProvider（全局单例）                    │
                    │  Get(key, args) 三档回退：                    │
                    │    当前语言表 → 简体中文表 → key 本身          │
                    └─────────────────────┬──────────────────────┘
                                          │ 语言切换事件 sOnLanguageChanged
                 ┌────────────────────────┴─────────────────────────┐
                 ▼                                                  ▼
   ┌──────────────────────────┐                       ┌──────────────────────────┐
   │  UILocalizedText 组件      │                       │  代码动态 Get()            │
   │  （挂 Text/TMP 上，自动刷新）│                       │  错误提示 / 带占位符文案    │
   └──────────────────────────┘                       └──────────────────────────┘
```

**关键点**：
- Excel 是**唯一文案编辑入口**，放在仓库根 `GameData/Config/`（`src/` 之外），策划不会碰工程文件。
- 导出的 json 是**运行时的产物**，不要手改；改文案请走 Excel + 导出。
- 组件挂载与代码取词都只认 **key**，文案内容在 Excel 里改，改了导出即生效，组件无需改动。

---

## 二、三种文本类型的配置方式

| 类型 | 适用场景 | 配置方式 |
|------|----------|----------|
| **静态文本** | 标题、按钮、标签等固定文案 | 挂 `UILocalizedText` 组件，填 `mKey` |
| **动态文本（代码引用）** | 错误提示、带占位符的拼接文案 | 代码里 `UITextProvider.Get(key, args)` |
| **动态文本（模板按钮）** | 如「从 X 复制」这类随上下文变化 | 面板基类 `UILLMCopyPanelBase` + `UITextProvider.Get` 格式化 |

### 2.1 静态文本 → UILocalizedText 组件

`UILocalizedText` 挂在 Text / Text (TMP) 物体上，监听 `UITextProvider.sOnLanguageChanged`，语言切换时自动刷新。

**配置步骤**：
1. 选中目标 Text 物体 → `Add Component` → `UILocalizedText`。
2. Inspector 中 `Key` 字段填文案 key（与 Excel 第 2 列一致）。
3. 保存场景。

> Unity 编辑器：可用菜单/工具批量给场景文本挂组件填 key（本项目开发期用 Unity MCP `execute_code` 批量挂载）。

**美术重做 UI 时如何恢复**（这是把文案挂到新物体上的标准流程）：
- 新 Text 物体建立后，**同样挂 `UILocalizedText` 组件、填同一个 key** 即可，文案自动跟随语言。
- 挂载清单见文末 §六，可据此逐条核对，防止重做后漏挂。
- 注意：`UILocalizedText` 是普通运行时组件，**不依赖物体名字/层级路径**，只认物体上的 Text 组件 + key，因此物体重命名/移动层级都不影响。

### 2.2 动态文本（错误提示等）→ 代码 UITextProvider.Get

动态文案（如 API 错误、保存确认提示）在代码里取词：

```csharp
// 带占位符：Excel 里写 "当前模型不可用：{0}"，{0} 传入错误原文
string msg = UITextProvider.Get("msgbox_model_available_hint", errmsg);
mMsgBox.SetText(msg);   // UIMsgBox.SetText 动态设置
```

- 代码持有 Text 组件引用（如 `UIMsgBox.mWarningTxt`），`SetText` 动态写入。
- **errmsg 原样透传**，只有前缀文案走 key 本地化（v0.23.5 约定）。

### 2.3 动态模板按钮（如「从 X 复制」）→ UILLMCopyPanelBase

「从 X 复制」文案由「固定前缀 + 来源面板标题」拼接，语言切换时两部分都要刷新。封装在基类 `UILLMCopyPanelBase`：

- 子类 `UILLMAgent` / `UILLMMemory` / `UILLMRerank` 各自实现 `SourceTitleKey`（来源面板的标题 key）。
- 基类在 `Awake` 注册语言事件，切语言时用 `UITextProvider.Get("config_copy_from", title)` 重新拼接 `BtnCopy/Text (TMP)`。
- Excel 中 `config_copy_from` 值为 `从 {0} 复制` / `Copy from {0}`。

---

## 三、新增一条文案的标准流程

1. **Excel 加行**（`GameData/Config/UI文案表.xlsx`「全部文案」表）：
   - 第 1 列 `序号`（手动递增）；第 2 列 `key`（唯一、英文蛇形命名，如 `main_menu_continue`）；第 3 列 `模块`；第 4 列 `简体中文`（**禁止留空**）；第 5 列 `English`（留空 = 运行时回退中文）；第 6 列 `备注`。
2. **导出**（二选一）：
   ```bash
   # 方式一：一键运行（推荐，Windows）——自动选用 PythonServer 的 uv 虚拟环境
   Tools\export_localization.cmd

   # 方式二：命令行直接调 Python（需先 uv sync 装好 openpyxl）
   cd Src/PythonServer && uv sync
   cd ../.. && python Tools/export_localization.py
   ```
   生成两个 json。
3. **挂载**：
   - 静态 → 给 Text 物体挂 `UILocalizedText`，Key 填新 key。
   - 动态 → 代码里 `UITextProvider.Get(newKey, args)`。
4. **验证**：切到 English 确认显示正确，简体中文不空。

**导出校验**：key 重复 / 中文为空时导出会报错中止，防止漏配进游戏。

---

## 四、导出工具细节

- **工具**：
  - `Tools/export_localization.cmd`（一键运行入口，推荐；双击或在仓库根执行；成功后停留等待确认，按任意键关闭）
  - `Tools/export_localization.py`（底层 Python 脚本）
  - 均位于仓库根 `Tools/`，不属于 `src/`。
- **运行环境**：优先使用 `Src/PythonServer/.venv`（uv 虚拟环境，多人协作统一依赖）。首次运行若 venv 缺 `openpyxl`，`.cmd` 会自动 `uv sync` 同步；也支持系统 `py`/`python` 兜底。
- **依赖**：`openpyxl`（已加入 `Src/PythonServer/pyproject.toml` 的 `dependencies`，协作者 `cd Src/PythonServer && uv sync` 一次即得）。
- **自定义 Excel 路径**：`Tools\export_localization.cmd --excel <path>`。
- **保留额外 key**：不在 Excel 里的 key（如代码内部使用的 `resolution_format`）会从现有 json 合并保留，导出不会冲掉运行时依赖的附加 key。
- **英文为空省略**：English json 中省略空英文 key，运行时回退中文；**不能填空串**（空串会被当作有效值命中而显示空白）。

---

## 五、语言切换如何生效

| 环节 | 作用 |
|------|------|
| `GameSettingsModel.Language` | 当前语言索引（`BindableProperty<int>`） |
| `UITextProvider.SetLanguage(lang)` | 切表 + 发 `sOnLanguageChanged` 事件 |
| `UILocalizedText` | 监听事件刷新自身文本 |
| 代码里注册 `UITextProvider.RegisterLanguageChanged(handler)` | 动态文案刷新（如复制按钮） |

切换入口：`UISetting` 的「游戏」Tab（`TabGameSettings`），左右箭头切换语言（最左/最右箭头自动禁用），ESC 弹出保存确认（语言变更纳入「设置变更」判定）。

---

## 六、已挂载 UILocalizedText 节点清单（Title 场景，v0.23.5）

> 美术重做 UI 后，按此清单核对文案是否恢复挂载。路径为场景层级中的关键节点，key 即 Excel 第 2 列。

### 6.1 主菜单（MainMenu / PanelMain）
| 节点 | key |
|------|-----|
| MainMenu 开始按钮 `Text (Legacy)` | `main_menu_start` |
| MainMenu 继续按钮 `Text (Legacy)` | `main_menu_continue` |
| MainMenu 设置按钮 `Text (Legacy)` | `main_menu_settings` |
| MainMenu 退出按钮 `Text (Legacy)` | `main_menu_quit` |

### 6.2 设置面板（UISetting / PanelSetting）
| 节点 | key |
|------|-----|
| 顶部「打开设置」`Text (TMP)`（多处，如 Content 内） | `open_settings` |
| Tab「游戏」标题 | `tab_game_settings`（按钮文本，见 UISetting 代码） |
| 语言行标签 `TxtTitle` | `language_label` |
| 语言名称显示 `mLanguageNameText` | `language_name_zh_cn` / `language_name_en_us` |
| 显示模式标题 `TxtTitle` | `display_mode_title` |
| 分辨率标题 `TxtTitle` | `resolution_title` |

### 6.3 模型配置面板（ContentModelConfig 四个按钮）
| 节点 | key |
|------|-----|
| `BtnLLMAgent` 标题 `TxtTitle` | `btn_llm_agent_title` |
| `BtnLLMMemory` 标题 `TxtTitle` | `btn_llm_memory_title` |
| `BtnEmbedding` 标题 `TxtTitle` | `btn_embedding_title` |
| `BtnRerank` 标题 `TxtTitle` | `btn_rerank_title` |

### 6.4 模型配置子面板（PanelLLMAgent / PanelLLMMemory / PanelEmbedding / PanelRerank）
每个子面板内（Provider / ApiKey / Model 三组，各面板相同结构）：
| 节点 | key |
|------|-----|
| 输入框上方标签 `Text (TMP)` | `config_field_provider` / `config_field_api_key` / `config_field_model_name` |
| 输入框占位符 `Placeholder` | `input_placeholder` |
| `BtnCopy/Text (TMP)` | 动态模板：`config_copy_from` + 来源标题 key（由 `UILLMCopyPanelBase` 拼接） |

### 6.5 消息框（Msgbox，Title 场景）
| 节点 | key |
|------|-----|
| 各 Msgbox 标题（如 `MsgboxSaveSetting` 标题） | `msgbox_save_setting_title` 等 |
| 各 Msgbox 按钮（确定/取消/退出等） | `btn_ok` / `btn_cancel` / `btn_exit` / `btn_configure` / `btn_save_exit` / `btn_continue_config` / `btn_exit_no_save` |
| 各 Msgbox 正文 | `msgbox_new_game_hint` / `msgbox_confirm_save_hint` / `msgbox_quit_hint` / `msgbox_model_available_hint` / `msgbox_model_testing_hint` / `msgbox_empty_api_key_hint` / `msgbox_no_api_key_hint` / `msgbox_save_api_key_title` |

### 6.6 其它
| 节点 | key |
|------|-----|
| `PanelPressAnyButton/Text (TMP)` | `press_any_button` |

---

## 七、动态文本（非 Excel 静态）的 key

以下 key 由**代码**调用（Excel 里也有对应行，但挂在代码路径）：
- `resolution_format`：`{0} x {1}`，分辨率下拉显示用，代码格式化（Excel 之外保留的附加 key 示例）。

---

## 八、注意事项

1. **改文案只动 Excel**，导出后 json 自动更新；**禁止手改 json**。
2. **中文列禁止留空**；英文可留空（回退中文），但不要填空串。
3. key 命名：`模块_含义` 的英文蛇形，全局唯一。
4. 换行：Excel 单元格里用字面 `\n`（导出时转成 json 的 `\n`）。
5. 占位符：`{0}`、`{1}` 对应 `UITextProvider.Get(key, arg0, arg1)`。
