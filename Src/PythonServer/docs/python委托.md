在 Python 中，可以通过多播委托（Multicast Delegate）的方式实现多个函数的调用。这种模式常见于事件处理或观察者模式。虽然 Python 本身没有直接的“委托”概念，但可以通过列表或其他容器存储多个回调函数，并依次调用它们来实现类似的功能。

以下是实现多播委托的几种方式：

------

### **1. 使用列表存储回调函数**

```python
复制代码class MulticastDelegate:
    def __init__(self):
        self.callbacks = []

    def add(self, callback):
        if callable(callback):
            self.callbacks.append(callback)

    def remove(self, callback):
        self.callbacks.remove(callback)

    def invoke(self, *args, **kwargs):
        for callback in self.callbacks:
            callback(*args, **kwargs)

# 示例
def callback1(message):
    print(f"Callback 1: {message}")

def callback2(message):
    print(f"Callback 2: {message}")

delegate = MulticastDelegate()
delegate.add(callback1)
delegate.add(callback2)

delegate.invoke("Hello, World!")
```

------

### **2. 使用 `functools.partial`**

如果需要传递特定参数，可以结合 `functools.partial` 使用：

```python
复制代码from functools import partial

class MulticastDelegate:
    def __init__(self):
        self.callbacks = []

    def add(self, callback):
        self.callbacks.append(callback)

    def invoke(self, *args, **kwargs):
        for callback in self.callbacks:
            callback(*args, **kwargs)

# 示例
def greet(name, message):
    print(f"{name} says: {message}")

delegate = MulticastDelegate()
delegate.add(partial(greet, "Alice"))
delegate.add(partial(greet, "Bob"))

delegate.invoke("Good morning!")
```

------

### **3. 使用 `@decorator` 简化添加**

通过装饰器的方式，可以更优雅地注册回调函数：

```python
复制代码class MulticastDelegate:
    def __init__(self):
        self.callbacks = []

    def register(self, func):
        self.callbacks.append(func)
        return func

    def invoke(self, *args, **kwargs):
        for callback in self.callbacks:
            callback(*args, **kwargs)

# 示例
delegate = MulticastDelegate()

@delegate.register
def callback1(message):
    print(f"Callback 1: {message}")

@delegate.register
def callback2(message):
    print(f"Callback 2: {message}")

delegate.invoke("Hello from decorator!")
```

------

这些方法都可以根据需求灵活调整，适用于事件广播或观察者模式的实现。