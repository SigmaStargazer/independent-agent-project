# PRD — v0.22.0 Unity 工程 .cs 编码统一与编码回流防护

> 状态：已确认
> 对应需求：requirements/Unity工程cs文件编码统一与防回流.md
> 立项来源：需求池 backlog.md 条目 7
> 最后更新：2026-06-30

---

## 1. 背景与目标

### 1.1 现象与触发

Unity 工程 `Src/IndependentAgentProject/Assets/Scripts/**/*.cs` 中含中文字面量的源文件，长期由 VS2022 在中文 Windows 上以 ANSI（CP936/GBK）保存，**无 BOM**。这造成两类不一致：

- VS2022 中文环境编辑器侧自动 fallback 到 CP936 解码，**肉眼无异常**。
- Unity Inspector 与 Roslyn 编译期固定按 UTF-8 解码，遇 GBK 字节渲染为 `��` 等替换字符，并导致 `Debug.Log` 中文乱码、Inspector 字段名错乱。

2026-06-30 全量扫描 `Src/IndependentAgentProject/Assets/Scripts/**/*.cs`（133 个文件）：

| 类型 | 数量 |
|---|---|
| 纯 ASCII | 31 |
| UTF-8（含/不含 BOM） | 44 |
| ISO-8859 / GBK（无 BOM） | **58** |

### 1.2 根因复盘

- `.editorconfig` 仅约束**新建文件** charset，无法转换历史文件。
- `.gitattributes` `* text=auto` 只规范换行符，**不规范字符编码**。
- VS2022 默认行为：UTF-8 失败 → 回退系统 ANSI；保存时若文件含非 ASCII 字符且当前以 CP936 打开，会以 CP936 重新写回，**进一步把曾经 UTF-8 的内容退化为 GBK**。
- 无任何自动化检测，问题完全靠人工肉眼或运行时报错发现。

### 1.3 目标

1. **一次性修复存量**：把 58 个 GBK 文件批量转 UTF-8 无 BOM，保留 CRLF，验证 Unity 显示无替换字符。
2. **建立长期防护**：在仓库内提供编码扫描脚本 + Git pre-commit hook，让"以 GBK 重新保存"的提交在本地直接被拦截。
3. **沉淀跨项目模板**：把"编码基线五件套"（`.editorconfig` / `.gitattributes` / `.vscode/settings.json` / 检测脚本 / hook）写成可复制的初始化模板，避免下一个 Unity + Python 混合项目再次踩坑。

---

## 2. 范围

### 2.1 本期包含

- 转换 `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/**/*.cs` 中所有非 UTF-8 文件（**不含** `Src/IndependentAgentProject/Assets/Scripts/ShootingEditor2D/`，详见 2.2）。
- 新增 `Tools/check_file_encoding.py`（扫描 + 可选 `--fix` 自动转换）。
- 新增 `Tools/hooks/pre-commit`（本地 git hook，调用上述脚本对 staged 文件做检查；本期**默认启用**，详见 §4.2）。
- 新增 `DevDocs/feature-design/项目编码基线.md`，沉淀新项目初始化清单。
- 更新 `.cursor/rules/file-encoding.mdc`（仓库根 + `Src/PythonServer/`），补充检测脚本与 hook 速查段。
- 提交 `.git-blame-ignore-revs`，登记本期批量转换 commit hash。

### 2.2 本期不包含

- **不转换** `Src/IndependentAgentProject/Assets/Scripts/ShootingEditor2D/**/*.cs`（属于"遗留无关"目录，见 `AGENTS.md` §1.6；是否整体删除是独立动作）。检测脚本会通过 `--exclude` 默认跳过该目录。
- 不修改任何 `.meta` / `.prefab` / 二进制资源。
- **不改 Python 端业务文件编码**：抽样 `Src/PythonServer/**/*.py` 均为 UTF-8；本期检测脚本**默认也跳过 Python 业务源码扫描**（避免在尚未确认治理策略前误报或被 hook 拦），等下一版另行讨论。
- 不接入 CI / GitHub Action（仓库当前无 CI，先做本地 hook；接 CI 是后续事项）。

---

## 3. 用户与场景

| 角色 | 场景 | 期望结果 |
|---|---|---|
| 开发者（本人） | 在 Unity 中打开工程 / Inspector / 运行 | 中文显示无 `��` 替换字符 |
| 开发者（本人） | VS2022 修改一个 `.cs` 文件并 commit | 若文件被 IDE 误存为 GBK，pre-commit 直接报错并打印修复命令；不会污染主线 |
| 未来新项目 | 初始化一个含中文的 Unity / Python 项目 | 拷贝本仓库的"编码基线五件套"即可完成基础配置 |
| Cursor Agent | 编辑或新增 `.cs` / `.py` / `.md` | 遵守 `.cursor/rules/file-encoding.mdc`，且在不确定时知道用 `Tools/check_file_encoding.py` 自检 |

---

## 4. 功能需求

### 4.1 编码扫描与转换脚本

- 路径：`Tools/check_file_encoding.py`，基于 Python 3.8+ 标准库，无需第三方依赖。
- 默认扫描根目录：`Src/IndependentAgentProject/Assets/Scripts`、`Src/PythonServer`、`Tools`、`DevDocs`、`Doc`、仓库根的 `.md`。可通过命令行参数覆盖。
- 默认扩展名：`.cs / .py / .md / .proto / .json / .yml / .yaml / .txt`。
- 编码判定算法：先尝试 UTF-8（含 BOM 与无 BOM）→ 成功视为 OK；失败再尝试 GBK → 成功视为 GBK 嫌疑；两次都失败视为未知。
- 输出：
  - OK 文件不打印（除非 `--verbose`）。
  - GBK 嫌疑文件输出绝对路径 + 起始 200 字节预览。
  - 未知文件单列在末尾，标注「需人工确认」。
  - 末尾汇总：`OK=N GBK嫌疑=M 未知=K`，仅当 M+K > 0 时退出码非 0。
- `--fix` 模式：把 GBK 嫌疑文件以 GBK 读、UTF-8 写回，保留原换行符；未知文件永不自动改。`--fix` 必须显式传入，默认只检查。
- `--staged` 模式：从 `git diff --cached --name-only` 取文件列表，仅校验这部分；用于 pre-commit。

### 4.2 Git pre-commit hook

- 路径：`Tools/hooks/pre-commit`（bash 脚本，Windows 在 Git Bash 下可运行）。
- **默认启用**：本期实现里直接把仓库 `core.hooksPath` 设到 `Tools/hooks`。具体做法二选一（在 §3.3 落地）：
  - 方案 A：在 `Tools/` 下提供 `enable_hooks.cmd` / `enable_hooks.sh` 一行脚本（执行 `git config core.hooksPath Tools/hooks`），README / `项目编码基线.md` 引导新克隆仓库的人首次执行一次。
  - 方案 B：在仓库根放一段说明，并在 `AGENTS.md` 写明"克隆后第一步执行该命令"。
  - 不使用 git `pre-commit` 框架等外部依赖。
- 行为：对 staged 文件中匹配扩展名的项调用 `python Tools/check_file_encoding.py --staged`；任意一个非 UTF-8 即拒绝提交，并打印 `python Tools/check_file_encoding.py --fix <file>` 建议。
- 必须保持快速：仅扫描 staged 文件而非全仓。
- **扫描范围与 §4.1 一致**：仅作用于 `.cs` 等本期治理的扩展名，且默认 `--exclude` `ShootingEditor2D`、Python 业务目录等，避免误拦旧目录或未治理范围的提交。

### 4.3 项目编码基线文档

- 路径：`DevDocs/feature-design/项目编码基线.md`。
- 内容：列出五件套（`.editorconfig` / `.gitattributes` / `.vscode/settings.json` / `Tools/check_file_encoding.py` / `Tools/hooks/pre-commit`）的全文或文件引用、安装步骤、Windows 中文环境注意事项、典型故障与排查。
- 在 `AGENTS.md` 的「文档、Skills 与约定」段加一条到该文件的链接。

### 4.4 file-encoding.mdc 更新

- 仓库根 `.cursor/rules/file-encoding.mdc` 与 `Src/PythonServer/.cursor/rules/file-encoding.mdc` 都补充「如何自检 / 如何启用 hook」速查段。
- 不修改既有「禁止以 GBK 读写」约束，仅追加。

### 4.5 存量批量转换

- 用 `Tools/check_file_encoding.py --fix` 对 `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/**/*.cs` 一次性转换（不含 `ShootingEditor2D/`）。
- 独立 commit，message 形如：`chore(encoding): 批量转换 IndependentAgentProject 主工程 .cs 文件为 UTF-8`。
- commit hash 追加到仓库根新增的 `.git-blame-ignore-revs`。

---

## 5. 非功能需求

- **跨平台**：脚本在 Windows（Git Bash）/ Linux / WSL 都能跑；不依赖 `file` 命令。
- **零业务回归**：转换前后用 git diff 抽样验证——若以 GBK 解码原文得到的字符序列，与转换后以 UTF-8 解码得到的字符序列**完全一致**（即只换字节，不换语义）。
- **可观测**：脚本输出可被 grep / 后续 CI 解析。
- **可回退**：转换是独立 commit；若发现某文件转换出错可单独 revert。

---

## 6. 验收标准

- [ ] `python Tools/check_file_encoding.py` 在仓库根执行，`Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject` 范围下退出码为 0，无 GBK 嫌疑、无未知（`ShootingEditor2D/` 通过默认 `--exclude` 跳过）。
- [ ] Git Bash 抽样 `file` 命令对 `SceneObjManager.cs` / `IInteractable.cs` / `InteractionZone.cs` 报告 `UTF-8 text, with CRLF line terminators`。
- [ ] Unity 打开工程进入第一关，Inspector / Console / UI 中文无替换字符。
- [ ] 仓库 `git config core.hooksPath` 已设为 `Tools/hooks`。
- [ ] 故意把任一 `.cs` 用 VS2022「高级保存选项」改为简体中文(GB2312)保存后 `git add` + `git commit`，pre-commit hook 拒绝提交并打印修复命令。
- [ ] `DevDocs/feature-design/项目编码基线.md` 存在；`AGENTS.md` 与两份 `.cursor/rules/file-encoding.mdc` 均交叉引用。
- [ ] `.git-blame-ignore-revs` 包含本期批量转换 commit hash。

---

## 7. 待确认问题（已确认结论记录）

- [x] **`ShootingEditor2D` 旧场景的 GBK 文件是否一并转**：**否**。本期不转，统一通过 `--exclude` 默认跳过。
- [x] **pre-commit hook 是否默认启用**：**是**。详见 §4.2，提供 `enable_hooks` 脚本或文档指引，并在验收里检查 `core.hooksPath`。
- [x] **是否同时对 Python 端文件做强校验**：**否**。本期检测脚本默认跳过 Python 业务源码（避免误报与误拦），等下一版讨论后再纳入。

---

*本文档由 Cursor Agent 根据 `requirements/` 生成；确认前请勿据此改代码。*
