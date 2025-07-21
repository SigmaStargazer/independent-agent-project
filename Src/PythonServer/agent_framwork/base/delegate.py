class Delegate:
    """
    委托,通过__call__触发
    """
    def __init__(self):
        self.handlers = []

    def __iadd__(self, handler):
        """模拟 += 操作符：绑定函数"""
        if handler not in self.handlers:
            self.handlers.append(handler)
        return self

    def __isub__(self, handler):
        """模拟 -= 操作符：解绑函数"""
        if handler in self.handlers:
            self.handlers.remove(handler)
        return self

    def __call__(self, *args, **kwargs):
        """模拟委托调用：触发所有绑定的函数"""
        for handler in self.handlers:
            handler(*args, **kwargs)

    def clear(self):
        """清空所有绑定"""
        self.handlers.clear()

class Event:
    """
    事件,通过.call触发
    """
    def __init__(self):
        self._handlers = []

    def __iadd__(self, handler):
        if handler not in self._handlers:
            self._handlers.append(handler)
        return self

    def __isub__(self, handler):
        if handler in self._handlers:
            self._handlers.remove(handler)
        return self

    def call(self, *args, **kwargs):
        for handler in self._handlers:
            handler(*args, **kwargs)

    def clear(self):
        self._handlers.clear()