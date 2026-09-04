# -*- coding: utf-8 -*-
"""单实例互斥（v0.23.3b，多开防线之一）。

随机端口设计下，第二个 Python 进程不会「绑定端口失败」而是拿到新端口，
所以不能靠端口占用检测。改用 PID 文件 + 进程存活检测：

- acquire()：读取 PID 文件，若指向的进程仍存活则判定「已有实例」，返回 False；
  否则覆盖写入自己的 PID，返回 True。启动早期调用。
- release()：退出时删除 PID 文件（仅当指向自己时），尽力而为的清理。

用 psutil 检测进程存活（psutil 已是项目依赖，见 pyproject.toml），可区分同 PID 复用，
避免「PID 被其他进程复用」时误判已有实例。
"""
import os

import psutil

from runtime.path_config import get_pid_file


def _pid_alive(pid: int) -> bool:
    """判断 pid 是否是一个存活的进程。"""
    try:
        proc = psutil.Process(pid)
        return proc.is_running() and proc.status() not in (
            psutil.STATUS_ZOMBIE,
            psutil.STATUS_DEAD,
        )
    except (psutil.NoSuchProcess, psutil.AccessDenied):
        return False


def acquire() -> bool:
    """尝试获取单实例锁。已有实例返回 False，本进程获得锁返回 True。"""
    pid_file = get_pid_file()
    try:
        os.makedirs(os.path.dirname(pid_file), exist_ok=True)
        if os.path.exists(pid_file):
            try:
                with open(pid_file, "r", encoding="utf-8") as f:
                    existing = int(f.read().strip())
                if _pid_alive(existing):
                    print(f"[single_instance] 检测到已有 Python 实例（PID={existing}），本进程退出。")
                    return False
                print(f"[single_instance] PID 文件指向的进程（{existing}）已不存在，接管单实例锁。")
            except (ValueError, OSError) as e:
                print(f"[single_instance] PID 文件读取异常，接管单实例锁: {e}")
        with open(pid_file, "w", encoding="utf-8") as f:
            f.write(str(os.getpid()))
        print(f"[single_instance] 已写入 PID 文件: {pid_file} -> {os.getpid()}")
        return True
    except OSError as e:
        print(f"[single_instance] 无法创建 PID 文件 {pid_file}，继续启动（不强制互斥）: {e}")
        return True


def release() -> None:
    """释放单实例锁：仅当 PID 文件仍指向本进程时删除（尽力而为）。"""
    pid_file = get_pid_file()
    try:
        if os.path.exists(pid_file):
            with open(pid_file, "r", encoding="utf-8") as f:
                existing = int(f.read().strip())
            if existing == os.getpid():
                os.remove(pid_file)
                print(f"[single_instance] 已删除 PID 文件: {pid_file}")
    except (ValueError, OSError):
        pass
