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
        """
        暂停时间系统。
        - 如果系统未启动，则不进行任何操作。
        - 如果系统正在运行，则暂停时间系统。
        - 暂停时间系统后，时间系统不会继续运行，直到调用aresume_time方法。
        - 暂停时间系统后，时间系统不会继续运行，直到调用aresume_time方法。
        """
        async with self._lock:
            if not self.running:
                return

            # 手动计算当前虚拟时间
            current_real_time = asyncio.get_event_loop().time()
            elapsed_real = current_real_time - self.real_start_time
            virtual_elapsed = elapsed_real * self.speed
            self.virtual_time += timedelta(seconds=virtual_elapsed)
            self.running = False

    async def areset(self):
        """暂停并归零虚拟时间（供 leave_game 调用，回 Title 回到零时间状态）。幂等。"""
        async with self._lock:
            if self.virtual_time is None and not self.running:
                return
            self.virtual_time = None
            self.real_start_time = None
            self.speed = 1.0
            self.running = False
            self.alarm_callbacks.clear()
        if self._task and not self._task.done():
            self._task.cancel()
            self._task = None

    async def aresume_time(self):
        """
        恢复时间系统。
        - 如果系统未启动，则不进行任何操作。
        - 如果系统正在运行，则恢复时间系统。
        - 恢复时间系统后，时间系统会继续运行，直到调用apause_time方法。
        - 恢复时间系统后，时间系统会继续运行，直到调用apause_time方法。
        """
        async with self._lock:
            if self.running:
                return
            self.real_start_time = asyncio.get_event_loop().time()
            self.running = True

    # async def arestart_time(self, year, month, day):
    #     async with self._lock:
    #         self.running = False
    #         if self._task and not self._task.done():
    #             self._task.cancel()
    #             try:
    #                 await self._task
    #             except asyncio.CancelledError:
    #                 pass

    #         self.virtual_time = datetime(year, month, day)
    #         self.real_start_time = asyncio.get_event_loop().time()
    #         self.speed = 1.0
    #         self.running = True
    #         self._task = asyncio.create_task(self._atime_loop())

    async def aset_time(self, year, month, day):
        """
        设置虚拟时间。
        - 如果系统未启动，仅设置时间基准。
        - 如果系统正在运行，时间会立即跳变到设定时间，并保持继续运行。
        """
        async with self._lock:
            self.virtual_time = datetime(year, month, day)
            
            # 【关键点】：如果系统正在运行，必须重置真实时间锚点
            # 这样下一帧计算时，(current_real - real_start_time) 接近 0
            # 新时间 = 设定时间 + 0，实现了瞬间跳变
            if self.running:
                self.real_start_time = asyncio.get_event_loop().time()

    async def astart_time(self):
        """
        启动时间系统。需要先调用aset_time设置时间。
        """
        async with self._lock:
            if self.running:
                print("时间系统已经在运行。")
                return

            # 兼容性处理：如果从未调用过 aset_time，virtual_time 可能为 None
            # 此时给它一个默认值，或者根据业务需求抛出异常
            if self.virtual_time is None:
                print("请先调用aset_time设置时间。")
                return

            # 启动系统，重置锚点，开始计时
            self.real_start_time = asyncio.get_event_loop().time()
            self.running = True
            # 防止重复创建 task
            if self._task is None or self._task.done():
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