import asyncio
from typing import Any, Callable, List, Union
import inspect
import threading


class IUnRegister:
    def un_register(self):
        raise NotImplementedError()


class BindableProperty:
    def __init__(self, initial_value: Any = None):
        self._value = initial_value
        self._callbacks: List[Callable[[Any], None]] = []

        # 根据是否在异步环境下决定使用哪种锁
        try:
            loop = asyncio.get_event_loop()
            self._lock = asyncio.Lock() if loop.is_running() else threading.Lock()
        except RuntimeError:
            self._lock = threading.Lock()

    @property
    def value(self) -> Any:
        return self._value

    async def aget_value(self) -> Any:
        """异步获取值"""
        return self.value

    async def aset_value(self, new_value: Any):
        """异步设置值"""
        if inspect.isawaitable(self._lock):
            async with self._lock:
                await self._set_value(new_value)
        else:
            with self._lock:
                await self._set_value(new_value)

    def set_value(self, new_value: Any):
        """同步设置值"""
        if inspect.isawaitable(self._lock):
            asyncio.run(self.aset_value(new_value))
        else:
            with self._lock:
                self._set_value_sync(new_value)

    def _set_value_sync(self, new_value: Any):
        if new_value != self._value:
            self._value = new_value
            for callback in self._callbacks:
                self._invoke_callback(callback, new_value)

    async def _set_value(self, new_value: Any):
        if new_value != self._value:
            self._value = new_value
            for callback in self._callbacks:
                self._invoke_callback(callback, new_value)

    def _invoke_callback(self, callback: Callable[[Any], None], value: Any):
        if inspect.iscoroutinefunction(callback):
            asyncio.create_task(callback(value))
        else:
            callback(value)

    def register_on_value_changed(self, callback: Callable[[Any], None]) -> IUnRegister:
        self._callbacks.append(callback)
        return BindablePropertyUnRegister(bindable_property=self, callback=callback)

    def unregister_on_value_changed(self, callback: Callable[[Any], None]):
        if callback in self._callbacks:
            self._callbacks.remove(callback)


class BindablePropertyUnRegister(IUnRegister):
    def __init__(self, bindable_property: BindableProperty, callback: Callable[[Any], None]):
        self.bindable_property = bindable_property
        self.callback = callback

    def un_register(self):
        if self.bindable_property and self.callback:
            self.bindable_property.unregister_on_value_changed(self.callback)
        self.bindable_property = None
        self.callback = None