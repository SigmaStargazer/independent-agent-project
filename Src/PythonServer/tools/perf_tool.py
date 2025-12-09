import os, time

# ENABLE_PERF = os.getenv("ENABLE_PERF", "prod")  # 测试环境启动前 export ENABLE_PERF=test
ENABLE_PERF = os.getenv("ENABLE_PERF", "test")  # 测试环境启动前 export ENABLE_PERF=test

_first_call_time = None
_last_call_time = None
_node_idx = 0

def perf_print(message:str=""):
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

    log_message = f"性能节点:{_node_idx}  "
    log_message += f"距上个节点: {delta}s  距离第一个节点{total}s"
    log_message += f"  节点信息:{message}  " if message else ""
    print(log_message)
    _node_idx += 1

if __name__ == "__main__":
    perf_print("第一节点")
    perf_print()