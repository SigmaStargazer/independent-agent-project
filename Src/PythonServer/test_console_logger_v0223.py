"""v0.22.3 终端日志落盘自测脚本。

测试目标：
1. 内容一致性：stdout/stderr 均被镜像，终端与文件内容一致。
2. 异常不丢：流程中抛异常时，文件含异常前的输出。
3. 开关：CONSOLE_LOG_ENABLED=false 时不生成文件。
4. 同秒冲突：同一秒内连续两次启动，第二次带毫秒后缀不覆盖。
5. prompt 日志不受影响：确认 agent_interuptible 的 PROMPT_SAVE_DIR 常量未被改动。

运行：cd Src/PythonServer && uv run python test_console_logger_v0223.py
"""
import os
import sys
import io
import shutil
import tempfile
import traceback

# 确保能 import tools.console_logger
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tools.console_logger import start_console_logging, stop_console_logging

PASS = 0
FAIL = 0


def check(name, cond, detail=""):
    global PASS, FAIL
    if cond:
        PASS += 1
        print(f"  [PASS] {name}")
    else:
        FAIL += 1
        print(f"  [FAIL] {name}  {detail}")


def test_content_consistency():
    """1. stdout/stderr 均被镜像到文件"""
    print("\n=== 测试1: 内容一致性 ===")
    tmp = tempfile.mkdtemp()
    captured_out = io.StringIO()
    captured_err = io.StringIO()
    real_stdout, real_stderr = sys.stdout, sys.stderr
    sys.stdout = captured_out
    sys.stderr = captured_err
    f, so, se = start_console_logging(base_dir=tmp)
    try:
        print("stdout 行1")
        print("stdout 行2")
        sys.stderr.write("stderr 行1\n")
        sys.stderr.flush()
    finally:
        stop_console_logging(f, so, se)
    sys.stdout, sys.stderr = real_stdout, real_stderr

    console_out = captured_out.getvalue()
    console_err = captured_err.getvalue()
    log_files = [x for x in os.listdir(os.path.join(tmp, "logs", "console")) if x.endswith(".log")]
    check("生成1个日志文件", len(log_files) == 1, f"实际 {len(log_files)}")
    if log_files:
        with open(os.path.join(tmp, "logs", "console", log_files[0]), "r", encoding="utf-8") as fh:
            file_text = fh.read()
        check("stdout行1在文件中", "stdout 行1" in file_text, file_text[:200])
        check("stdout行2在文件中", "stdout 行2" in file_text, file_text[:200])
        check("stderr行1在文件中", "stderr 行1" in file_text, file_text[:200])
        check("终端stdout是文件子串", console_out in file_text, f"stdout={console_out!r}")
        check("终端stderr是文件子串", console_err in file_text, f"stderr={console_err!r}")
        check("文件=终端stdout+终端stderr", file_text == console_out + console_err,
              f"文件={file_text!r} 拼接={console_out + console_err!r}")
    shutil.rmtree(tmp, ignore_errors=True)


def test_exception_not_lost():
    """2. 流程中抛异常时，文件含异常前的输出"""
    print("\n=== 测试2: 异常不丢 ===")
    tmp = tempfile.mkdtemp()
    captured = io.StringIO()
    real_stdout = sys.stdout
    sys.stdout = captured
    f, so, se = start_console_logging(base_dir=tmp)
    exc_raised = False
    try:
        try:
            print("异常前输出")
            raise ValueError("测试异常")
        except ValueError:
            exc_raised = True
            # traceback 默认输出到 stderr，已被 TeeWriter 镜像
            traceback.print_exc()
    finally:
        stop_console_logging(f, so, se)
    sys.stdout = real_stdout

    check("异常被捕获", exc_raised)
    log_files = [x for x in os.listdir(os.path.join(tmp, "logs", "console")) if x.endswith(".log")]
    check("生成1个日志文件", len(log_files) == 1, f"实际 {len(log_files)}")
    if log_files:
        with open(os.path.join(tmp, "logs", "console", log_files[0]), "r", encoding="utf-8") as fh:
            file_text = fh.read()
        check("异常前输出在文件中", "异常前输出" in file_text, file_text[:300])
        check("traceback在文件中", "ValueError" in file_text and "测试异常" in file_text, file_text[-300:])
    shutil.rmtree(tmp, ignore_errors=True)


def test_disabled():
    """3. CONSOLE_LOG_ENABLED=false 时不生成文件"""
    print("\n=== 测试3: 开关关闭 ===")
    tmp = tempfile.mkdtemp()
    old = os.environ.get("CONSOLE_LOG_ENABLED")
    os.environ["CONSOLE_LOG_ENABLED"] = "false"
    try:
        f, so, se = start_console_logging(base_dir=tmp)
        check("返回 None 元组", f is None and so is None and se is None)
        # stop 空操作不应报错
        stop_console_logging(f, so, se)
        exists = os.path.exists(os.path.join(tmp, "logs", "console"))
        check("未创建 console 目录", not exists)
    finally:
        if old is None:
            os.environ.pop("CONSOLE_LOG_ENABLED", None)
        else:
            os.environ["CONSOLE_LOG_ENABLED"] = old
    shutil.rmtree(tmp, ignore_errors=True)


def test_same_second_conflict():
    """4. 同一秒内连续两次启动，第二次带毫秒后缀不覆盖"""
    print("\n=== 测试4: 同秒冲突 ===")
    tmp = tempfile.mkdtemp()
    real_stdout = sys.stdout
    sys.stdout = io.StringIO()
    f1, so1, se1 = start_console_logging(base_dir=tmp)
    print("第一次")
    stop_console_logging(f1, so1, se1)

    # 立即第二次（大概率同秒）
    f2, so2, se2 = start_console_logging(base_dir=tmp)
    print("第二次")
    stop_console_logging(f2, so2, se2)
    sys.stdout = real_stdout

    log_dir = os.path.join(tmp, "logs", "console")
    log_files = sorted(x for x in os.listdir(log_dir) if x.endswith(".log"))
    check("生成2个日志文件", len(log_files) == 2, f"实际 {len(log_files)}: {log_files}")
    if len(log_files) == 2:
        with open(os.path.join(log_dir, log_files[0]), "r", encoding="utf-8") as fh:
            t1 = fh.read()
        with open(os.path.join(log_dir, log_files[1]), "r", encoding="utf-8") as fh:
            t2 = fh.read()
        check("第一个文件含「第一次」", "第一次" in t1)
        check("第二个文件含「第二次」", "第二次" in t2)
        check("两文件内容不同", t1 != t2)
    shutil.rmtree(tmp, ignore_errors=True)


def test_prompt_log_unchanged():
    """5. prompt 日志路径常量未被改动"""
    print("\n=== 测试5: prompt日志不受影响 ===")
    # 仅验证 import 与常量存在且指向预期相对路径，不实际启动 Graphiti
    try:
        import importlib
        mod = importlib.import_module("agent_framwork.agents.agent_interuptible")
        check("PROMPT_SAVE_DIR 常量存在", hasattr(mod, "PROMPT_SAVE_DIR"))
        check("PROMPT_SAVE_DIR 以 logs/prompts 结尾",
              mod.PROMPT_SAVE_DIR.replace("\\", "/").endswith("logs/prompts"),
              mod.PROMPT_SAVE_DIR)
        check("PROMPT_SAVE_ENABLED 常量存在", hasattr(mod, "PROMPT_SAVE_ENABLED"))
    except Exception as e:
        check("import agent_interuptible 成功", False, str(e))


if __name__ == "__main__":
    test_content_consistency()
    test_exception_not_lost()
    test_disabled()
    test_same_second_conflict()
    test_prompt_log_unchanged()
    print(f"\n=== 结果: {PASS} passed, {FAIL} failed ===")
    sys.exit(1 if FAIL > 0 else 0)
