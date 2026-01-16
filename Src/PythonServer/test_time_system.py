from agent_framwork.systems.time_system import TimeSystem
from agent_framwork.systems.alarm_system import AlarmSystem

if __name__ == "__main__":
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
            user_id = AlarmSystem().acreate_user()
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
                TimeSystem().astart_time(year, month, day)
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
                aid = AlarmSystem().aadd_alarm_in(current_user_id, days=days, hours=hours, minutes=minutes, description=desc)
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
                aid = AlarmSystem().aadd_alarm(current_user_id, hour, minute, repeat, desc)
                print(f"闹钟已添加，ID: {aid}")
            except ValueError:
                print("请输入有效的时间参数。")

        elif choice == "10":
            if current_user_id is None:
                print("请先切换用户。")
                continue
            alarms = AlarmSystem().alist_alarms(current_user_id)
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
                TimeSystem().aset_speed(speed)
                print(f"速度已设为 {speed} 倍速。")
            except ValueError:
                print("请输入有效数字。")

        elif choice == "4":
            TimeSystem().apause_time()
            print("时间已暂停。")

        elif choice == "5":
            TimeSystem().aresume_time()
            print("时间已恢复。")

        elif choice == "6":
            print("请输入新的起始日期以重启时间：")
            try:
                year = int(input("年份："))
                month = int(input("月份："))
                day = int(input("日期："))
                TimeSystem().arestart_time(year, month, day)
                print(f"时间已重置为 {year}-{month}-{day}")
            except ValueError:
                print("输入错误，请输入有效的数字。")
        elif choice == "11":  # 新增：为闹钟添加回调函数
            if current_user_id is None:
                print("请先切换用户。")
                continue
            
            alarms = AlarmSystem().alist_alarms(current_user_id)
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
                    result = AlarmSystem().aadd_callback_to_alarm(current_user_id, alarm_id, sample_callback)
                elif cb_choice == "2":
                    result = AlarmSystem().aadd_callback_to_alarm(current_user_id, alarm_id, sound_alert)
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
            print(f"当前虚拟时间：{TimeSystem().aget_current_time()}")

        else:
            print("无效选项。")