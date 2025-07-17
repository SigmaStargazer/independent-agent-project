import threading

# 通过@singleton注解来创建单例
def singleton(cls):
    instances = {}
    lock = threading.Lock()
    
    def wrapper(*args, **kwargs):
        if cls not in instances:
            with lock:
                if cls not in instances:
                    instances[cls] = cls(*args, **kwargs)
        return instances[cls]
    
    return wrapper