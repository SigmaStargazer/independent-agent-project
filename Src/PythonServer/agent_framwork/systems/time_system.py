import asyncio
from datetime import datetime, timedelta
from agent_framwork.base.singleton import singleton

@singleton
class TimeSystem:
    def __init__(self):
        self.virtual_time = None
        self.real_start_time = None
        self.speed = 1.0
        self.running = False
        self.alarm_callbacks = []
        self._task = None
        self._lock = asyncio.Lock()

    async def _atime_loop(self):
        while True:
            if not self.running:
                await asyncio.sleep(0.1)
                continue

            # 计算当前虚拟时间
            current_real_time = asyncio.get_event_loop().time()
            elapsed_real = current_real_time - self.real_start_time
            virtual_elapsed = elapsed_real * self.speed
            now = self.virtual_time + timedelta(seconds=virtual_elapsed)

            # 触发所有回调
            for callback in self.alarm_callbacks:
                try:
                    if asyncio.iscoroutinefunction(callback):
                        await callback(now)
                    else:
                        callback(now)
                except Exception as e:
                    print(f"回调执行失败: {e}")

            await asyncio.sleep(0.1)  # 控制更新频率

    def register_alarm_callback(self, callback):
        """注册一个全局回调，用于接收时间更新并检查闹钟"""
        self.alarm_callbacks.append(callback)

    async def aset_speed(self, speed):
        loop = asyncio.get_event_loop()
        loop.create_task(self._aset_speed(speed))

    async def _aset_speed(self, speed):
        async with self._lock:
            self.speed = speed
            if self.running:
                self.real_start_time = asyncio.get_event_loop().time()

    async def apause_time(self):
        async with self._lock:
            if not self.running:
                return

            # 手动计算当前虚拟时间
            current_real_time = asyncio.get_event_loop().time()
            elapsed_real = current_real_time - self.real_start_time
            virtual_elapsed = elapsed_real * self.speed
            self.virtual_time += timedelta(seconds=virtual_elapsed)
            self.running = False

    async def aresume_time(self):
        async with self._lock:
            if self.running:
                return
            self.real_start_time = asyncio.get_event_loop().time()
            self.running = True

    async def arestart_time(self, year, month, day):
        async with self._lock:
            self.running = False
            if self._task and not self._task.done():
                self._task.cancel()
                try:
                    await self._task
                except asyncio.CancelledError:
                    pass

            self.virtual_time = datetime(year, month, day)
            self.real_start_time = asyncio.get_event_loop().time()
            self.speed = 1.0
            self.running = True
            self._task = asyncio.create_task(self._atime_loop())

    async def astart_time(self, year, month, day):
        """显式启动时间系统，并设置初始虚拟时间"""
        async with self._lock:
            if self.running:
                print("时间系统已经在运行。")
                return

            self.virtual_time = datetime(year, month, day)
            self.real_start_time = asyncio.get_event_loop().time()
            self.running = True
            self._task = asyncio.create_task(self._atime_loop())

    async def aget_current_time(self, to_str = False):
        """
        获取当前虚拟时间
        Args:
            to_str: 是否返回字符串格式，默认False。
                如果为True，则返回字符串格式；否则返回datetime对象。如果未启动，则返回None。
        Returns:
            datetime: 当前虚拟时间
            str: 当前虚拟时间字符串格式，格式为/"%Y年%m月%d日 %H:%M/"。如果未启动，则返回"未启动"
        """
        async with self._lock:
            if self.virtual_time is None:
                if to_str:
                    return "未启动"
                else:
                    return None
            if not self.running:
                if to_str:
                    return self.virtual_time.strftime("%Y年%m月%d日 %H:%M")
                else:
                    return self.virtual_time
            current_real_time = asyncio.get_event_loop().time()
            elapsed_real = current_real_time - self.real_start_time
            virtual_elapsed = elapsed_real * self.speed
            current_time = self.virtual_time + timedelta(seconds=virtual_elapsed)
            if to_str:
                return current_time.strftime("%Y年%m月%d日 %H:%M")
            else:
                return current_time