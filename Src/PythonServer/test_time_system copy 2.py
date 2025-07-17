import threading
import time
from datetime import datetime, timedelta

import pandas as pd

class TimeSystem:
    def __init__(self):
        self.virtual_time = None  # 延迟设置
        self.real_start_time = None  # 延迟设置
        self.speed = 1.0
        self.running = False  # 初始不运行
        self.lock = threading.RLock()
        self.alarm_callbacks = []

        self.thread = None  # 延迟创建线程

    def _get_virtual_now(self):
        with self.lock:# with会开始时自动调用acquire()，结束时自动调用release()
            if not self.running:
                return self.virtual_time
            elapsed_real = time.time() - self.real_start_time
            virtual_elapsed = elapsed_real * self.speed
            return self.virtual_time + timedelta(seconds=virtual_elapsed)

    def _time_loop(self):
        while True:
            with self.lock:
                if not self.running:
                    break
            time.sleep(0.1)
            now = self._get_virtual_now()
            for callback in self.alarm_callbacks:
                try:
                    callback(now)
                except Exception as e:
                    print(f"回调执行失败: {e}")

    def register_alarm_callback(self, callback):
        """注册一个全局回调，用于接收时间更新并检查闹钟"""
        self.alarm_callbacks.append(callback)

    def set_speed(self, speed):
        with self.lock:
            self.speed = speed
            if self.running:
                self.real_start_time = time.time()

    def pause_time(self):
        with self.lock:
            if not self.running:
                return

            # 手动计算当前虚拟时间，避免调用 _get_virtual_now()
            elapsed_real = time.time() - self.real_start_time
            virtual_elapsed = elapsed_real * self.speed
            current_time = self.virtual_time + timedelta(seconds=virtual_elapsed)

            self.virtual_time = current_time
            self.running = False

    def resume_time(self):
        with self.lock:
            if self.running:
                return  # 已运行则不处理
            self.real_start_time = time.time()
            self.running = True

    def restart_time(self, year, month, day):
        with self.lock:
            self.running = False
            if self.thread and self.thread.is_alive():
                self.thread.join(timeout=1)  # 等待旧线程退出

            self.virtual_time = datetime(year, month, day)
            self.real_start_time = time.time()
            self.speed = 1.0
            self.running = True
            self.thread = threading.Thread(target=self._time_loop, daemon=True)
            self.thread.start()

    def start_time(self, year, month, day):
        """显式启动时间系统，并设置初始虚拟时间"""
        with self.lock:
            if self.running:
                print("时间系统已经在运行。")
                return

            # 设置初始虚拟时间
            self.virtual_time = datetime(year, month, day)
            self.real_start_time = time.time()
            self.running = True

            # 启动时间循环线程
            self.thread = threading.Thread(target=self._time_loop, daemon=True)
            self.thread.start()
            
    def get_current_time_str(self):
        with self.lock:
            if self.virtual_time is None:
                return "未启动"
            if not self.running:
                return self.virtual_time.strftime('%Y-%m-%d %H:%M:%S')
            elapsed_real = time.time() - self.real_start_time
            virtual_elapsed = elapsed_real * self.speed
            current_time = self.virtual_time + timedelta(seconds=virtual_elapsed)
            return current_time.strftime('%Y-%m-%d %H:%M:%S')

class UserAlarmManager:
    def __init__(self, time_system):
        self.time_system = time_system
        # 使用 DataFrame 存储闹钟数据
        self.alarms_df = pd.DataFrame(columns=[
            'user_id', 'alarm_id', 'hour', 'minute', 'repeat',
            'description', 'last_trigger', 'callbacks'
        ])
        self.user_counter = 0
        self.lock = threading.RLock()
        self.next_alarm_id = 0
        time_system.register_alarm_callback(self.check_alarms)

    def create_user(self):
        with self.lock:
            user_id = self.user_counter
            self.user_counter += 1
            return user_id

    def add_alarm(self, user_id, hour, minute, repeat=False, description="无描述"):
        with self.lock:
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

    def add_alarm_in(self, user_id, days=0, hours=0, minutes=0, description="无描述"):
        current_time = self.time_system._get_virtual_now()
        if current_time is None:
            print("❌ 错误：时间系统未启动！请先选择菜单项 7 启动时间系统")
            return None

        trigger_time = current_time + timedelta(days=days, hours=hours, minutes=minutes)
        hour = trigger_time.hour
        minute = trigger_time.minute

        return self.add_alarm(user_id, hour, minute, repeat=False, description=description)

    def remove_alarm(self, user_id, alarm_id):
        with self.lock:
            condition = (self.alarms_df['user_id'] == user_id) & (self.alarms_df['alarm_id'] == alarm_id)
            if not self.alarms_df[condition].empty:
                self.alarms_df = self.alarms_df[~condition].copy()
                return True
            return False

    def list_alarms(self, user_id):
        with self.lock:
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

    def add_callback_to_alarm(self, user_id, alarm_id, callback):
        with self.lock:
            condition = (self.alarms_df['user_id'] == user_id) & (self.alarms_df['alarm_id'] == alarm_id)
            if not self.alarms_df[condition].empty:
                idx = self.alarms_df[condition].index[0]
                callbacks = self.alarms_df.at[idx, 'callbacks']
                callbacks.append(callback)
                self.alarms_df.at[idx, 'callbacks'] = callbacks
                return True
            return False

    def check_alarms(self, current_time):
        with self.lock:
            # 复制当前 DataFrame 避免在迭代中修改
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
                        cb(user_id, alarm_id, current_time)
                    except Exception as e:
                        print(f"回调执行失败：{e}")

                with self.lock:
                    condition = (self.alarms_df['user_id'] == user_id) & (self.alarms_df['alarm_id'] == alarm_id)
                    if not self.alarms_df[condition].empty:
                        idx = self.alarms_df[condition].index[0]
                        self.alarms_df.at[idx, 'last_trigger'] = target_time
                        if not repeat:
                            self.alarms_df = self.alarms_df[~condition].copy()

if __name__ == "__main__":
    ts = TimeSystem()
    manager = UserAlarmManager(ts)

    users = {}
    current_user_id = None  # 当前登录用户

    # 示例回调函数
    def sample_callback(user_id, alarm_id, current_time):
        print(f"🔔 回调函数被触发！用户 {user_id} 的闹钟 {alarm_id} 时间到了：{current_time}")

    def sound_alert(*args):
        print("🔊 嘀嘀嘀...闹钟响了！")

    while True:
        print("\n=== 多用户时间系统 ===")
        print("1. 创建新用户")
        print("2. 切换用户")
        print("3. 控制时间流速")
        print("4. 暂停时间")
        print("5. 恢复时间")
        print("6. 重启时间")
        print("7. 启动时间系统")
        print("8. 添加一次性闹钟（指定多久后）")
        print("9. 添加每日闹钟")
        print("10. 查看当前用户所有闹钟")
        print("11. 为闹钟添加回调函数")  # 👈 新增菜单项
        print("12. 退出")
        print("t. 查看当前时间")
        choice = input("请选择操作：").strip()

        if choice == "1":
            user_id = manager.create_user()
            users[user_id] = user_id
            print(f"新用户已创建，ID: {user_id}")

        elif choice == "2":
            try:
                user_id = int(input("请输入用户 ID："))
                if user_id not in users:
                    print("用户不存在。")
                    continue
                current_user_id = user_id
                print(f"已切换到用户 {user_id}")
            except ValueError:
                print("请输入有效的用户 ID。")
                
        elif choice == "7":
            print("请输入起始日期：")
            try:
                year = int(input("年份："))
                month = int(input("月份："))
                day = int(input("日期："))
                ts.start_time(year, month, day)
                print(f"时间系统已启动，初始时间为 {year}-{month}-{day}")
            except ValueError:
                print("输入错误，请输入有效的数字。")

        elif choice == "8":
            if current_user_id is None:
                print("请先切换用户。")
                continue
            try:
                days = int(input("延迟天数：") or "0")
                hours = int(input("延迟小时：") or "0")
                minutes = int(input("延迟分钟：") or "0")
                desc = input("请输入闹钟描述（可为空）：").strip() or "无描述"
                aid = manager.add_alarm_in(current_user_id, days=days, hours=hours, minutes=minutes, description=desc)
                print(f"一次性闹钟已添加，ID: {aid}")
            except ValueError:
                print("请输入有效的时间参数。")

        elif choice == "9":
            if current_user_id is None:
                print("请先切换用户。")
                continue
            try:
                hour = int(input("小时："))
                minute = int(input("分钟："))
                repeat = input("是否重复？(y/n): ").strip().lower() == 'y'
                desc = input("请输入闹钟描述（可为空）：").strip() or "无描述"
                aid = manager.add_alarm(current_user_id, hour, minute, repeat, desc)
                print(f"闹钟已添加，ID: {aid}")
            except ValueError:
                print("请输入有效的时间参数。")

        elif choice == "10":
            if current_user_id is None:
                print("请先切换用户。")
                continue
            alarms = manager.list_alarms(current_user_id)
            if not alarms:
                print("该用户没有设置任何闹钟。")
            else:
                print(f"\n--- 用户 {current_user_id} 的闹钟列表 ---")
                for aid, alarm in alarms:
                    time_str = f"{alarm['time'][0]:02}:{alarm['time'][1]:02}"
                    repeat_str = "是" if alarm['repeat'] else "否"
                    cb_count = len(alarm['callbacks'])
                    description = alarm['description']
                    print(f"ID: {aid} | 时间: {time_str} | 重复: {repeat_str} | 回调数: {cb_count} | 描述: {description}")
                print("----------------------------------------")

        elif choice == "3":
            try:
                speed = float(input("请输入倍速："))
                ts.set_speed(speed)
                print(f"速度已设为 {speed} 倍速。")
            except ValueError:
                print("请输入有效数字。")

        elif choice == "4":
            ts.pause_time()
            print("时间已暂停。")

        elif choice == "5":
            ts.resume_time()
            print("时间已恢复。")

        elif choice == "6":
            print("请输入新的起始日期以重启时间：")
            try:
                year = int(input("年份："))
                month = int(input("月份："))
                day = int(input("日期："))
                ts.restart_time(year, month, day)
                print(f"时间已重置为 {year}-{month}-{day}")
            except ValueError:
                print("输入错误，请输入有效的数字。")
        elif choice == "11":  # 新增：为闹钟添加回调函数
            if current_user_id is None:
                print("请先切换用户。")
                continue
            
            alarms = manager.list_alarms(current_user_id)
            if not alarms:
                print("该用户没有设置任何闹钟。")
                continue
            
            print("\n--- 用户的闹钟列表 ---")
            for aid, alarm in alarms:
                time_str = f"{alarm['time'][0]:02}:{alarm['time'][1]:02}"
                repeat_str = "是" if alarm['repeat'] else "否"
                cb_count = len(alarm['callbacks'])
                print(f"ID: {aid} | 时间: {time_str} | 重复: {repeat_str} | 回调数: {cb_count}")
            
            try:
                alarm_id = int(input("请输入要添加回调的闹钟 ID："))
                print("选择要添加的回调函数：")
                print("1. 默认提示")
                print("2. 声音提醒")
                cb_choice = input("请输入编号：").strip()
                
                if cb_choice == "1":
                    result = manager.add_callback_to_alarm(current_user_id, alarm_id, sample_callback)
                elif cb_choice == "2":
                    result = manager.add_callback_to_alarm(current_user_id, alarm_id, sound_alert)
                else:
                    print("无效选项。")
                    continue
                
                if result:
                    print(f"回调函数已成功添加到闹钟 {alarm_id}。")
                else:
                    print(f"无法找到闹钟 ID {alarm_id} 或已存在此回调。")
                    
            except ValueError:
                print("请输入有效数字。")
                
        elif choice == "12":
            print("退出程序...")
            break

        elif choice == "t":
            print(f"当前虚拟时间：{ts.get_current_time_str()}")

        else:
            print("无效选项。")