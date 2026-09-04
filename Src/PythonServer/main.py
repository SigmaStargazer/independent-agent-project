import sys
import os
import re
import asyncio
from datetime import datetime

# Windows 控制台默认 GBK 编码，项目日志含大量 emoji/中文，先重配 stdout/stderr 为 UTF-8，
# 避免 print emoji 时 UnicodeEncodeError（v0.23.0 修复）。
if hasattr(sys.stdout, "reconfigure"):
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

from network.servers import AgentServerNetMessage, TOOL_WAITERS
from network import message_pb2

from agent_framwork.managers.agent_manager import AgentManager
from agent_framwork.systems.time_system import TimeSystem
from memory_system import MemoryManager
from memory_system.action_skill_system import load_default_skills
from tools.console_logger import start_console_logging, stop_console_logging
from lifecycle import AgentLifecycle
from config.api_tester import test_api_connectivity
from runtime.path_config import (
    get_runtime_root,
    get_port_config_file,
    get_data_dir,
    get_proto_dir,
    ensure_runtime_writable,
)
from runtime.single_instance import acquire as acquire_single_instance, release as release_single_instance

# 运行根（v0.23.3b：统一走 path_config，兼容开发态 venv 与打包态 exe）
RUNTIME_ROOT = get_runtime_root()
PORT_CONFIG_FILE = get_port_config_file()

# 添加proto路径（相对运行根 Lib/proto）
sys.path.append(get_proto_dir())

# 获取单例
server = AgentServerNetMessage(port_config_file=PORT_CONFIG_FILE)

# ======================
# 定义消息处理函数
# ======================

@server.on_message(message_pb2.AgentCreateRequest)
async def handle_agent_create_request(msg, context):
    name = msg.name
    desc = msg.desc

    print(f"创建Agent: {name}: {desc}")
    response = message_pb2.AgentCreateResponse()
    if not MemoryManager().is_initialized:
        response.success = False
        response.errormsg = "记忆系统尚未初始化：请先在 Title 配置 API 并确保已发送 InitRequest（或 Python 使用 --auto-init 启动）。"
        print(f"创建Agent失败: {response.errormsg}")
        await context['server'].send_message(response, context)
        return
    try:
        cur_time = await TimeSystem().aget_current_time()
        result = await AgentManager().acreate_agent(
            name=name, 
            summary=desc,
            create_time=cur_time
            )
        # ===== 注入默认技能（PRD v0.21.0 §4.5） =====
        # 失败仅打印日志，不回滚 Agent 创建——技能注入是辅助功能
        try:
            group_id = name.encode('utf-8').hex()
            default_skills = load_default_skills(group_id=group_id)
            cur_time_str = await TimeSystem().aget_current_time(to_str=True)
            for skill_data in default_skills:
                try:
                    await MemoryManager().action_skill.create_skill_from_dict(
                        group_id=group_id,
                        skill_data=skill_data,
                        curtime=cur_time_str,
                    )
                except Exception as inner_e:
                    print(f"[main] 默认技能 '{skill_data.get('name','?')}' 注入失败: {inner_e}")
            if default_skills:
                print(f"[main] 已为 Agent '{name}' 注入 {len(default_skills)} 个默认技能")
        except Exception as e:
            print(f"[main] 默认技能注入失败（Agent 创建已完成）: {e}")

        response.success = True
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"创建Agent失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message(message_pb2.AgentLoadRequest)
async def handle_agent_load_request(msg, context):
    print("加载Agent")
    response = message_pb2.AgentLoadResponse()
    if not MemoryManager().is_initialized:
        response.success = False
        response.errormsg = "记忆系统尚未初始化：请先在 Title 配置 API 并确保已发送 InitRequest（或 Python 使用 --auto-init 启动）。"
        print(f"加载Agent失败: {response.errormsg}")
        await context['server'].send_message(response, context)
        return
    try:
        agent_names = await AgentManager().aload_agent_all()
        response.agent_names.extend(agent_names) # agent_names 的list
        response.success = True
        # response.errormsg = ""
        print(f"加载Agent成功: {agent_names}")
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"加载Agent失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message(message_pb2.SceneStartRequest)
async def handle_scene_start_request(msg, context):
    map_id = msg.map_id
    print(f"启动场景: {map_id}")
    response = message_pb2.SceneStartResponse()
    if not MemoryManager().is_initialized:
        response.success = False
        response.errormsg = "记忆系统尚未初始化：请先在 Title 配置 API 并确保已发送 InitRequest（或 Python 使用 --auto-init 启动）。"
        print(f"场景启动失败: {response.errormsg}")
        await context['server'].send_message(response, context)
        return
    try:
        # await MemoryManager().initialize()
        
        # 时间基准（aset_time）已在 enter_game 设置；此处启动时钟并设定流速
        await TimeSystem().aset_speed(1440)
        await TimeSystem().astart_time()
        
        await AgentManager().astart_all()
        response.success = True
        # response.errormsg = ""
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"场景启动失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message(message_pb2.SceneStopRequest)
async def handle_scene_stop_request(msg, context):
    print("停止场景")
    response = message_pb2.SceneStopResponse()
    try:
        print("停止时间")
        await TimeSystem().apause_time()
        print("停止 Agent...")
        await AgentManager().aremove_all()
        response.success = True
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"场景停止失败: {str(e)}")
        await context['server'].send_message(response, context)

@server.on_message(message_pb2.AgentInterruptRequest)
async def handle_agent_interrupt_request(msg, context):
    print("中断Agent")
    reason = msg.reason
    response = message_pb2.AgentInterruptResponse()
    try:
        await AgentManager().ainterrupt_all(reason=reason)
        response.success = True
        await context['server'].send_message(response, context)
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"中断Agent失败: {str(e)}")
        await context['server'].send_message(response, context)    

@server.on_message(message_pb2.InitRequest)
async def handle_init_request(msg, context):
    """初始化信号（v0.23.0）：Unity 进场景前发送，触发 Python 读 api_config.json 并初始化系统。

    收敛到 AgentLifecycle.enter_game()（v0.23.0b）：读 json 注入 env -> 初始化 MemoryManager。
    幂等：已在游戏内（已初始化）时 enter_game 内部短路，直接返回成功。
    """
    print("收到 InitRequest，开始初始化...")
    response = message_pb2.InitResponse()
    try:
        await AgentLifecycle.enter_game()
        response.success = True
        print("InitRequest 处理完成，初始化成功。")
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"初始化失败: {str(e)}")
    await context['server'].send_message(response, context)

@server.on_message(message_pb2.CloseRequest)
async def handle_close_request(msg, context):
    """关闭信号（v0.23.0b）：回 Title 时发送，触发 Python 关闭全部已初始化系统。

    收敛到 AgentLifecycle.leave_game()：停止 Agent + 清 LLM 缓存 + 归零时间 + 关闭资源。
    幂等：未初始化时 leave_game 跳过资源关闭（但 Agent/时间清理始终执行）。
    """
    print("收到 CloseRequest，开始关闭系统...")
    response = message_pb2.CloseResponse()
    try:
        await AgentLifecycle.leave_game()
        response.success = True
        print("CloseRequest 处理完成，系统已关闭。")
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"关闭系统失败: {str(e)}")
    await context['server'].send_message(response, context)

@server.on_message(message_pb2.ApiTestRequest)
async def handle_api_test_request(msg, context):
    """API 连通性测试（v0.23.1）：Title 阶段「测试后保存」触发。

    零系统：不初始化任何系统，仅用面板文本框当前值临时构造客户端发一次最小请求。
    只做转发，把 test_api_connectivity 的 (success, errormsg) 回给 Unity。
    """
    print(f"收到 ApiTestRequest: category={msg.category}, base={msg.api_base}, model={msg.model}")
    response = message_pb2.ApiTestResponse()
    try:
        success, errormsg = await test_api_connectivity(
            category=msg.category,
            api_base=msg.api_base,
            api_key=msg.api_key,
            model=msg.model,
        )
        response.success = success
        response.errormsg = errormsg
        print(f"ApiTestRequest 处理完成: success={success}, errormsg={errormsg}")
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"API 测试异常: {str(e)}")
    await context['server'].send_message(response, context)

@server.on_message(message_pb2.UserSendMessageRequest)
async def handle_user_send_msg_request(msg, context):
    agent = msg.agent
    user_message = msg.user_message
    force_interrupt = msg.force_interrupt
    try:
        # to_agent_message = f"""用户向你发送了一则消息: {user_message}"""
        to_agent_message = f"""{user_message}"""
        await AgentManager().asend_message(
            name=agent, 
            message=to_agent_message,
            force_interrupt=force_interrupt
            )
    except Exception as e:
        print(f"接收消息失败: {str(e)}")

@server.on_message(message_pb2.UserSendFeedbackRequest)
async def handle_user_send_feedback_request(msg, context):
    agent = msg.agent
    user_feedback = msg.user_feedback
    force_interrupt = msg.force_interrupt
    try:
        await AgentManager().asend_feedback(
            name=agent, 
            feedback=user_feedback, 
            force_interrupt=force_interrupt
            )
    except Exception as e:
        print(f"接收反馈失败: {str(e)}")

@server.on_message(message_pb2.UserSendMessageAllRequest)
async def handle_user_send_msg_all_request(msg, context):
    user_message = msg.user_message
    force_interrupt = msg.force_interrupt
    try:
        # to_agent_message = f"""用户向你发送了一则消息: {user_message}"""
        to_agent_message = f"""{user_message}"""
        await AgentManager().asend_message_all(
            message=to_agent_message,
            force_interrupt=force_interrupt
            )
    except Exception as e:
        print(f"接收消息失败: {str(e)}")

@server.on_message(message_pb2.SendToolResultMessageRequest)
async def handle_tool_result_request(msg, context):
    agent = msg.agent
    tool_name = msg.tool_name
    request_id = msg.request_id
    result = msg.result

    fut = TOOL_WAITERS.get(request_id)

    if fut is None:
        print(f"[TOOL_WAITERS] 未找到等待中的 request_id: {request_id} (tool={tool_name})")
        return

    if fut.done():
        print(f"[TOOL_WAITERS] request_id 已完成: {request_id}")
        return

    # 唤醒 observe_cmd / 其他工具 await
    fut.set_result(result)

    print(f"[TOOL_WAITERS] 工具回调完成: agent={agent}, tool={tool_name}, request_id={request_id}")

# ======================
# 记忆存档
# ======================

@server.on_message(message_pb2.MemoryBackupRequest)
async def handle_memory_backup_request(msg, context):
    slot_id = msg.slot_id
    response = message_pb2.MemoryBackupResponse()
    try:
        result = await MemoryManager().backup_memory(slot_id=slot_id)
        response.success = True
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"备份失败: {str(e)}")
    await context['server'].send_message(response,context)

@server.on_message(message_pb2.MemoryRestoreRequest)
async def handle_memory_restore_request(msg, context):
    slot_id = msg.slot_id
    response = message_pb2.MemoryRestoreResponse()
    try:
        print("读档...")
        result = await MemoryManager().restore_memory(slot_id=slot_id)
        response.success = True
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"读档失败: {str(e)}")
    await context['server'].send_message(response,context)

@server.on_message(message_pb2.MemoryDeleteCurrentRequest)
async def handle_memory_delete_request(msg, context):
    response = message_pb2.MemoryDeleteCurrentResponse()
    try:
        result = await MemoryManager().delete_current_memory()
        response.success = True
        print("删除当前记忆成功")
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        print(f"删除当前记忆失败: {str(e)}")
    await context['server'].send_message(response,context)


@server.on_message(message_pb2.AgentExportSkillsRequest)
async def handle_agent_export_skills_request(msg, context):
    """导出指定 Agent 的全部技能为 YAML 文件，落到 db/default_skills/exports/。

    客户端只关心 success / errormsg / skill_count；具体文件名由服务端决定，
    开发者训练完到该目录挑选文件即可。
    """
    name = msg.name
    response = message_pb2.AgentExportSkillsResponse()
    try:
        if not name:
            raise ValueError("AgentExportSkillsRequest.name 不能为空")
        group_id = name.encode("utf-8").hex()
        yaml_text = await MemoryManager().action_skill.export_skills_yaml(group_id)
        skills = await MemoryManager().action_skill.get_all_skills(group_id)

        export_dir = os.path.join(
            get_data_dir(), "default_skills", "exports"
        )
        os.makedirs(export_dir, exist_ok=True)

        ts = datetime.now().strftime("%Y%m%d_%H%M%S")
        # 仅保留中文 / 英文数字 / 下划线 / 横线
        safe_name = re.sub(r"[^\w\u4e00-\u9fa5\-]", "_", name) or "agent"
        file_path = os.path.join(export_dir, f"{safe_name}_{ts}.yaml")
        with open(file_path, "w", encoding="utf-8") as f:
            f.write(yaml_text)

        response.success = True
        response.skill_count = len(skills)
        print(f"[main] 已导出 Agent '{name}' 的 {len(skills)} 个技能到 {file_path}")
    except Exception as e:
        response.success = False
        response.errormsg = str(e)
        response.skill_count = 0
        print(f"[main] 导出 Agent '{name}' 技能失败: {e}")
    await context['server'].send_message(response, context)

# ======================
# 启动服务器
# ======================
async def other_tasks():
    print("Other tasks started")
    await asyncio.sleep(10)
    print("Other tasks done")

async def main(auto_init: bool = False):
    # 0. 打包态单实例互斥（多开防线）：已有 Python 实例则退出（多开互斥 S6）。
    #    开发态（非 frozen）也执行——同一份代码在开发期多开同样应被拦截。
    if not acquire_single_instance():
        return
    try:
        # 0.1 打包态 db/ 可写自检（方案 B 兜底）：不可写则提示后退出，避免 Kuzu 静默崩溃。
        if not ensure_runtime_writable():
            print("[main] 运行目录不可写（可能解压到了只读位置如 Program Files）。")
            print("[main] 请将游戏解压到可写目录后重试。")
            return

        # 0.2 安装终端日志镜像（stdout/stderr 同时写入 logs/console/{时间戳}.log）
        log_file, stdout_orig, stderr_orig = start_console_logging(
            base_dir=get_runtime_root()
        )
        try:
            # 1. v0.23.0：默认无 Key 启动——不初始化记忆系统，仅监听端口，等 Unity 的 InitRequest。
            #    开发期可传 --auto-init 等效 init 信号（读 api_config.json -> 初始化）。
            if auto_init:
                print("--auto-init：执行初始化（等效收到 InitRequest）")
                await AgentLifecycle.enter_game()
            else:
                print("无 Key 启动模式：等待 Unity 发送 InitRequest 后再初始化记忆系统。")

            # TimeSystem 不在启动时设置时间基准（v0.23.0b）：Title 阶段完全零状态，
            # 进游戏由 SceneStart 设置，回 Title 由 leave_game 归零。见 DevDocs/Architecture/生命周期架构.md。

            print("正在启动服务器...")
            # 2. 系统初始化完成后，再启动网络服务和其他任务
            await asyncio.gather(
                server.astart(),
                other_tasks()
            )
        finally:
            stop_console_logging(log_file, stdout_orig, stderr_orig)
    finally:
        # 释放单实例锁（尽力而为；强杀时不会执行，由下次启动的存活检测兜底）
        release_single_instance()

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Independent Agent PythonServer")
    parser.add_argument(
        "--auto-init",
        action="store_true",
        help="启动时立即读 api_config.json 并初始化记忆系统（等效收到 InitRequest）；不带则无 Key 监听，等 Unity init 信号",
    )
    args = parser.parse_args()
    asyncio.run(main(auto_init=args.auto_init))