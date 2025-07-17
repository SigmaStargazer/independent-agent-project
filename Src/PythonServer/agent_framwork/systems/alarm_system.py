import pandas as pd
import asyncio
from datetime import timedelta
from agent_framwork.base.singleton import singleton
from agent_framwork.systems.time_system import TimeSystem

@singleton
class AlarmSystem:
    def __init__(self):
        self.alarms_df = pd.DataFrame(columns=[
            'user_id', 'alarm_id', 'hour', 'minute', 'repeat',
            'description', 'last_trigger', 'callbacks'
        ])
        self.user_counter = 0
        self.lock = asyncio.Lock()
        self.next_alarm_id = 0
        TimeSystem().register_alarm_callback(self.acheck_alarms)

    # 测试用
    async def acreate_user(self):
        async with self.lock:
            user_id = self.user_counter
            self.user_counter += 1
            return user_id

    async def aadd_alarm(self, user_id, hour, minute, repeat=False, description="无描述"):
        async with self.lock:
            alarm_id = self.next_alarm_id
            self.next_alarm_id += 1

            new_row = pd.DataFrame([{
                'user_id': user_id,
                'alarm_id': alarm_id,
                'hour': hour,
                'minute': minute,
                'repeat': repeat,
                'description': description,
                'last_trigger': None,
                'callbacks': []
            }])

            self.alarms_df = pd.concat([self.alarms_df, new_row], ignore_index=True)
            return alarm_id

    async def aadd_alarm_in(self, user_id, days=0, hours=0, minutes=0, description="无描述"):
        current_time = await TimeSystem()._get_virtual_now()
        if current_time is None:
            print("❌ 错误：时间系统未启动！请先选择菜单项 7 启动时间系统")
            return None

        trigger_time = current_time + timedelta(days=days, hours=hours, minutes=minutes)
        hour = trigger_time.hour
        minute = trigger_time.minute

        return await self.aadd_alarm(user_id, hour, minute, repeat=False, description=description)

    async def aremove_alarm(self, user_id, alarm_id):
        async with self.lock:
            condition = (self.alarms_df['user_id'] == user_id) & (self.alarms_df['alarm_id'] == alarm_id)
            if not self.alarms_df[condition].empty:
                self.alarms_df = self.alarms_df[~condition].copy()
                return True
            return False

    async def alist_alarms(self, user_id):
        async with self.lock:
            user_alarms = self.alarms_df[self.alarms_df['user_id'] == user_id]
            result = []
            for _, row in user_alarms.iterrows():
                result.append((
                    row['alarm_id'],
                    {
                        'time': (row['hour'], row['minute']),
                        'repeat': row['repeat'],
                        'description': row['description'],
                        'last_trigger': row['last_trigger'],
                        'callbacks': row['callbacks']
                    }
                ))
            return result

    async def aadd_callback_to_alarm(self, user_id, alarm_id, callback):
        async with self.lock:
            condition = (self.alarms_df['user_id'] == user_id) & (self.alarms_df['alarm_id'] == alarm_id)
            if not self.alarms_df[condition].empty:
                idx = self.alarms_df[condition].index[0]
                callbacks = self.alarms_df.at[idx, 'callbacks']
                callbacks.append(callback)
                self.alarms_df.at[idx, 'callbacks'] = callbacks
                return True
            return False

    async def acheck_alarms(self, current_time):
        async with self.lock:
            df_copy = self.alarms_df.copy()

        for _, row in df_copy.iterrows():
            user_id = row['user_id']
            alarm_id = row['alarm_id']
            hour = row['hour']
            minute = row['minute']
            repeat = row['repeat']
            description = row['description']
            last_trigger = row['last_trigger']
            callbacks = row['callbacks']

            target_time = current_time.replace(hour=hour, minute=minute, second=0, microsecond=0)

            if current_time < target_time:
                continue

            if last_trigger is None or last_trigger < target_time:
                print(f"[{current_time.strftime('%Y-%m-%d %H:%M:%S')}] USER[{user_id}] ALARM ID:{alarm_id} ({description}) 触发")

                for cb in callbacks:
                    try:
                        if asyncio.iscoroutinefunction(cb):
                            await cb(user_id, alarm_id, current_time)
                        else:
                            cb(user_id, alarm_id, current_time)
                    except Exception as e:
                        print(f"回调执行失败：{e}")

                async with self.lock:
                    condition = (self.alarms_df['user_id'] == user_id) & (self.alarms_df['alarm_id'] == alarm_id)
                    if not self.alarms_df[condition].empty:
                        idx = self.alarms_df[condition].index[0]
                        self.alarms_df.at[idx, 'last_trigger'] = target_time
                        if not repeat:
                            self.alarms_df = self.alarms_df[~condition].copy()