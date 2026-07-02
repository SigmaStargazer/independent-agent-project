# Unity 工程 .cs 源文件编码统一与防回流

> 来源：需求池 `DevDocs/需求池/backlog.md` 条目 7（"Unity 工程内 `.cs` 源文件编码不一致（GBK/UTF-8）"）于 v0.22.0 立项时抽出。
> 立项时间：2026-06-30
> 涉及面：Unity 工程清理 + 仓库基础设施 + 跨项目模板

---

## 1. 背景

Unity 工程 `Src/IndependentAgentProject/Assets/Scripts/**/*.cs` 内大量含中文字面量（注释、`Debug.Log`、UI 文案）的 `.cs` 文件，由 VS2022 中文 Windows 默认 ANSI（CP936/GBK）保存，**无 BOM**。表现：

- VS2022 编辑器：先尝试 UTF-8 → 解码失败回退 CP936，因此显示正常。
- Unity Inspector / Roslyn 编译期：固定按 UTF-8 解析，遇到 GBK 字节直接渲染为 `��`、`�����豸��Ϣ` 等替换字符。
- `.editorconfig` 中 `charset = utf-8` 只对**新建文件**生效，不会自动转换历史文件。

2026-06-30 实测扫描 `Src/IndependentAgentProject/Assets/Scripts/**/*.cs`（共 133 个文件）结果：

| 类型 | 数量 | 说明 |
|---|---|---|
| 纯 ASCII（无中文，无需关心） | 31 | 不影响 |
| UTF-8（含/不含 BOM） | 44 | 已正确 |
| **ISO-8859 / GBK（不含 BOM）** | **58** | **本期需要修复** |

GBK 文件覆盖 `IndependentAgentProject` 主工程与 `ShootingEditor2D` 旧场景两部分。

---

## 2. 需求

### 2.1 本期硬性要求

1. **统一编码**：把 `Src/IndependentAgentProject/Assets/Scripts/**/*.cs` 中所有非 UTF-8 文件批量转换为 **UTF-8 无 BOM、保留 CRLF**。
2. **零内容偏差**：转换后 VS2022 / Unity Inspector / Roslyn 三处显示均无替换字符；中文注释与字符串字面量原文一致。
3. **审 diff 友好**：编码转换 commit 独立、不混业务改动，便于 `git blame` 在 `.git-blame-ignore-revs` 中排除。
4. **范围覆盖**：`ShootingEditor2D` 旧场景虽属"遗留无关"（见 `AGENTS.md` §1.6），但同处一个 Unity 工程内、`.editorconfig` / git 视角统一，本期一并转换；后续若整体删除该目录是独立动作。

### 2.2 防回流要求（避免类似问题在本仓库与未来项目里再发生）

历史教训：`.editorconfig` 仅约束新建文件，未防止"老文件用 GBK 重新保存覆盖"；GBK 文件因 VS2022 自动 fallback 不易被肉眼发现；提交时 git 也不会报错。本期需建立**主动检测 + 准入拦截**机制：

1. **本仓库**：在 `Tools/` 下提供一个跨平台脚本，扫描指定目录的 `.cs` / `.py` / `.md` / `.proto` 等文本文件编码，对 **非 UTF-8** 文件以非零退出码报错，并打印文件清单与建议命令。
2. **本仓库**：把上述脚本接入 **Git pre-commit hook**（用户本地可选启用，不强加），拦截"以 GBK / ANSI 重新保存"的提交。CI 当前未配，可暂不接。
3. **跨项目复用**：把本仓库现有的"编码统一三件套"——`.editorconfig`、`.gitattributes`、`.vscode/settings.json`、上述检测脚本——整理成一份**项目初始化模板**（放仓库内某个固定位置即可，不必跨仓库分发），并在 `AGENTS.md` 或新建文档里清晰列出"新建一个含中文的 Unity / Python 项目时按这套配置初始化"。
4. **文档**：在 `.cursor/rules/file-encoding.mdc` 现有约束（"禁止以 GBK/GB2312 读写源文件"）基础上，补充"如何检测当前文件编码 / 如何批量转换 / 如何接入 pre-commit"的速查段，让人类与 Agent 都能照做。

### 2.3 非目标

- 不统一行结束符（保持 CRLF，遵循 `.gitattributes`）。
- 不动 `.meta` / Prefab / 任何非源代码文件。
- 不整理 `ShootingEditor2D` 是否删除（与本期解耦，仅做编码转换）。
- 不修改 Python 端 `Src/PythonServer/**/*.py`（抽样大概率都已是 UTF-8，但**检测脚本需要覆盖**；如果扫到非 UTF-8 文件，单列报告，本期不强制转换）。

---

## 3. 验收

1. 在 Windows / Linux 任一环境执行：

   ```bash
   python Tools/check_file_encoding.py
   ```

   输出 `OK: 所有文本文件均为 UTF-8` 且退出码 0。

2. 用 GitBash `file` 抽样：

   ```bash
   file Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Base/SceneObjManager.cs
   file Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/ViewController/Gameplay/SceneObj/Base/IInteractable.cs
   ```

   均报 `UTF-8 text, with CRLF line terminators`。

3. Unity 打开后 Inspector、`Debug.Log`、UI 中文文案无替换字符。

4. 启用本仓库 pre-commit hook，故意把任一 `.cs` 用 GBK 重新保存后 `git commit`，提示拦截并打印文件路径与"`Tools/check_file_encoding.py --fix <file>`"建议。

5. 一篇 `DevDocs/feature-design/`（或 `Doc/`）下的"项目编码基线"文档存在，列出 `.editorconfig` / `.gitattributes` / `.vscode/settings.json` / 检测脚本 / hook 五件套的内容与使用方式，新项目可直接拷贝。

---

## 4. 风险与备注

- `git blame` 会因整批 commit 失真：转换 commit hash 写入 `.git-blame-ignore-revs`。
- 转换脚本必须**保持 CRLF**；误转 LF 会让 `.gitattributes` 因为 `* text=auto` 自动归一化，但 Windows 上的部分编辑器仍可能因此重新触发回退保存。
- 误判风险：极少数 `.cs` 在 ASCII 上但 `file` 判成 `C++ source, ISO-8859 text`，常因含个别 0x80-0xFF 字节。脚本应先尝试 UTF-8 解码，失败再尝试 GBK 解码，**两次都失败的文件视为可疑**，本期必须人工确认而非自动转换。
