import os, time
import asyncio

import psutil
import os

# ENABLE_PERF = os.getenv("ENABLE_PERF", "prod")  # 测试环境启动前 export ENABLE_PERF=test
ENABLE_PERF = os.getenv("ENABLE_PERF", "test")  # 测试环境启动前 export ENABLE_PERF=test

_first_call_time = None
_last_call_time = None
_node_idx = 0

def get_memory_usage():
    process = psutil.Process(os.getpid())
    mem_info = process.memory_info()
    # RSS (Resident Set Size): 实际驻留在物理内存中的字节数
    return mem_info.rss  # 单位：字节

def perf_print(message:str=""):
    """
    输出距离上一次使用perf_print的间隔时间
    """
    global _first_call_time, _last_call_time, _node_idx# 声明使用全局变量

    if ENABLE_PERF != "test":
        return None
    
    # now  = time.perf_counter()
    now  = time.time()

    if not _first_call_time:
        _first_call_time = now
    delta = now - (_last_call_time or now)
    total = now - _first_call_time
    _last_call_time = now


    log_message = f"性能节点:{_node_idx}"
    log_message += f"  距上个节点: {delta}s  距离第一个节点{total}s"
    log_message += f"  当前内存占用: {get_memory_usage() / (1024 * 1024):.2f} MB"
    log_message += f"  节点信息:{message}  " if message else ""
    print(log_message)
    _node_idx += 1

async def aperf_print(message:str=""):
    """
    输出距离上一次使用aperf_print的间隔时间、当前协程数量
    """
    global _first_call_time, _last_call_time, _node_idx# 声明使用全局变量

    if ENABLE_PERF != "test":
        return None
    
    # now  = time.perf_counter()
    now  = time.time()

    if not _first_call_time:
        _first_call_time = now
    delta = now - (_last_call_time or now)
    total = now - _first_call_time
    _last_call_time = now

    # 当前协程总数
    all_tasks = asyncio.all_tasks()

    log_message = f"性能节点:{_node_idx}"
    log_message += f"  距上个节点: {delta}s  距离第一个节点{total}s"
    log_message += f"  当前协程数: {len(all_tasks)}"
    log_message += f"  当前内存占用: {get_memory_usage() / (1024 * 1024):.2f} MB"
    log_message += f"  节点信息:{message}  " if message else ""
    print(log_message)
    _node_idx += 1

if __name__ == "__main__":
    asyncio.run(aperf_print("第一节点"))