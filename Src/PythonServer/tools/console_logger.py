import os
import sys
from datetime import datetime


class _NullStream:
    """丢弃型流：noconsole 打包 exe 中 sys.stdout/sys.stderr 为 None 时兜底。"""

    def write(self, data):
        return len(data)

    def flush(self):
        pass

    def isatty(self):
        return False

    def __getattr__(self, name):
        raise AttributeError(name)


class TeeWriter:
    """把写入镜像到原始 stream 与日志文件，每行写后即 flush。"""

    def __init__(self, original, file):
        self._original = original if original is not None else _NullStream()
        self._file = file

    def write(self, data):
        # 终端流可能为 GBK 编码（Windows 控制台），emoji/非常规字符会触发
        # UnicodeEncodeError；用 errors='replace' 兜底，保证日志镜像不阻断业务。
        try:
            self._original.write(data)
            self._original.flush()
        except UnicodeEncodeError:
            self._original.write(data.encode(self._original.encoding, errors="replace").decode(self._original.encoding))
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
    """安装 TeeWriter，把 stdout/stderr 镜像到日志文件。

    返回 (file, stdout_orig, stderr_orig)；未启用或创建失败时返回 (None, None, None)。
    """
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
        # 降级：仅终端输出，不阻断启动
        # 此时 stdout 尚未被替换，直接 print 到终端
        print(f"[console_logger] 无法创建终端日志文件 {filepath}，跳过落盘")
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
    try:
        f.flush()
        f.close()
    except Exception:
        pass
