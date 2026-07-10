# 技术方案 - v0.22.3 main 运行终端日志落盘

> **状态**：已实现
> **依据 PRD**：`PRD.md`
> **最后更新**：2026-07-10

---

## 1. 方案概述

在 `main.py` 入口处用自定义 `TeeWriter` 包装 `sys.stdout` / `sys.stderr`：每行输出同时写到终端与一个日志文件。文件在 `main()` 启动时创建、退出时关闭，采用行缓冲保证异常退出不丢内容。不改任何现有 `print` 调用点，零侵入。

## 2. 影响范围

| 层级 | 模块/路径 | 变更类型 |
|------|-----------|----------|
| Python | `Src/PythonServer/main.py` | 新增：启动时安装 TeeWriter、退出时还原 |
| Python | `Src/PythonServer/tools/console_logger.py`（新增） | 新增：`TeeWriter` 类 + 启动/关闭函数 |
| Python | `Src/PythonServer/.env`（可选） | 新增：`CONSOLE_LOG_ENABLED` 环境变量说明 |
| Unity | 无 | 无 |
| 协议 | `Tools/message.proto` | 无 |

## 3. 详细设计

### 3.1 数据与协议

无协议变更。日志文件：

- 目录：`Src/PythonServer/logs/console/`
- 文件名：`{启动时间戳}.log`，格式 `%Y-%m-%d_%H-%M-%S`；同秒冲突追加 `_{毫秒3位}`。
- 编码：UTF-8。

### 3.2 Python（Brain）

#### 3.2.1 新增 `tools/console_logger.py`

核心是一个写时同时输出到「原始 stream」+「文件」的包装类，及一对启动/关闭函数。

```python
import sys
import os
from datetime import datetime

class TeeWriter:
    """把写入镜像到原始 stream 与日志文件，行缓冲。"""

    def __init__(self, original, file):
        self._original = original
        self._file = file

    def write(self, data):
        self._original.write(data)
        self._original.flush()
        self._file.write(data)
        self._file.flush()

    def flush(self):
        self._original.flush()
        self._file.flush()

    def isatty(self):
        return self._original.isatty()

    def __getattr__(self, name):
        return getattr(self._original, name)


def start_console_logging(base_dir: str):
    """安装 TeeWriter，返回 (file, stdout_orig, stderr_orig) 或 (None, None, None)。"""
    enabled = os.getenv("CONSOLE_LOG_ENABLED", "true").lower() == "true"
    if not enabled:
        return None, None, None

    now = datetime.now()
    filename = now.strftime("%Y-%m-%d_%H-%M-%S")
    log_dir = os.path.join(base_dir, "logs", "console")
    os.makedirs(log_dir, exist_ok=True)
    filepath = os.path.join(log_dir, filename + ".log")
    if os.path.exists(filepath):
        filename += f"_{now.microsecond // 1000:03d}"
        filepath = os.path.join(log_dir, filename + ".log")

    try:
        f = open(filepath, "w", encoding="utf-8")
    except OSError:
        print(f"[console_logger] 无法创建终端日志文件，跳过落盘")
        return None, None, None

    stdout_orig, stderr_orig = sys.stdout, sys.stderr
    sys.stdout = TeeWriter(stdout_orig, f)
    sys.stderr = TeeWriter(stderr_orig, f)
    return f, stdout_orig, stderr_orig


def stop_console_logging(f, stdout_orig, stderr_orig):
    """还原 stdout/stderr，关闭日志文件。参数为 None 时空操作。"""
    if f is None:
        return
    sys.stdout = stdout_orig
    sys.stderr = stderr_orig
    f.close()
```

#### 3.2.2 `main.py` 改动

在 `main()` 最开始调用 `start_console_logging`，最末尾（或 `finally`）调用 `stop_console_logging`。

```python
from tools.console_logger import start_console_logging, stop_console_logging

async def main():
    log_file, stdout_orig, stderr_orig = start_console_logging(
        base_dir=os.path.dirname(__file__)
    )
    try:
        print("正在初始化记忆系统...")
        await MemoryManager().initialize()
        # ... 其余逻辑不变 ...
    finally:
        stop_console_logging(log_file, stdout_orig, stderr_orig)

if __name__ == "__main__":
    asyncio.run(main())
```

要点：

- `start_console_logging` 在第一行 `print` 之前调用，保证「正在初始化记忆系统...」等首条输出即被记录。
- `stop_console_logging` 放在 `finally`，保证 `main` 异常退出也能还原并 flush。
- `stop_console_logging` 还原后，`asyncio.run` 本身的退出路径若再打印（如未捕获异常），仍走原始 stdout/stderr，不写入文件——这是可接受的，因为 `main` 内部异常已被 `finally` 覆盖。

### 3.3 Unity（Environment）

无改动。

### 3.4 工具 / ActionSequence

不适用。

## 4. 实现步骤

1. 新建 `Src/PythonServer/tools/console_logger.py`，实现 `TeeWriter` + `start/stop_console_logging`。
2. 修改 `Src/PythonServer/main.py`：import 并在 `main()` 头尾调用。
3. 自测：`uv run python main.py` 短暂运行后 Ctrl+C，检查 `logs/console/` 文件内容是否与终端一致、`logs/prompts/` 不受影响、`CONSOLE_LOG_ENABLED=false` 不生成文件。
4. 更新 `.env` 示例（如有）补充 `CONSOLE_LOG_ENABLED` 说明。

## 5. 风险与回退

| 风险 | 缓解 |
|------|------|
| `TeeWriter` 未代理某些 stream 属性导致第三方库报错 | `__getattr__` 透传到原始 stream；`isatty` 显式代理 |
| 文件创建失败阻断启动 | `start_console_logging` 捕获 `OSError`，降级为仅终端输出 |
| 行缓冲频繁 flush 影响性能 | 每行写后 flush 保证异常不丢；若实测有性能问题可改为缓冲 + atexit flush |
| 与 `perf_tool` 的 `print` 叠加 | 无影响，`perf_print` 走标准 `print`，会被 TeeWriter 自动镜像 |

回退方案：删除 `main.py` 中两处调用与 `tools/console_logger.py` 即可完全还原，无数据副作用。

## 6. 测试建议

> 以下为本功能开发完成后 Agent 自行编写脚本验证的内容，不依赖用户手动验证。

由于本功能不依赖 Unity 联调，属于可自测范围，开发完成后 Agent 自行验证：

1. **内容一致性**：模拟 `main` 运行，对比 `logs/console/{ts}.log` 与终端输出。
2. **异常不丢**：临时在流程中抛异常，确认文件含异常前的输出与 traceback。
3. **开关**：`CONSOLE_LOG_ENABLED=false` 时确认不生成文件、终端正常。
4. **同秒冲突**：快速连续两次启动，确认第二次文件带毫秒后缀不覆盖。
5. **prompt 日志不受影响**：确认 `logs/prompts/` 逻辑未被破坏（不实际启动 Graphiti，验证 import 与路径常量未被改动即可）。

---

## 7. 实现记录（开发完成后填写）

| 日期 | 说明 |
|------|------|
| 2026-07-10 | 新增 `tools/console_logger.py`（`TeeWriter` + `start/stop_console_logging`）；`main.py` 在 `main()` 头尾接入。自测脚本 `test_console_logger_v0223.py` 20 项断言全部通过，覆盖内容一致性、异常不丢、开关关闭、同秒冲突、prompt 日志常量未受影响。 |

---

*本文档由 Cursor Agent 根据 PRD 生成；**你确认后** Agent 方可按本方案修改代码。*
