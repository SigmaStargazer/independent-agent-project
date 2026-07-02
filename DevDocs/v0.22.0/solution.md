# 技术方案 — v0.22.0 Unity 工程 .cs 编码统一与编码回流防护

> 状态：已实现
> 依据 PRD：PRD.md
> 最后更新：2026-06-30

---

## 1. 方案概述

分三步：(1) 写一个**纯标准库 Python 检测/修复脚本** `Tools/check_file_encoding.py`，用「先 UTF-8 后 GBK 再标记未知」三段式判定；(2) 用该脚本 `--fix` 一次性把 58 个 GBK 文件批量转 UTF-8 无 BOM 保留 CRLF，单独 commit；(3) 提供一个**可选启用**的 pre-commit hook 与一份「项目编码基线」文档，把检测能力沉淀为长期防护与新项目模板。

## 2. 影响范围

| 层级 | 模块 / 路径 | 变更类型 |
|------|-------------|----------|
| 工具 | `Tools/check_file_encoding.py` | 新增 |
| 工具 | `Tools/hooks/pre-commit` | 新增 |
| Unity 工程 | `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/**/*.cs`（GBK 文件） | 文件编码替换（内容字符序列不变） |
| Unity 工程 | `Src/IndependentAgentProject/Assets/Scripts/ShootingEditor2D/**/*.cs` | **本期不动**（默认 `--exclude` 跳过） |
| 文档 | `DevDocs/feature-design/项目编码基线.md` | 新增 |
| 文档 | `AGENTS.md` | 在「文档、Skills 与约定」段加链接 |
| 规则 | `.cursor/rules/file-encoding.mdc`（仓库根 + `Src/PythonServer/`） | 追加「自检 / hook 启用」段 |
| 仓库基础设施 | `.git-blame-ignore-revs` | 新增，登记本期 commit hash |
| 协议 / Python 业务 / Unity 业务 | — | 无 |

## 3. 详细设计

### 3.1 编码检测脚本 `Tools/check_file_encoding.py`

**判定流程**：

```text
读取 bytes
  ├─ 去 BOM（若以 b"\xef\xbb\xbf" 开头视为 utf-8-sig）
  ├─ 尝试 bytes.decode("utf-8", "strict")
  │     成功 → OK，记录 utf-8 / utf-8-sig
  │     失败 → 进入下一步
  ├─ 尝试 bytes.decode("gbk", "strict")
  │     成功 → GBK 嫌疑，附 200 字节预览（UTF-8 替换形式）+ 文件大小
  │     失败 → 未知，附 200 字节 hex
  └─ 汇总
```

**为什么不依赖 chardet**：

- 标准库即可完成；零依赖跨平台。
- 我们只关心「是不是 UTF-8」与「是不是 GBK」两个具体决策，无需通用编码检测。
- chardet 在 GBK / Big5 / SHIFT_JIS 上的判定置信度不高，反而引入不确定性。

**命令行接口**：

```text
python Tools/check_file_encoding.py [PATH ...] \
    [--ext .cs,.py,.md,.proto,.json,.yml,.yaml,.txt] \
    [--exclude DIR ...] \
    [--fix] \
    [--staged] \
    [--verbose]
```

- 默认 `PATH = ['Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject', 'Tools', 'DevDocs', 'Doc', 'AGENTS.md', 'README.md']`（仅本期治理范围；Python 业务目录 `Src/PythonServer` 与旧场景 `Src/IndependentAgentProject/Assets/Scripts/ShootingEditor2D` 默认**不**纳入）。
- 默认 `--ext`：`.cs,.md,.proto,.json,.yml,.yaml,.txt`（本期默认 **不含 `.py`**，对应 PRD §2.2 "Python 端暂不强校验"）。
- 默认 `--exclude`：`.git`、`Src/IndependentAgentProject/Library`、`Src/IndependentAgentProject/Temp`、`Src/IndependentAgentProject/obj`、`Src/IndependentAgentProject/Build`、`Src/IndependentAgentProject/Assets/Scripts/ShootingEditor2D`、`Src/PythonServer/.venv`、`Src/PythonServer/db`、`Src/PythonServer/logs`。
- `--fix`：仅对「GBK 嫌疑」做 `bytes_gbk → str → bytes_utf8`，写回原文件；**保留原换行符**（即不主动换 CRLF↔LF）；不输出 BOM。
- `--staged`：从 `git diff --cached --name-only` 取候选，再用本脚本同样的 `--ext` / `--exclude` 规则过滤一次（旧场景与 Python 业务目录中的文件被 hook 默认放行），命中后逐个校验，不接 `--fix`。
- 退出码：扫描结束后若存在「GBK 嫌疑」或「未知」非零退出；`--fix` 成功修复并复扫通过后退出码 0。
- 输出格式：每行 `[<状态>] <相对路径> (<size> bytes)`，状态枚举 `OK | UTF8-BOM | GBK | UNKNOWN`。

**「未知」永不自动改**的理由：转换工具必须可逆 + 安全；若文件既不是 UTF-8 也不是 GBK，可能是 UTF-16 / 二进制误命中 / 损坏，让人决定。

### 3.2 批量转换执行（一次性）

执行序列（开发者本地）：

```bash
# 1) 干跑：列出 IndependentAgentProject 主工程的 GBK 文件
python Tools/check_file_encoding.py \
    Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject \
    --ext .cs --verbose

# 2) 修复
python Tools/check_file_encoding.py \
    Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject \
    --ext .cs --fix

# 3) 复扫确认退出码 0
python Tools/check_file_encoding.py \
    Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject \
    --ext .cs

git add Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject
git commit -m "chore(encoding): 批量转换 IndependentAgentProject 主工程 .cs 文件为 UTF-8（无 BOM, CRLF 保留）"
git rev-parse HEAD >> .git-blame-ignore-revs
git add .git-blame-ignore-revs
git commit -m "chore: 登记 v0.22.0 编码转换 commit 到 .git-blame-ignore-revs"
```

校验：

- `git show <hash> --stat` 应只列 `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/**/*.cs`，不含 `ShootingEditor2D/` 与业务变更。
- 抽样 3 个文件 `git show <hash>:<path> | iconv -f UTF-8 -t UTF-8` 不报错。
- Unity 重新打开工程后进第一关，Console 中文不乱码。

### 3.3 Git pre-commit hook `Tools/hooks/pre-commit`

脚本本体：

```bash
#!/usr/bin/env bash
set -e
ROOT="$(git rev-parse --show-toplevel)"
python "$ROOT/Tools/check_file_encoding.py" --staged
```

不再传 `--ext`，让脚本默认值与本期治理范围保持一致（旧场景与 Python 业务目录自动放行）。

**默认启用机制**（PRD §4.2）采用方案 A，提供两个一行启用脚本：

`Tools/enable_hooks.sh`：

```bash
#!/usr/bin/env bash
git config core.hooksPath Tools/hooks
chmod +x Tools/hooks/pre-commit
echo "[OK] core.hooksPath -> Tools/hooks"
```

`Tools/enable_hooks.cmd`：

```bat
@echo off
git config core.hooksPath Tools/hooks
echo [OK] core.hooksPath -> Tools/hooks
```

并在仓库根 `README.md` 与 `DevDocs/feature-design/项目编码基线.md` 中**显著标注**：克隆仓库后第一步执行 `Tools/enable_hooks.sh`（Linux/macOS/Git Bash）或 `Tools\enable_hooks.cmd`（Windows）。Git 本身不支持"克隆后自动设 hooksPath"，本期就采用「文档强引导 + 一行脚本」组合作为默认启用方案。

本期开发者（本人）本地必须执行一次该脚本，作为验收前置条件（PRD §6 中"`core.hooksPath` 已设为 `Tools/hooks`"）。

### 3.4 项目编码基线文档

`DevDocs/feature-design/项目编码基线.md` 结构：

1. 适用范围（含中文文案的 Unity / Python / 协议混合项目）。
2. 五件套清单及引用路径：
   - `.editorconfig`
   - `.gitattributes`
   - `.vscode/settings.json`
   - `Tools/check_file_encoding.py`
   - `Tools/hooks/pre-commit` + `Tools/enable_hooks.{sh,cmd}`
3. 「新项目初始化」步骤：
   1. 拷贝五件套到新仓库；
   2. 在新仓库根执行一次 `Tools/enable_hooks.sh` 或 `Tools\enable_hooks.cmd`；
   3. 运行一次 `python Tools/check_file_encoding.py` 复扫；
   4. 根据需要在新项目里调整脚本默认 `PATH` / `--ext` / `--exclude`（例如该新项目可能要把 Python 业务源码纳入扫描）。
4. Windows 中文环境必读：VS2022 默认编码、CP936 fallback 行为、如何在 VS2022 中固定 UTF-8 无 BOM 保存。
5. 故障排查：Unity Inspector 出现 `��` / Roslyn 编译报字符串截断 / 工具脚本判为 UNKNOWN 等。
6. 与本仓库 v0.22.0 的差异：本仓库 Python 端暂未纳入治理（解释原因），新项目可在 §3.1 默认值上自行扩展。

### 3.5 file-encoding.mdc 追加段

在仓库根 `.cursor/rules/file-encoding.mdc` 与 `Src/PythonServer/.cursor/rules/file-encoding.mdc` 末尾追加：

```text
## 自检与拦截

- 本地自检：`python Tools/check_file_encoding.py`
- 启用本地 pre-commit 拦截（推荐）：`git config core.hooksPath Tools/hooks`
- 批量修复 GBK 嫌疑文件：`python Tools/check_file_encoding.py --fix <path>`
- 完整说明：`DevDocs/feature-design/项目编码基线.md`
```

### 3.6 `.git-blame-ignore-revs`

仓库根新增此文件；GitHub / 命令行 `git blame --ignore-revs-file=.git-blame-ignore-revs` 可跳过批量编码 commit。文件内容：

```text
# v0.22.0: 批量把 GBK .cs 转为 UTF-8（仅字节变化，无内容变更）
<commit-hash-placeholder>
```

## 4. 实现步骤

1. 写脚本 `Tools/check_file_encoding.py`，本地用 `pytest`-free 小用例自测：构造 GBK / UTF-8 / UTF-8 BOM / UTF-16 / 二进制 5 个临时文件，验证脚本判定与 `--fix` 行为；额外测试 `--staged` 与 `--exclude` 的默认值。
2. 跑 `python Tools/check_file_encoding.py Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject --ext .cs --verbose`，复核「GBK 嫌疑」清单（与 2026-06-30 的 58 个扫描结果做差异比对，应基本一致，差异需逐项确认）。
3. 跑 `--fix`，复扫确认退出码 0。
4. Unity 打开工程跑一遍第一关，确认中文显示正常。
5. 提交转换 commit（仅含 `IndependentAgentProject` 主工程）。
6. 写 `Tools/hooks/pre-commit`、`Tools/enable_hooks.sh`、`Tools/enable_hooks.cmd`。
7. 本地执行 `Tools/enable_hooks.sh`（或 `.cmd`），并用一个故意以 GBK 保存的临时 `.cs` 验证拦截；再用一个 `ShootingEditor2D/` 内的 GBK 文件 staged 验证**被默认放行**（属于本期不纳入范围）。
8. 写 `DevDocs/feature-design/项目编码基线.md`。
9. 追加两份 `.cursor/rules/file-encoding.mdc` 的「自检与拦截」段。
10. 新增 `.git-blame-ignore-revs`，登记第 5 步 commit。
11. 在仓库根 `README.md` 加一段「克隆后第一步执行 `Tools/enable_hooks`」说明。
12. 更新 `AGENTS.md` §八 的文档索引（指向 `项目编码基线.md`）。
13. 回到 `DevDocs/需求池/backlog.md`，把条目 7 的「状态」改成「已立项 v0.22.0」（**已完成**，无需重复）。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `--fix` 把误判为 GBK 的文件破坏 | 三段式判定严格：UTF-8 decode 失败才进 GBK；GBK decode 失败标 UNKNOWN 不动；批量转换单独 commit 便于 revert |
| Unity 在 Windows 上重新打开工程时把 CRLF 换 LF / 重新触发回退保存 | `.gitattributes` `* text=auto` + 转换时保留原换行符；提交前 `git status` 复核 |
| pre-commit hook 在新机器上未启用，回流 | 提供 `Tools/enable_hooks.{sh,cmd}` 一行启用脚本 + README 强引导；本项目本地完成执行作为验收前置 |
| Python 端文件本期未治理，可能潜伏 GBK | 抽样已是 UTF-8；脚本默认放行 Python 业务目录避免误判；下一版立项时再扩展 `PATH` / `--ext` |
| 旧 commit `git blame` 失真 | `.git-blame-ignore-revs` |
| `ShootingEditor2D` 文件本期不转，未来若需要清理仍需处理 | 通过 `--exclude` 默认跳过；不引入对该目录的新业务依赖；最终去留作为独立动作 |

## 6. 测试建议

- **脚本单元自测**：在 `Tools/` 下加一个 `test_check_file_encoding.py`（pytest 风格但只用 unittest），覆盖：
  1. 5 种构造文件（GBK / UTF-8 / UTF-8 BOM / UTF-16 / 二进制）的判定结果。
  2. `--fix` 幂等性（多跑一次不再改文件）。
  3. `--exclude` 与默认 `--exclude` 命中（`ShootingEditor2D/` 中的 GBK 文件被跳过）。
  4. `--staged` 走 mock `git diff --cached --name-only`，验证只校验 staged 文件且尊重 `--exclude`。
- **集成验收**：
  1. `python Tools/check_file_encoding.py` 退出码 0（默认 PATH/EXT/EXCLUDE）。
  2. Unity 进第一关 → Console / Inspector / UI 中文无替换字符。
  3. 故意 VS2022 保存主工程内一个 `.cs` 为 GB2312 后 `git commit` → 拦截。
  4. 故意改 `ShootingEditor2D/` 内 GBK 文件并 `git commit` → **放行**（不在本期治理范围）。
- **不涉及**：Python 业务逻辑、LangGraph、记忆系统、Unity 运行时行为；本期无需联调 Agent。

---

## 7. 实现记录

| 日期 | 说明 |
|------|------|
| 2026-06-30 | 编码检测/修复脚本 `Tools/check_file_encoding.py` 落地（三段式判定 + `--fix` + `--staged` + 默认 PATH/EXT/EXCLUDE；UTF-8 控制台兜底；用 git 检测 repo root 避免被脚本路径绑死）。配套 `Tools/test_check_file_encoding.py`（9 个用例覆盖 5 种构造文件、`--fix` 幂等、默认 `--exclude` 跳过 `ShootingEditor2D`），单测全部通过。 |
| 2026-06-30 | 批量转换 `Src/IndependentAgentProject/Assets/Scripts/IndependentAgentProject/**/*.cs`：dry-run 报 GBK 嫌疑 44 个、UTF-8 BOM 6 个、OK 47 个；`--fix` 后复扫 GBK=0 UNKNOWN=0。`file` 命令抽查若干已转文件输出 `UTF-8 text, with CRLF line terminators`，CRLF 与字符序列保持。 |
| 2026-06-30 | 新增 `Tools/hooks/pre-commit` + `Tools/enable_hooks.sh` + `Tools/enable_hooks.cmd`；本地执行 `enable_hooks.sh` 已把 `core.hooksPath` 设为 `Tools/hooks`。手工验收：在主工程内放入合成 GBK `.cs` 并 `git add` → hook 退出 1 拦截；在 `ShootingEditor2D/` 放同样合成文件 → hook 放行（默认 `--exclude` 生效）。 |
| 2026-06-30 | 新增 `DevDocs/feature-design/项目编码基线.md`（编码基线五件套 + 新项目初始化步骤 + Windows 故障排查）。 |
| 2026-06-30 | 更新 `.cursor/rules/file-encoding.mdc`（仓库根 + Src/PythonServer/）追加「自检与拦截」段；`AGENTS.md §八` 文档索引新增编码基线条目；`README.md` 增加「克隆后第一步：启用 pre-commit」引导段。 |
| 2026-06-30 | 新增 `.git-blame-ignore-revs` 占位文件，等待批量转换 commit 合入后把 SHA 写入。 |
