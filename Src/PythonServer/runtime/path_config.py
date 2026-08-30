# -*- coding: utf-8 -*-
"""统一运行根目录解析层（v0.23.3b）。

解决打包态（PyInstaller exe）下 __file__ 指向 exe 内临时目录、路径推导失效的问题，
让所有模块的路径依赖收敛到一处，同时兼容开发态（venv / python.exe）与打包态（exe）。

两个「根」概念（不要混淆）：
- runtime_root（运行根）：db/、logs/、Lib/proto、config/ 的基准。
    开发态 = Src/PythonServer；打包态 = exe 同级目录（<游戏根>/PythonServer/）。
- config_root（配置根）：Data/Config/（端口文件、api_config.json）的基准。
    开发态 = Src（PythonServer 上级）；打包态 = 游戏根（exe 上级，即 runtime_root 的上级）。

两种形态的目录映射：

| 路径 | 开发态 | 打包态 |
|------|--------|--------|
| Data/Config/ | Src/Data/Config | <游戏根>/Data/Config（runtime_root 上级） |
| db/ | PythonServer/db | <PythonServer>/db（runtime_root） |
| logs/ | PythonServer/logs | <PythonServer>/logs（runtime_root） |
| Lib/proto | PythonServer/Lib/proto | <PythonServer>/Lib/proto（runtime_root） |

运行根（get_runtime_root）按优先级解析：
1. 环境变量 AGENT_SERVER_ROOT（显式指定，最高优先，便于外部指定数据目录）。
2. 打包态（sys.frozen，PyInstaller 标志）：exe 同级目录。
3. 开发态：由本文件推导 PythonServer（Src/PythonServer）。

已定（PRD §7）：zip 分发 + 数据放游戏根（方案 B）。
- 打包态启动时对 db/ 做可写自检（ensure_runtime_writable），不可写则提示玩家换目录。
"""
import os
import sys

# 运行时写数据目录名（相对运行根）
_DATA_DIR_NAME = "db"
_LOG_DIR_NAME = "logs"

_RUNTIME_ROOT = None


def get_runtime_root() -> str:
    """返回运行根目录：
    打包态=exe 同级（<游戏根>/PythonServer），开发态=PythonServer（Src/PythonServer）。
    db/、logs/、Lib/proto、config/ 都相对此根。
    """
    global _RUNTIME_ROOT
    if _RUNTIME_ROOT is not None:
        return _RUNTIME_ROOT

    # 1. 显式环境变量（最高优先）
    env = os.environ.get("AGENT_SERVER_ROOT")
    if env:
        _RUNTIME_ROOT = os.path.abspath(env)
        return _RUNTIME_ROOT

    # 2. 打包态（PyInstaller）：exe 同级目录
    if getattr(sys, "frozen", False):
        _RUNTIME_ROOT = os.path.abspath(os.path.dirname(sys.executable))
        return _RUNTIME_ROOT

    # 3. 开发态：由本文件（Src/PythonServer/runtime/path_config.py）推导 PythonServer
    _RUNTIME_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    return _RUNTIME_ROOT


def get_config_root() -> str:
    """返回配置根目录（Data/Config 的基准）：
    开发态 = Src（PythonServer 上级）；打包态 = 游戏根（runtime_root 上级）。
    两种形态下都是 runtime_root 的上级。
    """
    return os.path.abspath(os.path.join(get_runtime_root(), ".."))


def get_port_config_file() -> str:
    """返回 agent_server_port.txt 的绝对路径（相对配置根 Data/Config）。"""
    return os.path.join(get_config_root(), "Data", "Config", "agent_server_port.txt")


def get_api_config_file() -> str:
    """返回 api_config.json 的绝对路径（相对配置根 Data/Config）。"""
    return os.path.join(get_config_root(), "Data", "Config", "api_config.json")


def get_data_dir() -> str:
    """返回运行时写数据目录 db/（Kuzu 图库、备份、PID 文件、default_skills）。"""
    return os.path.join(get_runtime_root(), _DATA_DIR_NAME)


def get_log_dir() -> str:
    """返回运行时日志目录 logs/（console 日志镜像）。"""
    return os.path.join(get_runtime_root(), _LOG_DIR_NAME)


def get_pid_file() -> str:
    """返回单实例 PID 文件路径（db/agent_server.pid）。"""
    return os.path.join(get_data_dir(), "agent_server.pid")


def get_proto_dir() -> str:
    """返回 proto 生成代码目录（相对运行根 Lib/proto）。"""
    return os.path.join(get_runtime_root(), "Lib", "proto")


def ensure_runtime_writable() -> bool:
    """打包态启动自检：db/ 目录可写（试写临时文件）。

    不可写（如解压到 Program Files 只读目录）返回 False，供 Unity 提示玩家改目录。
    开发态跳过（返回 True，不干预开发工作流）。
    """
    if not getattr(sys, "frozen", False):
        return True  # 开发态跳过
    data_dir = get_data_dir()
    try:
        os.makedirs(data_dir, exist_ok=True)
        probe = os.path.join(data_dir, ".write_probe")
        with open(probe, "w") as f:
            f.write("ok")
        os.remove(probe)
        return True
    except OSError:
        return False
